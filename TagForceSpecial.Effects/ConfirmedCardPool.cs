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
    private static readonly Lazy<DeckLegalityPolicy> LazyPolicy = new(BuildPolicy);
    private static readonly string PreviewPolicyId = InitialDeckPolicy.Create().Id, PreviewPolicyRevision = InitialDeckPolicy.Create().Revision;

    public static bool Contains(uint cardId) => Members.Contains(cardId);

    /// <summary>The card belongs in the Extra Deck by its registered rules.</summary>
    public static bool IsExtraDeck(uint cardId) => Members.Contains(cardId) && CardPool.Rules[cardId].AllowsExtraDeck;

    public static CardCategory Category(uint cardId) => CardPool.Rules[cardId].Category;

    public static DeckLegalityPolicy Policy => LazyPolicy.Value;

    /// <summary>The regulation a saved deck names: the preview policy for preview decks, otherwise this pool.</summary>
    public static DeckLegalityPolicy PolicyFor(DeckDefinition deck) => deck.PolicyId == PreviewPolicyId ? InitialDeckPolicy.Create() : Policy;

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

    private static DeckLegalityPolicy BuildPolicy() => new(PolicyId, PolicyRevision, InitialDeckPolicy.SourceBuild, CatalogId, CatalogRevision,
        TfspCardMetadata.CardInfoSha256,
        "Every card of a registered effect family except Tokens; Extra Deck monsters go in the Extra or Side Deck, others in the Main or Side Deck. " +
        $"Copy limits come from the built-in EBOOT {BuiltInRestrictions.EbootSha256[..8]} Forbidden/Limited seed at file 0x{BuiltInRestrictions.FileOffset:x} " +
        $"(table SHA-256 {BuiltInRestrictions.TableSha256}); unlisted cards allow 3. Each card ID is its own copy group; artwork variants " +
        "sharing a name are not yet merged. No active save, DLC or story override is claimed.",
        CardIds.Select(id => new DeckCardRule(id,
            (CardPool.Rules[id].AllowsExtraDeck ? DeckPlacements.Extra : DeckPlacements.Main) | DeckPlacements.Side,
            $"card:{id}", BuiltInRestrictions.CopiesAllowed(id))));
}
