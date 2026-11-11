namespace TagForceSpecial.Core;

/// <summary>Each bound monster gets these locks until the end of the turn; they follow the monster if it changes control.</summary>
public sealed record LockTargetsUntilEndStep(LockedAction Actions) : EffectStep;

/// <summary>The activation happens only in this phase.</summary>
public sealed record PhaseCondition(DuelPhase Phase) : EffectCondition;

/// <summary>Ends the effect unless it is its controller's turn (as a trigger's first step, also needed to activate).</summary>
public sealed record StopUnlessOwnTurnStep : EffectStep;

public sealed partial class DuelEngine
{
    /// <summary>A lock on one monster for the rest of the turn, bound by card and placement.</summary>
    private static DuelState AddBoundLock(DuelState state, PlayerId controller, LockedAction actions, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        var timed = new TimedRuleLock(controller, state.TurnNumber, new RuleLock(actions, Scope: LockScope.Self)) { BoundCard = target.Card, BoundPlacementId = target.PlacementId };
        events.Add(new RuleLockApplied(controller, effectId));
        return state.WithBookkeeping(state.Bookkeeping with
        {
            TimedLocks = Array.AsReadOnly(state.Bookkeeping.TimedLocks.Where(t => t.TurnNumber == state.TurnNumber).Append(timed).ToArray())
        });
    }

    /// <summary>A lock's "while ..." condition, relative to the lock's controller; the source itself never counts as "another".</summary>
    private bool LockConditionHolds(DuelState state, RuleLock rule, PlayerId controller, CardInstance? self, long selfPlacement)
    {
        if (rule.WhileHandEmpty && state.GetPlayer(controller).Hand.Count != 0) return false;
        return rule.WhileControlsOther is not { } other || FieldMonsters(state).Any(e => e.Binding.Player == controller &&
            e.Monster.Position != BattlePosition.FaceDownDefense && !(self is not null && e.Monster.Card == self && e.Monster.PlacementId == selfPlacement) &&
            MatchesFilter(e.Monster.Card, other));
    }
}
