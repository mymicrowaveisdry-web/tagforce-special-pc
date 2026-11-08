namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult CommitSummon(DuelState next, PlayerId player, CardInstance card, int zone,
        bool faceDown, List<DuelEvent> events)
    {
        var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player, card, zone, Other(player));
        if (!faceDown && OpensSummonWindow(next.WithSummon(pending), card.GameCardId))
        {
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
        if (pending.PendulumGroup.Count != 0)
        {
            foreach (var entry in pending.PendulumGroup)
                next = PlaceSpecialSummon(next, entry.Player, entry.Card, entry.MonsterZone, entry.Position,
                    entry.Source, events, "pendulum", entry.AttemptId, sourceAlreadyRemoved: true, timingGroup: 1);
            return next.WithSummon(null);
        }
        // The supported negation chain cannot move monsters or occupy this reserved slot.
        if (next.GetPlayer(pending.Player).MonsterZones[pending.MonsterZone] is not null)
            throw new InvalidOperationException("A pending summon destination was occupied by an unsupported operation.");
        next = pending.IsSpecialSummon
            ? PlaceSpecialSummon(next, pending.Player, pending.Card, pending.MonsterZone, pending.Position,
                pending.Source, events, pending.ProcedureId, pending.AttemptId, pending.ProperProcedureId, sourceAlreadyRemoved: true,
                timingGroup: pending.Source == DecisionCardSource.ExtraDeck ? 1 : null, xyzMaterials: pending.XyzMaterials)
            : PlaceSuccessfulSummon(next, pending.Player, pending.Card, pending.MonsterZone, false, events);
        return next.WithSummon(null);
    }

    private DuelActionResult ExecuteSummonWindow(DuelCommand command)
    {
        var pending = State.PendingSummon!;
        // A Counter Trap answering this Summon may still be choosing its cost.
        if (State.PendingDecision is not null && State.PendingSpellActivation is { } activating && definitions.ContainsKey(activating.Spell.GameCardId))
            return command is DecisionCommand decision ? AnswerDecision(decision) : Reject(DuelError.DecisionPending);
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
            return BeginSummonNegation(activate);
        List<DuelEvent> events = [new SummonResponsePassed(command.Player)];
        var updated = pending with { DecisionId = checked(State.Revision + 1), Responder = Other(pending.Responder),
            ConsecutivePasses = pending.ConsecutivePasses + 1 };
        var next = State.WithSummon(updated);
        if (updated.ConsecutivePasses == 2) next = CompletePendingSummon(next, events);
        else events.Add(new SummonResponseRequested(updated));
        return Accept(next, events.ToArray());
    }

    private DuelActionResult BeginSummonNegation(ActivateSummonNegation command)
    {
        if (command.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(command.Player);
        var source = player.SpellTrapFields[command.SpellTrapZone];
        if (source is null || !definitions.TryGetValue(source.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedSummonResponse);
        return BeginSummonDefinition(player.Id, command.SpellTrapZone, source, definition);
    }

    private DuelState ResolveSummonNegation(DuelState next, ChainLink link, List<DuelEvent> events)
    {
        var pending = next.PendingSummon;
        if (pending is null || pending.AttemptId != link.SummonAttemptId || pending.Negated)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return next;
        }
        if (pending.PendulumGroup.Count != 0)
        {
            foreach (var entry in pending.PendulumGroup)
            {
                var destination = next.GetPlayer(entry.Card.Owner);
                next = next.With(player: destination.With(graveyard: destination.Graveyard.Append(entry.Card)));
                events.Add(new SummonNegated(entry) { TimingGroup = 1 });
            }
            return next.WithSummon(pending with { Negated = true });
        }
        var owner = next.GetPlayer(pending.Card.Owner);
        events.Add(new SummonNegated(pending) { TimingGroup = 1 });
        next = next.With(player: owner.With(graveyard: owner.Graveyard.Append(pending.Card)));
        next = SendXyzMaterials(next, pending.Card, pending.XyzMaterials, false, events, 1);
        return next.WithSummon(pending with { Negated = true, XyzMaterials = Array.AsReadOnly(Array.Empty<CardInstance>()) });
    }
}
