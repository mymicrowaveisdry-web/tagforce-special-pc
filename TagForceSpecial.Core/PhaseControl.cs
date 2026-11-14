namespace TagForceSpecial.Core;

/// <summary>
/// The players on <paramref name="Player"/> skip their next <paramref name="Count"/> occurrences of <paramref name="Phase"/>
/// (Draw, Standby, Battle or Main Phase 2), counted from the turn after this one.
/// </summary>
public sealed record SkipPhaseStep(DuelPhase Phase, TargetSide Player = TargetSide.Own, int Count = 1) : EffectStep;

/// <summary>Ends the Battle Phase: any attack in progress ends and the battle moves to its End Step.</summary>
public sealed record EndBattlePhaseStep : EffectStep;

/// <summary>The activating player has at least <paramref name="Amount"/> LP fewer than the opponent.</summary>
public sealed record LifePointsBehindCondition(int Amount) : EffectCondition;

/// <summary>Ends the effect unless its source monster was Special Summoned (as a trigger's first step, also needed to activate).</summary>
public sealed record StopUnlessSpecialSummonedStep : EffectStep;

/// <summary>Sends the trigger's source monster from the field to the Graveyard; it is not destroyed.</summary>
public sealed record SendSourceToGraveyardStep : EffectStep;

public sealed record PhaseSkipped(PlayerId Player, DuelPhase Phase) : DuelEvent;

internal sealed record PhaseSkip(PlayerId Player, DuelPhase Phase, int FromTurn);

public sealed partial class DuelEngine
{
    private static DuelState AddPhaseSkips(DuelState state, PlayerId controller, SkipPhaseStep step)
    {
        PlayerId[] players = step.Player switch
        {
            TargetSide.Own => [controller],
            TargetSide.Opponent => [Other(controller)],
            _ => [controller, Other(controller)]
        };
        // Skips of the same phase do not add up: the player skips the most any one effect asked for (Reckless Greed ruling).
        var existing = state.Bookkeeping.PhaseSkips;
        var added = players.SelectMany(p => Enumerable.Repeat(new PhaseSkip(p, step.Phase, state.TurnNumber),
            Math.Max(0, step.Count - existing.Count(s => s.Player == p && s.Phase == step.Phase))));
        return state.WithBookkeeping(state.Bookkeeping with { PhaseSkips = Array.AsReadOnly([.. existing, .. added]) });
    }

    private static bool OneTimeSkipPending(DuelState state, PlayerId player, DuelPhase phase) =>
        state.Bookkeeping.PhaseSkips.Any(s => s.Player == player && s.Phase == phase && s.FromTurn < state.TurnNumber);

    /// <summary>True when the player skips this phase now: a pending one-time skip, or a face-up card that skips it.</summary>
    private bool PhaseSkipPending(DuelState state, PlayerId player, DuelPhase phase) =>
        OneTimeSkipPending(state, player, phase) ||
        phase == DuelPhase.Battle && RuleLocked(state, LockedAction.EnterBattlePhase, player, null) ||
        phase == DuelPhase.Main2 && RuleLocked(state, LockedAction.EnterMainPhase2, player, null);

    /// <summary>Records that the phase was skipped and uses up one one-time skip, if one was pending.</summary>
    private static DuelState ConsumePhaseSkip(DuelState state, PlayerId player, DuelPhase phase, List<DuelEvent> events)
    {
        events.Add(new PhaseSkipped(player, phase));
        var skips = state.Bookkeeping.PhaseSkips.ToList();
        int index = skips.FindIndex(s => s.Player == player && s.Phase == phase && s.FromTurn < state.TurnNumber);
        if (index < 0) return state;
        skips.RemoveAt(index);
        return state.WithBookkeeping(state.Bookkeeping with { PhaseSkips = Array.AsReadOnly(skips.ToArray()) });
    }

    /// <summary>On reaching the End Phase, the turn player's skipped Battle Phase and Main Phase 2 count as skipped.</summary>
    private DuelState SpendTurnSkips(DuelState state, List<DuelEvent> events)
    {
        foreach (var phase in new[] { DuelPhase.Battle, DuelPhase.Main2 })
            if (PhaseSkipPending(state, state.ActivePlayer, phase)) state = ConsumePhaseSkip(state, state.ActivePlayer, phase, events);
        return state;
    }

    /// <summary>The battle jumps to its End Step; an attack in progress ends.</summary>
    private DuelState EndBattlePhase(DuelState state, List<DuelEvent> events)
    {
        if (state.Battle is not { } battle || battle.Step == BattleStep.End) return state;
        if (battle.Attack is { Cancellation: null } attack) events.Add(new AttackCancelled(attack.Id, BattleAttackCancellation.Negated));
        events.Add(new BattleStepChanged(battle.Step, BattleStep.End));
        return state.WithBattle(battle with { Step = BattleStep.End, DamageStep = null, Attack = null, Window = NewBattleWindow(BattleTiming.End, state.ActivePlayer) });
    }

    private static bool WasSpecialSummoned(DuelState state, MonsterBinding monster) =>
        state.SummonHistory.Any(h => h.Card == monster.Card && h.PlacementId == monster.PlacementId && h.Status == SummonStatus.Completed &&
            h.Method is SummonMethod.SpecialEffect or SummonMethod.SpecialProcedure or SummonMethod.Pendulum or SummonMethod.Token);

    private DuelState SendSourceToGraveyard(DuelState state, ChainLink link, List<DuelEvent> events)
    {
        if (link.Trigger?.Source is not { } source || !source.IsStillPresent(state))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceLeftField));
            return state;
        }
        return SendFieldCardToGraveyard(RemovePlacement(state, new(source.Player, FieldLocation.MonsterZone, source.Zone, source.Card, source.PlacementId)),
            source.Card, new MonsterSentToGraveyardByRule(source.Player, source.Zone, source.Card, source.PlacementId), events);
    }

    /// <summary>
    /// The ways this hand monster can be Tribute Summoned: its registered procedures, then the ordinary count by Level.
    /// The engine still checks every offer.
    /// </summary>
    internal IEnumerable<(string? ProcedureId, int Count, bool AllowsSet)> TributeOptions(DuelState state, PlayerId player, CardInstance card)
    {
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category != CardCategory.Monster || !rule.AllowsNormalSummon || !rule.AllowsTributeSummon) yield break;
        foreach (var procedure in tributeProcedures.Values.Where(p => p.CardId == card.GameCardId).OrderBy(p => p.ProcedureId, StringComparer.Ordinal))
            yield return (procedure.ProcedureId, procedure.TributeCount, procedure.AllowsSet);
        if (rule.RequiresRegisteredTributeProcedure) yield break;
        uint level = ComputeHandSummonLevel(state, player, card);
        if (level >= 5) yield return (null, level <= 6 ? 1 : 2, true);
    }
}
