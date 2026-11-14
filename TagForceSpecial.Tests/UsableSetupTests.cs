using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public sealed class UsableSetupTests
{
    [Fact]
    public void FreshSetupHasActionableFirstLaunchAndPreferencesPersistAtomically()
    {
        using var temp = new PrivateDirectory(); var setup = new SetupStore(temp.Root);
        Assert.Null(setup.Cache);
        Assert.Equal("CacheMissing", setup.ValidateActive().Issue!.Code);
        setup.SavePreferences(setup.Preferences with { OpponentSeed = uint.MaxValue, IntegerScaling = true });
        var reload = new SetupStore(temp.Root);
        Assert.Null(reload.Issue);
        Assert.Equal(uint.MaxValue, reload.Preferences.OpponentSeed);
        Assert.True(reload.Preferences.IntegerScaling);
        Assert.Empty(Directory.EnumerateFiles(temp.Root, "*.pending-*"));
        reload.RestorePreferences();
        Assert.Equal(7u, reload.Preferences.OpponentSeed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":99,\"activeGeneration\":null,\"sourcePath\":null,\"selectedDeck\":null,\"opponentSeed\":7,\"integerScaling\":false}")]
    public void MalformedOrUnsupportedPreferencesDoNotSelectAnotherCache(string json)
    {
        using var temp = new PrivateDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "preferences.json"), json);
        var setup = new SetupStore(temp.Root);
        Assert.Equal("PreferencesInvalid", setup.Issue!.Code);
        Assert.Null(setup.Cache); Assert.Null(setup.Preferences.ActiveGeneration);
        Assert.Equal(json, File.ReadAllText(Path.Combine(temp.Root, "preferences.json")));
    }

    [Fact]
    public void ResetAfterCorruptPreferencesDoesNotOverwriteTheLastAcceptedBackup()
    {
        using var temp = new PrivateDirectory(); var original = new SetupStore(temp.Root);
        original.SavePreferences(original.Preferences with { OpponentSeed = 42 });
        original.SavePreferences(original.Preferences with { OpponentSeed = 77 });
        File.WriteAllText(Path.Combine(temp.Root, "preferences.json"), "broken");
        var recovery = new SetupStore(temp.Root);
        Assert.Equal("PreferencesInvalid", recovery.Issue!.Code);
        recovery.SavePreferences(new Preferences());
        recovery.RestorePreferences();
        Assert.Equal(42u, recovery.Preferences.OpponentSeed);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    public void PrivateKeysCannotTraverseOutOfTheOwnedRoot(string key)
    {
        using var temp = new PrivateDirectory(); var storage = new PrivateStorage(temp.Root);
        Assert.Throws<InvalidDataException>(() => storage.PathFor(key));
    }

    [Fact]
    public void MissingAndWrongBuildSourcesNeverChangePreferencesOrExposeTheirPaths()
    {
        using var temp = new PrivateDirectory(); var setup = new SetupStore(temp.Root);
        string missing = Path.Combine(temp.Root, "private-name.iso");
        var absent = setup.Import(missing);
        Assert.Equal("SourceUnavailable", absent.Issue!.Code);
        Assert.DoesNotContain(missing, absent.Issue.Message);
        File.WriteAllBytes(missing, [1, 2, 3]);
        var unsupported = setup.Import(missing);
        Assert.Equal("SourceLengthMismatch", unsupported.Issue!.Code);
        Assert.Null(setup.Preferences.ActiveGeneration);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(missing));
        Assert.False(File.Exists(Path.Combine(temp.Root, "preferences.json")));
    }

    [Fact]
    public void CancelBeforeStartingAndRestartWorkAreNonDestructive()
    {
        using var temp = new PrivateDirectory(); var setup = new SetupStore(temp.Root);
        var cancelled = setup.Import("not-opened.iso", cancellation: new CancellationToken(true));
        Assert.Equal("ImportCancelled", cancelled.Issue!.Code);
        string work = setup.Storage.PathFor("work/original.txt"); Directory.CreateDirectory(Path.GetDirectoryName(work)!);
        File.WriteAllText(work, "retained checkpoint");
        setup.RestartWork();
        Assert.False(Directory.Exists(Path.Combine(temp.Root, "work")));
        string retained = Assert.Single(Directory.EnumerateFiles(Path.Combine(temp.Root, "retained"), "original.txt", SearchOption.AllDirectories));
        Assert.Equal("retained checkpoint", File.ReadAllText(retained));
        Assert.Null(setup.Preferences.ActiveGeneration);
    }

    [Fact]
    public void SourceHashValidationCanBeCancelledAndClosesItsReadOnlyHandle()
    {
        using var fixture = new FoundationFixture();
        Assert.Throws<OperationCanceledException>(() => GameSourceValidator.Open(fixture.Iso, fixture.Profile(), new CancellationToken(true)));
        using var read = File.OpenRead(fixture.Iso);
        Assert.True(read.Length > 0);
    }

    [Fact]
    public void DraftEditingSearchFilterAndSnapshotsStaySeparateFromDeckStorage()
    {
        var draft = Draft(); var before = draft.Snapshot();
        draft.Search = "warwolf";
        Assert.Equal(6960u, Assert.Single(draft.VisibleCards()).GameCardId);
        draft.Search = "4352"; Assert.Equal("Ookazi", Assert.Single(draft.VisibleCards()).Name);
        draft.Search = ""; draft.Filter = DeckFilter.Monster;
        Assert.Equal(8, draft.VisibleCards().Count);
        draft.Filter = DeckFilter.Spell; Assert.Equal(6, draft.VisibleCards().Count);
        uint card = draft.Cards(DeckSection.Main)[0];
        Assert.True(draft.Remove(card)); Assert.Equal(39, draft.Cards(DeckSection.Main).Count);
        Assert.Equal(40, before.Main.Count); Assert.False(draft.Validate().CanPlay);
        Assert.True(draft.Add(card)); Assert.True(draft.Validate().CanPlay);
        Assert.False(draft.Add(90001));
    }

    [Theory]
    [InlineData("initial", true, null)]
    [InlineData("too-small", false, "MainDeckSize")]
    [InlineData("four-copies", false, "CopyLimitExceeded")]
    [InlineData("wrong-extra", false, "WrongSection")]
    [InlineData("combined-side", false, "CopyLimitExceeded")]
    [InlineData("forty-two", true, null)]
    public void EditorLegalityFixturesUseReviewedOriginalEraCounts(string scenario, bool canPlay, string? code)
    {
        var draft = Draft();
        if (scenario == "too-small") draft.Remove(4012);
        if (scenario == "four-copies") draft.Add(4012);
        if (scenario == "wrong-extra") { draft.Section = DeckSection.Extra; draft.Add(4840); }
        if (scenario == "combined-side") { draft.Section = DeckSection.Side; draft.Add(4012); }
        if (scenario == "forty-two") { draft.Add(4840); draft.Add(4840); }
        var legality = draft.Validate();
        Assert.Equal(canPlay, legality.CanPlay);
        if (code is not null) Assert.Contains(legality.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public void SaveReloadAndRevisionSelectionPreserveOlderAndInvalidDrafts()
    {
        using var temp = new PrivateDirectory(); var library = new DeckLibrary(new(temp.Root));
        var draft = Draft(); draft.Name = "My supported deck";
        string original = library.Save(draft.Snapshot("first"));
        draft.Remove(4012);
        string next = library.Save(draft.Snapshot("second"), original);
        Assert.Equal(40, library.Load(original).Main.Count);
        Assert.Equal(39, library.Load(next).Main.Count);
        var listed = library.List(); Assert.Equal(2, listed.Count);
        Assert.All(listed, d => Assert.NotNull(d.Deck));
        File.WriteAllText(Path.Combine(temp.Root, "decks", next.Replace('/', Path.DirectorySeparatorChar)), "broken");
        Assert.Contains(library.List(), d => d.Key == next && d.Error is not null);
        string malformedKey = next.Split('/')[0] + "/x.json";
        File.WriteAllText(Path.Combine(temp.Root, "decks", malformedKey.Replace('/', Path.DirectorySeparatorChar)), "broken");
        Assert.Contains(library.List(), d => d.Key == malformedKey && d.Error is not null);
        Assert.Equal(40, library.Load(original).Main.Count);
        Assert.Throws<InvalidDataException>(() => library.Load("../../anything.json"));
    }

    [Fact]
    public void UnknownRegulationAndUnreviewedCardsRemainBlockedAfterReload()
    {
        var source = InitialOpponentDecks.Create(0);
        var unknown = new DeckDefinition(source.DeckId, "1", source.OwnerId, source.CatalogId, source.CatalogRevision,
            source.SourceBuild, source.PolicyId, "unreviewed", source.Main);
        Assert.Equal(DeckLegality.Indeterminate, new DeckDraft(Catalog(), unknown).Validate().Legality);
        var outside = new DeckDefinition(source.DeckId, "1", source.OwnerId, source.CatalogId, source.CatalogRevision,
            source.SourceBuild, source.PolicyId, source.PolicyRevision, source.Main.Take(39).Append(90001u));
        Assert.False(new DeckDraft(Catalog(), outside).Validate().CanPlay);
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalIsoImportsResumesRebuildsAndLaunchesASavedDeckWithoutConvertedInput()
    {
        using var temp = new PrivateDirectory(); var setup = new SetupStore(temp.Root);
        string iso = Environment.GetEnvironmentVariable("TAGFORCE_ISO") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PPSSPP/PSP/GAME/Yu-Gi-Oh! ARC-V Tag Force Special (English v2.1).iso");
        using var cancel = new CancellationTokenSource();
        var interrupted = setup.Import(iso, new CallbackProgress(p => { if (p.Percent == 35) cancel.Cancel(); }), cancel.Token);
        Assert.Equal("ImportCancelled", interrupted.Issue!.Code);
        Assert.Null(setup.Preferences.ActiveGeneration);
        var reloaded = new SetupStore(temp.Root);
        Assert.Equal(Path.GetFullPath(iso), reloaded.ResumeSource());
        var imported = reloaded.Import(reloaded.ResumeSource()!);
        Assert.True(imported.Success, imported.Issue?.Code);
        Assert.Equal(7254, reloaded.Cache!.Catalog.Catalog.Count);
        Assert.All(PlayablePreview.Cards, c => Assert.Contains(reloaded.Resources!.Cards, r => r.CardId == c.CardId));
        string originalGeneration = reloaded.Preferences.ActiveGeneration!;
        var draft = new DeckDraft(reloaded.Cache.Catalog.Catalog, InitialOpponentDecks.Create(2)) { Name = "Saved through setup" };
        uint card = draft.Cards(DeckSection.Main)[0]; draft.Remove(card); draft.Add(card);
        Assert.True(draft.Validate().CanPlay);
        var library = new DeckLibrary(reloaded.Storage); string saved = library.Save(draft.Snapshot("saved"));
        reloaded.SavePreferences(reloaded.Preferences with { SelectedDeck = saved, OpponentSeed = 42, SourcePath = Path.Combine(temp.Root, "disconnected.iso") });
        var cached = new SetupStore(temp.Root);
        Assert.True(cached.ValidateActive().Success); Assert.Equal(42u, cached.Preferences.OpponentSeed);
        var session = SupportedDuelFactory.Create(cached.Cache!, library.Load(saved), cached.Preferences.OpponentSeed);
        for (int step = 0; step < 6000 && session.View.Outcome is null; step++)
            Assert.True(session.View.Choices.Count > 0 ? session.Choose(session.View.Revision, 0) : session.StepOpponent());
        Assert.NotNull(session.View.Outcome);
        reloaded.Storage.Write($"caches/{originalGeneration}/resources.json", reloaded.Resources! with { Converter = "tampered" });
        Assert.Equal("ResourceIndexMismatch", reloaded.ValidateActive().Issue!.Code);
        Assert.Null(reloaded.Cache);
        Assert.True(reloaded.Import(iso).Success);
        Assert.NotEqual(originalGeneration, reloaded.Preferences.ActiveGeneration);
        Assert.True(Directory.Exists(reloaded.Storage.PathFor($"caches/{originalGeneration}")));
        Assert.Equal(40, library.Load(saved).Main.Count);
        string active = reloaded.Preferences.ActiveGeneration!;
        File.WriteAllBytes(reloaded.Storage.PathFor($"caches/{active}/cache/assets/cardh_j.cip"), [0]);
        Assert.False(reloaded.ValidateActive().Success);
        Assert.True(reloaded.Import(iso).Success);
        Assert.Equal(40, library.Load(saved).Main.Count);
        using var original = File.OpenRead(iso);
        Assert.Equal(GameBuildProfile.English21.ImageSha256, Convert.ToHexStringLower(SHA256.HashData(original)));
    }

    private static CardCatalog Catalog() => new(new(TfspCardMetadata.CardInfoSha256, "authored-setup-legality-fixture"),
        NormalSpellTests.Catalog().Cards); // Version stamp for policy fixtures.
    private static DeckDraft Draft() => new(Catalog(), InitialOpponentDecks.Create(0));
    private sealed class CallbackProgress(Action<ImportProgress> callback) : IProgress<ImportProgress>
    { public void Report(ImportProgress value) => callback(value); }
    private sealed class PrivateDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("tagforce-setup-").FullName;
        public void Dispose()
        {
            string path = Path.GetFullPath(Root), parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (Path.GetDirectoryName(path) != parent || !Path.GetFileName(path).StartsWith("tagforce-setup-", StringComparison.Ordinal))
                throw new InvalidOperationException("Temporary setup path is outside its owned parent.");
            PrivateStorage.Check(path); Directory.Delete(path, recursive: true);
        }
    }
}
