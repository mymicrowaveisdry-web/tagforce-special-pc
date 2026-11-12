namespace TagForceSpecial.Core;

public enum CardIdentityLocation { Deck, Hand, MonsterZone, SpellTrapZone, FieldZone, PendulumZone, Graveyard, Banished, ExtraDeck, FaceUpExtraDeck, Material }

/// <summary>Full-information identity context for a present card; callers must enforce visibility separately.</summary>
public sealed record CardIdentityState(CardInstance Card, string PrintedName, CardIdentityLocation Location,
    PlayerId Controller, bool FaceUp, bool EffectsNegated, long PlacementId, uint? ChangedNameCardId);

/// <summary>Gives bound face-up monsters another catalog card's name; it does not copy effects or statistics.</summary>
public sealed record ChangeTargetsNameStep(uint NameCardId, bool UntilEndOfTurn = true) : EffectStep;
public sealed record CardNameChanged(CardInstance Card, uint NameCardId, string EffectId) : DuelEvent;
internal sealed record CardNameChange(CardInstance Card, long PlacementId, uint NameCardId, int LastTurn);

public sealed partial class DuelEngine
{
    public string GetOriginalCardName(uint cardId) => catalog[cardId].Name;

    public CardIdentityState? GetCardIdentityState(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        CardIdentityState Context(CardIdentityLocation location, PlayerId controller, bool faceUp = false,
            bool negated = false, long placement = 0) => new(card, catalog[card.GameCardId].Name,
                location, controller, faceUp, negated, placement,
                location == CardIdentityLocation.MonsterZone && faceUp
                    ? State.Bookkeeping.NameChanges.LastOrDefault(c => c.Card == card && c.PlacementId == placement && c.LastTurn >= State.TurnNumber)?.NameCardId : null);
        foreach (var player in State.Players)
        {
            for (int zone = 0; zone < player.MonsterZones.Count; zone++)
            {
                var monster = player.MonsterZones[zone];
                if (monster?.Card == card)
                    return Context(CardIdentityLocation.MonsterZone, player.Id, monster.Position != BattlePosition.FaceDownDefense,
                        State.EventEffects.NegatedMonsters.Contains(new MonsterBinding(player.Id, zone, card, monster.PlacementId)), monster.PlacementId);
                if (monster?.XyzMaterials.Contains(card) == true) return Context(CardIdentityLocation.Material, player.Id);
            }
            foreach (var (location, fields) in new[] {
                (CardIdentityLocation.SpellTrapZone, player.SpellTrapFields),
                (CardIdentityLocation.PendulumZone, player.PendulumZones),
                (CardIdentityLocation.FieldZone, (IReadOnlyList<FieldSpellTrap?>)new[] { player.FieldSpell }) })
                foreach (var field in fields)
                    if (field?.Card == card)
                        return Context(location, player.Id, field.IsFaceUp,
                            (location == CardIdentityLocation.PendulumZone || rules[card.GameCardId].Category == CardCategory.Spell) && IsSpellsNegated(State), field.PlacementId);
            foreach (var (location, cards) in new[] {
                (CardIdentityLocation.Deck, player.Deck), (CardIdentityLocation.Hand, player.Hand),
                (CardIdentityLocation.Graveyard, player.Graveyard), (CardIdentityLocation.Banished, player.Banished),
                (CardIdentityLocation.ExtraDeck, player.ExtraDeck), (CardIdentityLocation.FaceUpExtraDeck, player.FaceUpExtraDeck) })
                if (cards.Contains(card)) return Context(location, player.Id, location is CardIdentityLocation.Graveyard or CardIdentityLocation.Banished or CardIdentityLocation.FaceUpExtraDeck);
        }
        return null;
    }

    private static DuelState ChangeName(DuelState state, FieldCardTarget target, ChangeTargetsNameStep step, string effectId, List<DuelEvent> events)
    {
        if (target.Location != FieldLocation.MonsterZone || state.GetPlayer(target.Player).MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense }) return state;
        events.Add(new CardNameChanged(target.Card, step.NameCardId, effectId));
        return state.WithBookkeeping(state.Bookkeeping with { NameChanges = Array.AsReadOnly(state.Bookkeeping.NameChanges.Append(
            new CardNameChange(target.Card, target.PlacementId, step.NameCardId, step.UntilEndOfTurn ? state.TurnNumber : int.MaxValue)).ToArray()) });
    }

    private static DuelState PruneNameChanges(DuelState state)
    {
        if (state.Bookkeeping.NameChanges.Count == 0) return state;
        var retained = state.Bookkeeping.NameChanges.Where(c => c.LastTurn >= state.TurnNumber &&
            state.Players.Any(p => p.MonsterZones.Any(m => m is { Position: not BattlePosition.FaceDownDefense } && m.Card == c.Card && m.PlacementId == c.PlacementId))).ToArray();
        return retained.Length == state.Bookkeeping.NameChanges.Count ? state
            : state.WithBookkeeping(state.Bookkeeping with { NameChanges = Array.AsReadOnly(retained) });
    }
}
