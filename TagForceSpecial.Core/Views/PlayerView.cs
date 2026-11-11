using TagForceSpecial.Cards;

namespace TagForceSpecial.Core.Views;

public sealed record CardView(int? InstanceId, uint? CardId, string Name, string Description,
    int? Attack, int? Defense, BattlePosition? Position, bool Hidden);
public sealed record PlayerView(PlayerId Id, int LifePoints, int DeckCount, int ExtraDeckCount,
    IReadOnlyList<CardView> Hand, IReadOnlyList<CardView?> Monsters,
    IReadOnlyList<CardView?> SpellTraps, IReadOnlyList<CardView?> Pendulum,
    IReadOnlyList<CardView> Graveyard, IReadOnlyList<CardView> FaceUpExtraDeck,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CardView? FieldSpell = null);

/// <summary>The shared visibility boundary for human and opponent presentation.</summary>
public static class PlayerProjection
{
    public static PlayerView Create(DuelState state, PlayerId player, PlayerId viewer, CardCatalog catalog, bool handRevealed = false)
    {
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        var p = state.GetPlayer(player);
        bool own = player == viewer;
        return new(player, p.LifePoints, p.Deck.Count, p.ExtraDeck.Count,
            Read(p.Hand.Select(c => Card(c, own || handRevealed, null, catalog))),
            Read(p.MonsterZones.Select(m => m is null ? null : Card(m.Card,
                own || m.Position != BattlePosition.FaceDownDefense, m.Position, catalog))),
            Read(p.SpellTrapFields.Select(f => f is null ? null : Card(f.Card, own || f.IsFaceUp, null, catalog))),
            Read(p.PendulumZones.Select(f => f is null ? null : Card(f.Card, own || f.IsFaceUp, null, catalog))),
            Read(p.Graveyard.Select(c => Card(c, true, null, catalog))),
            Read(p.FaceUpExtraDeck.Select(c => Card(c, true, null, catalog))),
            p.FieldSpell is { } field ? Card(field.Card, own || field.IsFaceUp, null, catalog) : null);
    }

    internal static CardView Card(CardInstance card, bool visible, BattlePosition? position, CardCatalog catalog)
    {
        if (!visible) return new(null, null, "Hidden card", "", null, null, position, true);
        var data = catalog[card.GameCardId];
        return new(card.InstanceId, card.GameCardId, data.Name, data.Description,
            data.Statistics.Attack, data.Statistics.Defense, position, false);
    }

    public static IReadOnlyList<T> Read<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());
}
