namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult Activate(int instanceId, int zone, int effectIndex = 0)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or > DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (definitions.ContainsKey(card.GameCardId))
            return EffectAt(card.GameCardId, effectIndex, out var definition) ? ActivateDefinition(card, zone, definition) : Reject(DuelError.UnsupportedSpellEffect);
        if (zone == DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        if (summonEffects.TryGetValue(card.GameCardId, out var summon)) return ActivateSummonEffect(card, zone, summon);
        return Reject(DuelError.UnsupportedSpellEffect);
    }

    /// <summary>
    /// End Phase entry: banishes each placement scheduled for this turn, in scheduling order. A placement that is
    /// gone (destroyed, moved or replaced) expires instead; nothing is retargeted.
    /// </summary>
    private DuelState ResolveEndPhaseBanishments(DuelState state, List<DuelEvent> events)
    {
        var due = state.PendingEndPhaseBanishments.Where(b => b.DueTurn == state.TurnNumber).ToArray();
        if (due.Length == 0) return state;
        foreach (var delayed in due)
        {
            var player = state.GetPlayer(delayed.Player);
            var monster = player.MonsterZones[delayed.MonsterZone];
            if (monster is null || monster.Card != delayed.Monster || monster.PlacementId != delayed.PlacementId)
            {
                events.Add(new EndPhaseBanishmentExpired(delayed.Player, delayed.Monster, delayed.EffectId));
                continue;
            }
            if (RuleLocked(state, LockedAction.BeBanished, delayed.Player, monster.Card, monster))
            {
                events.Add(new EndPhaseBanishmentExpired(delayed.Player, delayed.Monster, delayed.EffectId));
                continue;
            }
            var zones = player.MonsterZones.ToArray();
            zones[delayed.MonsterZone] = null;
            state = state.With(player: player.With(monsters: zones));
            var owner = state.GetPlayer(monster.Card.Owner);
            state = state.With(player: owner.With(banished: owner.Banished.Append(monster.Card)));
            events.Add(new MonsterBanished(delayed.Player, monster.Card, delayed.MonsterZone, delayed.EffectId));
        }
        return state.WithEndPhaseBanishments(state.PendingEndPhaseBanishments.Except(due));
    }
}
