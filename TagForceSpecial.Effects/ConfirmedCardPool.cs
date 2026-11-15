using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.Effects;

/// <summary>
/// The confirmed card waves as the deck pool: every card of a registered family except Tokens, with the original built-in
/// copy limits. Decks of the earlier preview regulation keep their own policy and collection.
/// </summary>
public static class ConfirmedCardPool
{
    public const string PolicyId = "tfsp-confirmed-card-pool", PolicyRevision = "1";
    private const string CatalogId = "tfsp-original", CatalogRevision = "1";

    /// <summary>Pool card IDs in ascending order.</summary>
    public static IReadOnlyList<uint> CardIds { get; } =
        Array.AsReadOnly(CardPool.Rules.Where(e => !e.Value.IsToken).Select(e => e.Key).Order().ToArray());

    private static readonly FrozenSet<uint> Members = CardIds.ToFrozenSet();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardCatalog, DeckLegalityPolicy> Policies = new();
    private static readonly string PreviewPolicyId = InitialDeckPolicy.Create().Id, PreviewPolicyRevision = InitialDeckPolicy.Create().Revision;

    public static bool Contains(uint cardId) => Members.Contains(cardId);

    /// <summary>The card belongs in the Extra Deck by its registered rules.</summary>
    public static bool IsExtraDeck(uint cardId) => Members.Contains(cardId) && CardPool.Rules[cardId].AllowsExtraDeck;

    public static CardCategory Category(uint cardId) => CardPool.Rules[cardId].Category;

    /// <summary>This pool's regulation over a catalog; alt styles (catalog records with the same card name) share one copy limit.</summary>
    public static DeckLegalityPolicy Policy(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return Policies.GetValue(catalog, BuildPolicy);
    }

    /// <summary>The regulation a saved deck names: the preview policy for preview decks, otherwise this pool.</summary>
    public static DeckLegalityPolicy PolicyFor(DeckDefinition deck, CardCatalog catalog) =>
        deck.PolicyId == PreviewPolicyId ? InitialDeckPolicy.Create() : Policy(catalog);

    /// <summary>The owner's collection for a deck's regulation; until progression exists the player owns 3 of every pool card.</summary>
    public static PlayerCollection CollectionFor(DeckDefinition deck) =>
        deck.PolicyId == PreviewPolicyId ? InitialDeckPolicy.ExampleCollection(deck.OwnerId) : Collection(deck.OwnerId);

    public static PlayerCollection Collection(string ownerId = "local-player") =>
        new(ownerId, CatalogId, CatalogRevision, InitialDeckPolicy.SourceBuild, "1", CardIds.ToDictionary(id => id, _ => 3));

    /// <summary>
    /// A preview-regulation deck as the same cards under this pool's regulation (the pool holds every preview card with the
    /// same limits). Any other deck, including one naming an unknown regulation, is returned unchanged.
    /// </summary>
    public static DeckDefinition AdoptPreview(DeckDefinition deck) =>
        deck.PolicyId != PreviewPolicyId || deck.PolicyRevision != PreviewPolicyRevision ? deck
        : new(deck.DeckId, deck.Revision, deck.OwnerId, deck.CatalogId, deck.CatalogRevision, deck.SourceBuild,
            PolicyId, PolicyRevision, deck.Main, deck.Extra, deck.Side, deck.SchemaVersion);

    /// <summary>
    /// Loads every family into one engine for two decks. The catalog must come from the reviewed source build and hold every
    /// deck card; family metadata was reviewed against that build, so a full per-family check is left to the family tests.
    /// </summary>
    public static DuelEngine CreateEngine(CardCatalog catalog, DeckDefinition first, DeckDefinition second, ulong shuffleSeed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("The confirmed card pool has not been reviewed against this source build.");
        var known = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        if (first.Entries().Concat(second.Entries()).Any(e => !known.Contains(e.CardId) || !Contains(e.CardId)))
            throw new ArgumentException("Both decks must hold only confirmed pool cards present in the catalog.");
        return CardPool.CreateEngine(catalog, first.Main, second.Main, first.Extra, second.Extra, shuffleSeed);
    }

    /// <summary>The copy group of each pool card: its catalog name, so alt styles count together; a card missing from the catalog stands alone.</summary>
    public static IReadOnlyDictionary<uint, string> CopyGroups(CardCatalog catalog)
    {
        var names = catalog.Cards.ToDictionary(c => c.GameCardId, c => c.Name);
        return CardIds.ToDictionary(id => id, id => names.TryGetValue(id, out var name) ? "name:" + name : $"card:{id}");
    }

    /// <summary>A group's limit is the strictest of its members, so a Forbidden or Limited card stays so under every alt style.</summary>
    private static DeckLegalityPolicy BuildPolicy(CardCatalog catalog)
    {
        var groups = CopyGroups(catalog);
        var limits = CardIds.GroupBy(id => groups[id]).ToDictionary(g => g.Key, g => g.Min(BuiltInRestrictions.CopiesAllowed));
        return new(PolicyId, PolicyRevision, InitialDeckPolicy.SourceBuild, CatalogId, CatalogRevision, TfspCardMetadata.CardInfoSha256,
            "Every card of a registered effect family except Tokens; Extra Deck monsters go in the Extra or Side Deck, others in the Main or Side Deck. " +
            $"Copy limits come from the built-in EBOOT {BuiltInRestrictions.EbootSha256[..8]} Forbidden/Limited seed at file 0x{BuiltInRestrictions.FileOffset:x} " +
            $"(table SHA-256 {BuiltInRestrictions.TableSha256}); unlisted cards allow 3. Alt styles (the same card name) are one card: at most " +
            "three copies in total, under the strictest limit any of them carries. No active save, DLC or story override is claimed.",
            CardIds.Select(id => new DeckCardRule(id,
                (CardPool.Rules[id].AllowsExtraDeck ? DeckPlacements.Extra : DeckPlacements.Main) | DeckPlacements.Side,
                groups[id], limits[groups[id]])));
    }
}
