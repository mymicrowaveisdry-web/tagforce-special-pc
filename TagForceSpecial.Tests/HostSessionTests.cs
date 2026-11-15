using TagForceSpecial.Core.Decks;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public sealed class SetupOperationTests
{
    [Fact]
    public async Task ValidationCompletionIsConsumedOnceAndCanBeRetried()
    {
        using var directory = new HostSessionDirectory();
        var store = new SetupStore(directory.Root);
        using var operation = new SetupOperation(store);
        Assert.Null(operation.Poll());
        operation.Validate(initial: true);
        Assert.True(operation.Busy);
        Assert.Equal("Checking existing local game data", operation.Progress.Message);
        var completion = await Complete(operation);
        Assert.False(completion.Success);
        Assert.Equal(store.Issue!.Message, completion.Message);
        Assert.False(operation.Busy); Assert.Null(operation.Poll());
        operation.Validate();
        Assert.Equal(completion, await Complete(operation));
        Assert.Null(store.Preferences.ActiveGeneration);
    }

    [Fact]
    public async Task FailedImportKeepsSourcePrivateAndAllowsAnotherOperation()
    {
        using var directory = new HostSessionDirectory();
        var store = new SetupStore(directory.Root);
        using var operation = new SetupOperation(store);
        string missing = Path.Combine(directory.Root, "private-source-name.iso");
        Assert.True(operation.Import(missing));
        var completion = await Complete(operation);
        Assert.False(completion.Success);
        Assert.DoesNotContain(missing, completion.Message);
        Assert.DoesNotContain("private-source-name", completion.Message);
        Assert.Null(store.Cache); Assert.Null(store.Preferences.SourcePath);
        Assert.False(File.Exists(Path.Combine(directory.Root, "preferences.json")));
        Assert.True(operation.Import(missing));
        Assert.Equal(completion, await Complete(operation));
    }

    private static async Task<SetupCompletion> Complete(SetupOperation operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (operation.Poll() is { } completion) return completion;
            await Task.Delay(5, timeout.Token);
        }
    }
}

[Trait("Category", "LocalReference")]
public sealed class DeckEditorSessionTests(DeckSessionFixture fixture) : IClassFixture<DeckSessionFixture>
{
    [Fact]
    public void TextModalOwnsConfirmAndCancelAndSectionEditsStayInTheDraft()
    {
        var session = new DeckEditorSession(fixture.Store()); session.Open();
        string original = session.Draft!.Name;
        Menu(session, 1); session.TypeCharacter('X');
        session.Update(default(GuiButtons) with { Back = true });
        Assert.Equal(original, session.Draft.Name); Assert.Null(session.EditingText);
        Menu(session, 1);
        foreach (char _ in original) session.TypeCharacter('\b');
        session.Update(default(GuiButtons) with { Confirm = true });
        Assert.NotNull(session.EditingText); Assert.Equal("Enter a name for this deck.", session.Notice);
        foreach (char c in new string('A', 60)) session.TypeCharacter(c);
        Assert.Equal(48, session.EditingText!.Length);
        session.Update(default(GuiButtons) with { Confirm = true });
        Assert.Equal(new string('A', 48), session.Draft.Name); Assert.False(session.MenuOpen);
        Menu(session, 2); foreach (char c in "4352") session.TypeCharacter(c);
        session.Update(default(GuiButtons) with { Confirm = true });
        Assert.Equal(4352u, Assert.Single(session.Draft.VisibleCards()).GameCardId);
        session.Update(default(GuiButtons) with { Inspect = true });
        var section = session.Draft.Section;
        int before = session.Draft.Count(4352, section);
        session.Update(default(GuiButtons) with { Right = true });
        Assert.Equal(before + 1, session.Draft.Count(4352, section));
        session.Update(default(GuiButtons) with { Left = true });
        Assert.Equal(before, session.Draft.Count(4352, section));
        Assert.Null(session.DraftKey);
        Assert.Equal(DeckEditorRequest.Back, Menu(session, 8));
    }

    [Fact]
    public void RevisionsRemainImmutableAndInvalidDraftsCannotLaunch()
    {
        var store = fixture.Store(); var session = new DeckEditorSession(store); session.Open();
        var library = new DeckLibrary(store.Storage);
        session.Save(); string first = session.DraftKey!;
        session.Draft!.Remove(session.Draft.Cards(DeckSection.Main)[0]); session.Save();
        string second = session.DraftKey!;
        Assert.NotEqual(first, second); Assert.Equal(first.Split('/')[0], second.Split('/')[0]);
        Assert.Equal(40, library.Load(first).Main.Count); Assert.Equal(39, library.Load(second).Main.Count);
        session.ReloadDraft(); Assert.Equal(39, session.Draft!.Cards(DeckSection.Main).Count);
        var blocked = session.Launch(37);
        Assert.Null(blocked.Controller); Assert.Null(blocked.SetupError);
        Assert.Equal("Fix deck legality before starting a duel.", session.Notice);
        Assert.Equal(second, session.DraftKey);
        Assert.Equal(DeckEditorRequest.SavedDecks, Menu(session, 4));
        Assert.Contains(session.SavedDecks, d => d.Key == first);
        File.WriteAllText(store.Storage.PathFor("decks/" + first), "broken");
        Menu(session, 4);
        int unreadable = session.SavedDecks.ToList().FindIndex(d => d.Key == first);
        var draft = session.Draft; session.LoadSaved(unreadable);
        Assert.Same(draft, session.Draft); Assert.Equal(second, session.DraftKey);
        Assert.Contains("could not be read", session.Notice);
    }

    [Fact]
    public void PreferenceWriteFailureRetainsPreviousSelectionAndBothRevisions()
    {
        var store = fixture.Store(); var session = new DeckEditorSession(store); session.Open();
        session.Save(); string previous = session.DraftKey!;
        var library = new DeckLibrary(store.Storage); int revisions = library.List().Count;
        session.Draft!.Name = "Retained on failure";
        using (var locked = new FileStream(store.Storage.PathFor("preferences.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(session.Save);
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(previous, session.DraftKey); Assert.Equal(previous, store.Preferences.SelectedDeck);
        Assert.Equal(previous, new SetupStore(fixture.Root).Preferences.SelectedDeck);
        Assert.Equal(revisions + 1, library.List().Count);
        Assert.NotEqual("Retained on failure", library.Load(previous).DeckId);
        Assert.Contains(library.List(), d => d.Deck?.DeckId == "Retained on failure");
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.pending-*"));
    }

    private static DeckEditorRequest Menu(DeckEditorSession session, int index)
    {
        session.Update(default(GuiButtons) with { Back = true });
        for (int i = 0; i < index; i++) session.Update(default(GuiButtons) with { Down = true });
        return session.Update(default(GuiButtons) with { Confirm = true });
    }
}

public sealed class DeckSessionFixture : IDisposable
{
    private readonly HostSessionDirectory directory = new();
    public string Root => directory.Root;
    public DeckSessionFixture()
    {
        string iso = Environment.GetEnvironmentVariable("TAGFORCE_ISO") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PPSSPP/PSP/GAME/Yu-Gi-Oh! ARC-V Tag Force Special (English v2.1).iso");
        var store = new SetupStore(Root);
        var result = store.Import(iso);
        Assert.True(result.Success, result.Issue?.Code);
    }
    public SetupStore Store()
    {
        var store = new SetupStore(Root); Assert.True(store.ValidateActive().Success);
        store.SavePreferences(store.Preferences with { SelectedDeck = null });
        return store;
    }
    public void Dispose() => directory.Dispose();
}

internal sealed class HostSessionDirectory : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("tagforce-host-session-").FullName;
    public void Dispose()
    {
        string path = Path.GetFullPath(Root);
        if (Path.GetDirectoryName(path) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(path).StartsWith("tagforce-host-session-", StringComparison.Ordinal))
            throw new InvalidOperationException("Temporary session storage is outside its owned parent.");
        PrivateStorage.Check(path); Directory.Delete(path, recursive: true);
    }
}
