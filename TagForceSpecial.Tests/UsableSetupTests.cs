using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.Game;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public sealed class UsableSetupTests
{
    [Fact]
    public void FreshSetupHasActionableFirstLaunchAndPreferencesPersistAtomically()
    {
        using var temp = new PrivateDirectory(); var setup = new GameDataStore(temp.Root);
        Assert.Null(setup.Game);
        Assert.Equal("ImportMissing", setup.Reload().Issue!.Code);
        setup.SavePreferences(setup.Preferences with { OpponentSeed = uint.MaxValue, IntegerScaling = true });
        var reload = new GameDataStore(temp.Root);
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
        var setup = new GameDataStore(temp.Root);
        Assert.Equal("PreferencesInvalid", setup.Issue!.Code);
        Assert.Null(setup.Game); Assert.Null(setup.Preferences.ActiveGeneration);
        Assert.Equal(json, File.ReadAllText(Path.Combine(temp.Root, "preferences.json")));
    }

    [Fact]
    public void ResetAfterCorruptPreferencesDoesNotOverwriteTheLastAcceptedBackup()
    {
        using var temp = new PrivateDirectory(); var original = new GameDataStore(temp.Root);
        original.SavePreferences(original.Preferences with { OpponentSeed = 42 });
        original.SavePreferences(original.Preferences with { OpponentSeed = 77 });
        File.WriteAllText(Path.Combine(temp.Root, "preferences.json"), "broken");
        var recovery = new GameDataStore(temp.Root);
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

    private static CardCatalog Catalog() => new(new(TfspCardMetadata.CardInfoSha256, "authored-setup-legality-fixture"),
        NormalSpellTests.Catalog().Cards); // Version stamp for policy fixtures.
    private static DeckDraft Draft() => new(Catalog(), InitialOpponentDecks.Create(0));
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
