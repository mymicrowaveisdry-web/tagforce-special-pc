using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.Effects;

/// <summary>Named original-era preview regulation, with no save/DLC or story-unlock override inferred.</summary>
public static class InitialDeckPolicy
{
    public const string SourceBuild = "e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707";
    public static DeckLegalityPolicy Create() => new("tfsp-built-in-seed-preview", "1", SourceBuild,
        "tfsp-original", "1", TfspCardMetadata.CardInfoSha256,
        "Bounded initial 14 IDs; original-era 40–60/15/15 and combined three-copy envelope. " +
        "Static EBOOT 8087681f seed at file 0xd27e0, table SHA-256 ea2aa9fa9c493a82374d0b64ed7628f7e5906f69178d3533c5e0c45afba09009: " +
        "none of these 14 IDs are listed. No active save/DLC/story override claim; all other card restrictions remain unknown.",
        PlayablePreview.Cards.Select(c => new DeckCardRule(c.CardId, DeckPlacements.Main | DeckPlacements.Side, $"card:{c.CardId}", 3)));

    public static DeckDefinition Example(string ownerId = "local-player")
    {
        var policy = Create();
        return new("initial-preview", "1", ownerId, policy.CatalogId, policy.CatalogRevision, SourceBuild,
            policy.Id, policy.Revision, PlayablePreview.ExampleDeck);
    }
    public static PlayerCollection ExampleCollection(string ownerId = "local-player") =>
        new(ownerId, "tfsp-original", "1", SourceBuild, "1", PlayablePreview.Cards.ToDictionary(c => c.CardId, _ => 3));
}
