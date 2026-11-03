using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

public sealed record PreviewCard(uint CardId, string Family,
    string Contract, NormalSpellProfile? Spell = null);

/// <summary>Stage 10B's explicit original-card pool. Synthetic validation and PSP fidelity are separate.</summary>
public static class PlayablePreview
{
    public const string ManifestId = "tfsp-playable-preview-10b-v1";
    public const string CardInfoSha256 = TfspCardMetadata.CardInfoSha256;
    public static IReadOnlyList<PreviewCard> Cards { get; } = Array.AsReadOnly(new PreviewCard[]
    {
        Monster(4012), Monster(4030), Monster(4032), Monster(4033),
        Monster(4071), Monster(4084), Monster(5644), Monster(6960),
        Spell(4345, NormalSpellOperation.GainLifePoints, 500),
        Spell(4348, NormalSpellOperation.GainLifePoints, 1000),
        Spell(4349, NormalSpellOperation.DamageOpponent, 200),
        Spell(4350, NormalSpellOperation.DamageOpponent, 500),
        Spell(4352, NormalSpellOperation.DamageOpponent, 800),
        Spell(4840, NormalSpellOperation.GainLifePoints, 400)
    });
    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } = Cards.ToFrozenDictionary(c => c.CardId,
        c => c.Spell is null ? new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true)
            : new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true));

    private static PreviewCard Monster(uint id) =>
        new(id, "ordinary-normal-monster",
            "Normal Summon/Set, flip, position change, basic battle and owner Graveyard movement; no card effect.");
    private static PreviewCard Spell(uint id, NormalSpellOperation operation, int amount) =>
        new(id, operation == NormalSpellOperation.GainLifePoints ? "fixed-lp-recovery" : "fixed-effect-damage",
            $"{operation}: {amount}; targetless, no cost/choice/once-per-turn restriction; own Main1/Main2 from hand or set; " +
            "Spell Speed 1 chain, negation skips effect, source destruction alone does not negate; ordinary chain cleanup.",
            new(TfspCardMetadata.Reviewed[id].Effects.References.Single(), id, operation, amount));

    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != CardInfoSha256)
            throw new InvalidDataException("Preview profiles have not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (var card in Cards)
        {
            var expected = TfspCardMetadata.Reviewed[card.CardId];
            if (!byId.TryGetValue(card.CardId, out var actual) || actual.Kind != expected.Kind ||
                actual.Effects is not { } effects || effects.State != expected.Effects.State ||
                !effects.References.SequenceEqual(expected.Effects.References) ||
                actual.Source?.ContainerSha256 is { } origin && origin != CardInfoSha256)
                throw new InvalidDataException($"Preview static metadata or effect reference differs for {card.CardId}.");
        }
    }

    /// <summary>Preview copy limit is three; original banlists and deck editing belong to later stages.</summary>
    public static DuelEngine CreateEngine(CardCatalog catalog, IEnumerable<uint> first, IEnumerable<uint> second)
    {
        ValidateCatalog(catalog);
        uint[] Deck(IEnumerable<uint> input)
        {
            ArgumentNullException.ThrowIfNull(input);
            var deck = input.Take(61).ToArray();
            if (deck.Length is < 40 or > 60 || deck.Any(id => !Rules.ContainsKey(id)) || deck.GroupBy(id => id).Any(g => g.Count() > 3))
                throw new ArgumentException("Preview decks need 40–60 cards from the named pool, at most three copies per ID.");
            return deck;
        }
        return new(catalog, Rules, Deck(first), Deck(second), normalSpellProfiles: Cards.Select(c => c.Spell).OfType<NormalSpellProfile>());
    }

    /// <summary>Ordered input for reproducible engine scenarios; the application will supply shuffling later.</summary>
    public static IReadOnlyList<uint> ExampleDeck { get; } = Array.AsReadOnly(Cards.SelectMany(c => Enumerable.Repeat(c.CardId, 3)).Take(40).ToArray());
}
