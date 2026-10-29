namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult BeginBattleResponse(PlayerId actor, int zone, int? summonedInstance, int? monsterZone,
        BattleResponseProfile profile)
    {
        var battle = State.Battle;
        if (battle is null || !profile.Windows.HasFlag(battle.Window.Timing)) return Reject(DuelError.UnsupportedBattleResponse);
        var player = State.GetPlayer(actor); var source = player.SpellTrapFields[zone];
        if (source is null || source.IsFaceUp || source.SetOnTurn >= State.TurnNumber) return Reject(DuelError.UnsupportedBattleResponse);
        var chain = State.PendingChain;
        if (chain is not null && (int)profile.SpellSpeed < (int)chain.Links[^1].SpellSpeed)
            return Reject(DuelError.ChainResponseSpeedNotAllowed);
        var attack = battle.Attack;
        MonsterBinding? binding = null; CardInstance? summon = null;
        if (profile.Operation != BattleResponseOperation.SpecialSummonFromHand)
        {
            if (attack is null || attack.Cancellation is not null) return Reject(DuelError.UnsupportedBattleResponse);
            binding = profile.Operation == BattleResponseOperation.ModifyCombatantStatistics && profile.Combatant == BattleCombatant.Defender
                ? attack.Defender : attack.Attacker;
            if (binding is null || !binding.IsStillPresent(State) || State.GetPlayer(binding.Player).MonsterZones[binding.Zone]!.BattleDestroyed)
                return Reject(DuelError.UnsupportedBattleResponse);
        }
        if (profile.Operation == BattleResponseOperation.SpecialSummonFromHand)
        {
            if (monsterZone is null or < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
            if (player.MonsterZones[monsterZone.Value] is not null) return Reject(DuelError.MonsterZoneOccupied);
            summon = player.Hand.SingleOrDefault(c => c.InstanceId == summonedInstance);
            if (summon is null || !profile.SummonableCardIds.Contains(summon.GameCardId)) return Reject(DuelError.InvalidDecisionChoice);
        }
        else if (summonedInstance is not null || monsterZone is not null) return Reject(DuelError.InvalidDecisionChoice);
        var fields = player.SpellTrapFields.ToArray(); fields[zone] = source with { IsFaceUp = true };
        var receipt = new CommittedActivation(actor, profile.EffectId, source.Card, zone, null, null);
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, source.PlacementId, ChainOperation.BattleResponse,
            profile.SpellSpeed, BattleEffect: profile, BattleAttackId: attack?.Id, BattleTarget: binding,
            BattleSummonCard: summon, BattleMonsterZone: monsterZone);
        var nextChain = new PendingChain(checked(State.Revision + 1), (chain?.Links ?? []).Append(link), Other(actor), freezeOnCompletion: false);
        var window = battle.Window with { ConsecutivePasses = 0, Responder = Other(actor) };
        return Accept(State.With(player: player.With(spellTraps: fields)).WithBattle(battle with { Window = window }).WithChain(nextChain),
            new ChainResponseActivated(actor, profile.EffectId, source.Card, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(nextChain.Responder, nextChain.DecisionId));
    }

    private DuelState ResolveBattleResponse(DuelState next, ChainLink link, List<DuelEvent> events)
    {
        var profile = link.BattleEffect!; var battle = next.Battle!; var attack = battle.Attack;
        if (profile.Operation == BattleResponseOperation.SpecialSummonFromHand)
        {
            var player = next.GetPlayer(link.Activation.Player); var card = link.BattleSummonCard!; int zone = link.BattleMonsterZone!.Value;
            if (!player.Hand.Contains(card) || player.MonsterZones[zone] is not null)
            {
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable)); return next;
            }
            var monsters = player.MonsterZones.ToArray();
            var monster = new FieldMonster(card, profile.SummonPosition, SummonedOnTurn: State.TurnNumber,
                PlacementId: checked(State.Revision + 1));
            monsters[zone] = monster;
            events.Add(new MonsterSpecialSummoned(player.Id, monster, zone, DecisionCardSource.Hand));
            return next.With(player: player.With(hand: player.Hand.Where(c => c != card), monsters: monsters));
        }
        if (attack is null || link.BattleAttackId != attack.Id || link.BattleTarget is not { } target || !target.IsStillPresent(next))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable)); return next;
        }
        switch (profile.Operation)
        {
            case BattleResponseOperation.NegateAttack:
                if (attack.Cancellation is null)
                {
                    events.Add(new AttackCancelled(attack.Id, BattleAttackCancellation.Negated));
                    next = next.WithBattle(battle with { Attack = attack with { Cancellation = BattleAttackCancellation.Negated } });
                }
                break;
            case BattleResponseOperation.DestroyAttacker:
            {
                var player = next.GetPlayer(target.Player); var monsters = player.MonsterZones.ToArray(); monsters[target.Zone] = null;
                events.Add(new MonsterDestroyed(player.Id, target.Zone, target.Card));
                next = next.With(player: player.With(monsters: monsters, graveyard: player.Graveyard.Append(target.Card)));
                break;
            }
            case BattleResponseOperation.ModifyCombatantStatistics:
            {
                var effects = next.EventEffects;
                var modifier = new StatModifier(profile.EffectId, null, target, profile.AttackDelta, profile.DefenseDelta,
                    checked(State.Revision + 1), State.TurnNumber, ExpiresAfterAttackId: attack.Id,
                    SpellTrapSource: new(link.Activation.Player, link.Activation.SpellTrapZone, link.Activation.Spell, link.SourcePlacementId));
                events.Add(new MonsterModifierCreated(modifier));
                next = next.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
                break;
            }
        }
        return next;
    }
}
