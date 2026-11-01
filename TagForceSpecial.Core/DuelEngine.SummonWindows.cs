namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult CommitSummon(DuelState next, PlayerId player, CardInstance card, int zone,
        bool faceDown, List<DuelEvent> events)
    {
        if (!faceDown && summonWindows.Contains(card.GameCardId))
        {
            var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1),
                player, card, zone, Other(player));
            events.Add(new SummonAttempted(pending) { TimingGroup = 1 });
            events.Add(new SummonResponseRequested(pending));
            return Accept(next.WithSummon(pending), events.ToArray());
        }
        next = PlaceSuccessfulSummon(next, player, card, zone, faceDown, events);
        return Accept(next, events.ToArray());
    }

    private DuelState PlaceSuccessfulSummon(DuelState next, PlayerId playerId, CardInstance card, int zone,
        bool faceDown, List<DuelEvent> events)
    {
        var player = next.GetPlayer(playerId);
        var monster = NewSummonedMonster(next, card, faceDown ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack);
        var monsters = player.MonsterZones.ToArray();
        monsters[zone] = monster;
        events.Add(faceDown ? new MonsterSet(playerId, monster, zone) { TimingGroup = 1 }
            : new MonsterNormalSummoned(playerId, monster, zone, next.PendingSummon?.AttemptId) { TimingGroup = 1 });
        return next.With(player: player.With(monsters: monsters));
    }

    private DuelState CompletePendingSummon(DuelState next, List<DuelEvent> events)
    {
        var pending = next.PendingSummon!;
        if (pending.Negated) return next.WithSummon(null);
        // The supported negation chain cannot move monsters or occupy this reserved slot.
        if (next.GetPlayer(pending.Player).MonsterZones[pending.MonsterZone] is not null)
            throw new InvalidOperationException("A pending summon destination was occupied by an unsupported operation.");
        next = pending.IsSpecialSummon
            ? PlaceSpecialSummon(next, pending.Player, pending.Card, pending.MonsterZone, pending.Position,
                pending.Source, events, pending.ProcedureId, pending.AttemptId, pending.ProperProcedureId, sourceAlreadyRemoved: true,
                timingGroup: pending.Source == DecisionCardSource.ExtraDeck ? 1 : null)
            : PlaceSuccessfulSummon(next, pending.Player, pending.Card, pending.MonsterZone, false, events);
        return next.WithSummon(null);
    }

    private DuelActionResult ExecuteSummonWindow(DuelCommand command)
    {
        var pending = State.PendingSummon!;
        long id = command switch
        {
            PassSummonResponse p => p.DecisionId,
            ActivateSummonNegation a => a.DecisionId,
            _ => -1
        };
        if (id == -1) return Reject(DuelError.SummonWindowPending);
        if (id != pending.DecisionId) return Reject(DuelError.StaleSummonDecision);
        if (command.Player != pending.Responder) return Reject(DuelError.NotSummonResponder);
        if (command is ActivateSummonNegation activate)
            return BeginSummonNegation(pending, activate);
        List<DuelEvent> events = [new SummonResponsePassed(command.Player)];
        var updated = pending with { DecisionId = checked(State.Revision + 1), Responder = Other(pending.Responder),
            ConsecutivePasses = pending.ConsecutivePasses + 1 };
        var next = State.WithSummon(updated);
        if (updated.ConsecutivePasses == 2) next = CompletePendingSummon(next, events);
        else events.Add(new SummonResponseRequested(updated));
        return Accept(next, events.ToArray());
    }

    private DuelActionResult BeginSummonNegation(PendingSummon pending, ActivateSummonNegation command)
    {
        if (command.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(command.Player);
        var source = player.SpellTrapFields[command.SpellTrapZone];
        if (command.Player == pending.Player || source is null || source.IsFaceUp || source.SetOnTurn >= State.TurnNumber ||
            !summonNegations.TryGetValue(source.Card.GameCardId, out var profile) ||
            !profile.Kinds.HasFlag(pending.IsSpecialSummon ? SummonNegationKinds.Special : SummonNegationKinds.Normal))
            return Reject(DuelError.UnsupportedSummonResponse);
        if (player.LifePoints <= profile.LifePointCost) return Reject(DuelError.InsufficientLifePoints);
        var slots = player.SpellTrapFields.ToArray();
        slots[command.SpellTrapZone] = source with { IsFaceUp = true };
        var receipt = new CommittedActivation(player.Id, profile.EffectId, source.Card, command.SpellTrapZone, null, null);
        var link = new ChainLink(1, receipt, source.PlacementId, ChainOperation.SummonNegation, ChainSpellSpeed.Three,
            SummonAttemptId: pending.AttemptId);
        var chain = new PendingChain(checked(State.Revision + 1), [link], Other(player.Id), freezeOnCompletion: false);
        var next = State.With(player: player.With(spellTraps: slots, lifePoints: player.LifePoints - profile.LifePointCost)).WithChain(chain);
        List<DuelEvent> events = [new ChainResponseActivated(player.Id, profile.EffectId, source.Card, command.SpellTrapZone)];
        if (profile.LifePointCost != 0) events.Add(new SummonNegationCostPaid(player.Id, profile.EffectId, profile.LifePointCost));
        events.Add(new ActivationCommitted(receipt));
        events.Add(new ChainLinkAdded(link));
        events.Add(new ChainResponseRequested(chain.Responder, chain.DecisionId));
        return Accept(next, events.ToArray());
    }

    private DuelState ResolveSummonNegation(DuelState next, ChainLink link, List<DuelEvent> events)
    {
        var pending = next.PendingSummon;
        if (pending is null || pending.AttemptId != link.SummonAttemptId || pending.Negated)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return next;
        }
        var owner = next.GetPlayer(pending.Card.Owner);
        events.Add(new SummonNegated(pending) { TimingGroup = 1 });
        next = next.With(player: owner.With(graveyard: owner.Graveyard.Append(pending.Card)));
        return next.WithSummon(pending with { Negated = true });
    }
}
