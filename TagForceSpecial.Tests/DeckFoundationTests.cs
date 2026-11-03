using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.Tests;

public sealed class DeckFoundationTests
{
    private const string Build = "synthetic-original-era-build";
    private static CardCatalog Catalog() => new(new(new string('a', 64), "synthetic-original-era"),
        Enumerable.Range(1, 80).Select(id => new CardDefinition((uint)id, $"Synthetic {id}", "", new(1000, 1000, 4))));
    private static DeckLegalityPolicy Policy(Func<DeckCardRule, DeckCardRule>? change = null) => new("synthetic-original-era", "1",
        Build, "synthetic", "1", new string('a', 64), "Synthetic policy; not measured PSP restrictions.",
        Enumerable.Range(1, 60).Select(id => new DeckCardRule((uint)id,
            (id <= 50 ? DeckPlacements.Main : DeckPlacements.Extra) | DeckPlacements.Side, $"id:{id}", 3)).Select(c => change?.Invoke(c) ?? c));
    private static DeckDefinition Deck(IEnumerable<uint>? main = null, IEnumerable<uint>? extra = null, IEnumerable<uint>? side = null,
        string policyRevision = "1", string owner = "owner", string build = Build, int schema = 1) =>
        new("example", "1", owner, "synthetic", "1", build, "synthetic-original-era", policyRevision,
            main ?? Enumerable.Range(1, 40).Select(i => (uint)i), extra, side, schema);
    private static PlayerCollection Collection(string owner = "owner", string build = Build, int schema = 1) =>
        new(owner, "synthetic", "1", build, "1", Enumerable.Range(1, 80).ToDictionary(i => (uint)i, _ => 3), schema);
    private static DeckValidation Check(DeckDefinition deck, DeckLegalityPolicy? policy = null, PlayerCollection? collection = null) =>
        DeckValidator.Validate(deck, Catalog(), policy ?? Policy(), collection ?? Collection());

    [Fact]
    public void LegalMainExtraSideDeckAndOwnershipClassifyDeterministically()
    {
        var deck = Deck(extra: [51, 52], side: [41, 42]);
        var a = Check(deck); var b = Check(deck);
        Assert.Equal(DeckLegality.Legal, a.Legality); Assert.True(a.CanPlay); Assert.Empty(a.Diagnostics);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
    }

    [Theory]
    [InlineData("main-small", "MainDeckSize")]
    [InlineData("main-large", "MainDeckSize")]
    [InlineData("extra-large", "ExtraDeckSize")]
    [InlineData("side-large", "SideDeckSize")]
    [InlineData("normal-in-extra", "WrongSection")]
    [InlineData("extra-in-main", "WrongSection")]
    [InlineData("unknown-id", "UnknownCardId")]
    [InlineData("zero-id", "UnknownCardId")]
    public void KnownInvalidDecksAreRejected(string scenario, string code)
    {
        var main = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        var deck = scenario switch
        {
            "main-small" => Deck(main[..39]),
            "main-large" => Deck(Enumerable.Range(1, 61).Select(i => (uint)i)),
            "extra-large" => Deck(extra: Enumerable.Repeat(51u, 16)),
            "side-large" => Deck(side: Enumerable.Repeat(41u, 16)),
            "normal-in-extra" => Deck(extra: [1]),
            "extra-in-main" => Deck(main.Skip(1).Append(51u)),
            "unknown-id" => Deck(main.Skip(1).Append(999u)),
            _ => Deck(main.Skip(1).Append(0u))
        };
        var result = Check(deck);
        Assert.Equal(DeckLegality.Illegal, result.Legality); Assert.False(result.CanPlay);
        Assert.Contains(result.Diagnostics, d => d.Code == code && !d.Unknown);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(Check(deck)));
    }

    [Fact]
    public void CopiesAndOwnedQuantitiesAreCombinedAcrossAllSections()
    {
        var main = new uint[] { 1, 1, 1 }.Concat(Enumerable.Range(2, 37).Select(i => (uint)i));
        var result = Check(Deck(main, side: [1]));
        Assert.Equal(DeckLegality.Illegal, result.Legality);
        Assert.Contains(result.Diagnostics, d => d.Code == "CopyLimitExceeded" && d.CardId == 1);
        Assert.Contains(result.Diagnostics, d => d.Code == "NotOwned" && d.CardId == 1);
        var unavailable = new PlayerCollection("owner", "synthetic", "1", Build, "1", new Dictionary<uint, int>());
        Assert.Contains(Check(Deck(), collection: unavailable).Diagnostics, d => d.Code == "NotOwned");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void ForbiddenLimitedAndSemiLimitedPoliciesAreExplicit(int limit, int count)
    {
        var policy = Policy(c => c.CardId == 1 ? c with { BanlistLimit = limit } : c);
        var deck = Deck(side: Enumerable.Repeat(1u, count - 1));
        var result = Check(deck, policy);
        Assert.Equal(DeckLegality.Illegal, result.Legality);
        Assert.Contains(result.Diagnostics, d => d.Code == "BanlistLimitExceeded" && d.CardId == 1);
        var legalMain = Enumerable.Range(1, 40).Select(i => (uint)i).Where(i => i != 1).Append(41u);
        Assert.Equal(DeckLegality.Legal, Check(Deck(legalMain), policy).Legality);
    }

    [Fact]
    public void ArtworkAndAliasIdsShareExplicitOriginalEraCopyGroup()
    {
        var policy = Policy(c => c.CardId is 1 or 2 ? c with { CopyGroup = "same-original-card", BanlistLimit = 1 } : c);
        var result = Check(Deck(), policy);
        Assert.Equal(DeckLegality.Illegal, result.Legality);
        Assert.Contains(result.Diagnostics, d => d.Code == "AliasGroupLimitExceeded");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "BanlistLimitExceeded");
        Assert.Throws<ArgumentException>(() => Policy(c => c.CardId is 1 or 2 ? c with { CopyGroup = "same", BanlistLimit = (int)c.CardId } : c));
    }

    [Theory]
    [InlineData("unreviewed-card", "CardRestrictionsUnknown")]
    [InlineData("banlist", "BanlistRestrictionUnknown")]
    [InlineData("alias", "CopyGroupUnknown")]
    [InlineData("placement", "PlacementUnknown")]
    [InlineData("policy-version", "UnknownPolicy")]
    [InlineData("source-build", "CatalogOrBuildMismatch")]
    [InlineData("deck-schema", "UnknownDeckSchema")]
    public void MissingKnowledgeIsIndeterminateInsteadOfAssumingUnrestricted(string unknown, string code)
    {
        var policy = Policy(c => c.CardId != 1 ? c : unknown switch
        {
            "banlist" => c with { BanlistLimit = null }, "alias" => c with { CopyGroup = null },
            "placement" => c with { Placements = null }, _ => c
        });
        var deck = unknown switch
        {
            "unreviewed-card" => Deck(side: [70]), "policy-version" => Deck(policyRevision: "2"),
            "source-build" => Deck(build: "another-build"), "deck-schema" => Deck(schema: 2), _ => Deck()
        };
        var result = Check(deck, policy);
        Assert.Equal(DeckLegality.Indeterminate, result.Legality); Assert.False(result.CanPlay);
        Assert.Contains(result.Diagnostics, d => d.Code == code && d.Unknown);
    }

    [Fact]
    public void UnknownPolicyNeverAppliesTheSelectedPoliciesSpecificBanlist()
    {
        var forbiddenHere = Policy(c => c.CardId == 1 ? c with { BanlistLimit = 0 } : c);
        var result = Check(Deck(policyRevision: "unknown-dlc"), forbiddenHere);
        Assert.Equal(DeckLegality.Indeterminate, result.Legality);
        Assert.Equal("UnknownPolicy", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void MissingOrMismatchedOwnerCollectionDoesNotImplyOwnership()
    {
        foreach (var collection in new[] { null, Collection(owner: "other"), Collection(build: "other"), Collection(schema: 2) })
        {
            var result = DeckValidator.Validate(Deck(), Catalog(), Policy(), collection);
            Assert.Equal(DeckLegality.Indeterminate, result.Legality);
            Assert.Contains(result.Diagnostics, d => d.Code == "OwnershipUnknown");
        }
    }

    [Fact]
    public void ModelsSnapshotSectionsPoliciesAndCollectionQuantities()
    {
        var main = Enumerable.Range(1, 40).Select(i => (uint)i).ToList();
        var deck = Deck(main); main.Clear(); Assert.Equal(40, deck.Main.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)deck.Main).Clear());
        var quantities = new Dictionary<uint, int> { [1] = 3 };
        var collection = new PlayerCollection("owner", "synthetic", "1", Build, "1", quantities);
        quantities[1] = 0; Assert.Equal(3, collection.Counts[1]);
    }

    [Fact]
    public void DeckAndCollectionFilesRoundTripVersionsOrderAndOwnershipWithoutOverwriting()
    {
        string root = Path.Combine(Path.GetTempPath(), "tagforce-deck-files-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string deckPath = Path.Combine(root, "deck.json"), collectionPath = Path.Combine(root, "collection.json");
            var deck = Deck(extra: [51], side: [41]); var collection = Collection();
            DeckFiles.WriteDeck(deckPath, deck); DeckFiles.WriteCollection(collectionPath, collection);
            Assert.Equal(JsonSerializer.Serialize(deck), JsonSerializer.Serialize(DeckFiles.ReadDeck(deckPath)));
            Assert.Equal(JsonSerializer.Serialize(collection), JsonSerializer.Serialize(DeckFiles.ReadCollection(collectionPath)));
            Assert.Equal(DeckLegality.Legal, DeckValidator.Validate(DeckFiles.ReadDeck(deckPath), Catalog(), Policy(), DeckFiles.ReadCollection(collectionPath)).Legality);
            Assert.Throws<IOException>(() => DeckFiles.WriteDeck(deckPath, deck));
            File.WriteAllText(deckPath, File.ReadAllText(deckPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));
            Assert.Throws<InvalidDataException>(() => DeckFiles.ReadDeck(deckPath));
            DeckFiles.WriteDeck(Path.Combine(root, "duplicate.json"), deck);
            string duplicate = Path.Combine(root, "duplicate.json");
            File.WriteAllText(duplicate, File.ReadAllText(duplicate).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"));
            Assert.Throws<InvalidDataException>(() => DeckFiles.ReadDeck(duplicate));
            File.WriteAllText(collectionPath, File.ReadAllText(collectionPath).Replace("\"1\": 3", "\"1\": 0, \"01\": 3"));
            Assert.Throws<InvalidDataException>(() => DeckFiles.ReadCollection(collectionPath));
            File.WriteAllText(collectionPath, "{}"); Assert.Throws<InvalidDataException>(() => DeckFiles.ReadCollection(collectionPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NamedPreviewPolicyHasExplicitSourceAndDoesNotInventOtherCardRestrictions()
    {
        var policy = InitialDeckPolicy.Create();
        Assert.Equal(14, policy.Cards.Count);
        Assert.Equal(40, InitialDeckPolicy.Example().Main.Count);
        Assert.Equal(InitialDeckPolicy.SourceBuild, InitialDeckPolicy.ExampleCollection().SourceBuild);
        Assert.False(policy.Cards.ContainsKey(4844)); // A real card outside the reviewed release slice is not guessed.
        Assert.Contains("No active save/DLC/story override", policy.Evidence);
    }
}
