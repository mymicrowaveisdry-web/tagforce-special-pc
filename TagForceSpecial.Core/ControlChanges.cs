namespace TagForceSpecial.Core;

/// <summary>How long a change of control lasts before the monster goes back to the player who controlled it.</summary>
public enum ControlDuration
{
    Permanent,
    /// <summary>Until the End Phase of the turn it was taken.</summary>
    UntilEndPhase,
    /// <summary>While the resolving Equip Spell stays equipped to it.</summary>
    WhileEquipped
}

/// <summary>The activating player takes control of the bound opponent's monsters.</summary>
public sealed record TakeControlStep(ControlDuration Duration = ControlDuration.Permanent) : EffectStep;

/// <summary>The activating player gives control of the bound monsters they control to the opponent.</summary>
public sealed record GiveControlStep(ControlDuration Duration = ControlDuration.Permanent) : EffectStep;

/// <summary>The two bound monsters, one on each side, switch control.</summary>
public sealed record SwitchControlStep : EffectStep;

/// <summary>
/// Each player chooses a monster they control (activating player first; not targeted) and the two switch control. Neither can
/// have its battle position changed for the rest of the turn.
/// </summary>
public sealed record SwapChosenMonstersStep : EffectStep;

/// <summary>A monster moved to the other side. <see cref="EffectId"/> is empty when control returned by itself.</summary>
public sealed record ControlChanged(PlayerId From, PlayerId To, CardInstance Card, int FromZone, int ToZone, string EffectId) : DuelEvent;

/// <summary>A monster whose control ended had no free zone to return to and was sent to its owner's Graveyard.</summary>
public sealed record MonsterSentToGraveyardByRule(PlayerId Player, int Zone, CardInstance Card, long PlacementId) : DuelEvent;

internal sealed record ControlGrant(CardInstance Card, long PlacementId, PlayerId From, PlayerId To, int TurnNumber, ControlDuration Duration,
    CardInstance? Equip = null, long EquipPlacementId = 0);

public sealed partial class DuelEngine
{
    private static int? FreeZone(DuelState state, PlayerId player) =>
        Enumerable.Range(0, 5).Where(z => state.GetPlayer(player).MonsterZones[z] is null).Select(z => (int?)z).FirstOrDefault();

    private bool ControlLocked(DuelState state, MonsterBinding monster) =>
        RuleLocked(state, LockedAction.SwitchControl, monster.Player, monster.Card, state.GetPlayer(monster.Player).MonsterZones[monster.Zone]);

    /// <summary>Points every lasting reference to a monster at its new place after it changed sides.</summary>
    private static DuelState Rebind(DuelState state, IReadOnlyDictionary<MonsterBinding, MonsterBinding> moves)
    {
        MonsterBinding R(MonsterBinding b) => moves.TryGetValue(b, out var to) ? to : b;
        MonsterBinding? N(MonsterBinding? b) => b is { } some ? R(some) : null;
        var effects = state.EventEffects;
        effects = effects
            .WithModifiers(effects.TimedModifiers.Select(m => m with { Target = R(m.Target), Source = N(m.Source) }),
                effects.AppliedModifiers.Select(m => m with { Target = R(m.Target), Source = N(m.Source) }),
                effects.Statistics.Select(s => s with { Monster = R(s.Monster) }))
            .WithNegations(effects.TimedNegations.Select(n => n with { Target = R(n.Target), Source = R(n.Source) }), effects.NegatedMonsters.Select(R));
        state = state.WithEventEffects(effects);
        foreach (var player in state.Players)
            state = state.With(player: player.With(spellTraps: player.SpellTrapFields.Select(f => f is null ? null : f with
            {
                EquippedTo = N(f.EquippedTo), LinkedMonster = N(f.LinkedMonster)
            })));
        return state;
    }

    /// <summary>Moves monsters to new places on either side at once, keeping their placement, counters and materials.</summary>
    private static DuelState MoveMonsters(DuelState state, IReadOnlyList<(MonsterBinding From, PlayerId To, int Zone)> moves, string effectId,
        List<DuelEvent> events, bool lockPositions = false)
    {
        var zones = state.Players.ToDictionary(p => p.Id, p => p.MonsterZones.ToArray());
        var monsters = moves.Select(m => zones[m.From.Player][m.From.Zone]!).ToArray();
        foreach (var move in moves) zones[move.From.Player][move.From.Zone] = null;
        Dictionary<MonsterBinding, MonsterBinding> rebound = [];
        for (int i = 0; i < moves.Count; i++)
        {
            var monster = lockPositions ? monsters[i] with { PositionChangedOnTurn = state.TurnNumber } : monsters[i];
            zones[moves[i].To][moves[i].Zone] = monster;
            rebound[moves[i].From] = new(moves[i].To, moves[i].Zone, monster.Card, monster.PlacementId);
            events.Add(new ControlChanged(moves[i].From.Player, moves[i].To, monster.Card, moves[i].From.Zone, moves[i].Zone, effectId));
        }
        foreach (var (id, list) in zones) state = state.With(player: state.GetPlayer(id).With(monsters: list));
        return Rebind(state, rebound);
    }

    private static MonsterBinding Bound(FieldCardTarget target) => new(target.Player, target.Zone, target.Card, target.PlacementId);

    /// <summary>Takes or gives control of one bound monster; nothing happens if it cannot switch or the receiver has no free zone.</summary>
    private DuelState ChangeControl(DuelState state, ChainLink link, FieldCardTarget target, PlayerId receiver, ControlDuration duration,
        string effectId, List<DuelEvent> events)
    {
        var monster = Bound(target);
        if (target.Player == receiver || ControlLocked(state, monster) || FreeZone(state, receiver) is not int zone) return state;
        state = MoveMonsters(state, [(monster, receiver, zone)], effectId, events);
        if (duration == ControlDuration.Permanent) return state;
        var grant = new ControlGrant(target.Card, target.PlacementId, target.Player, receiver, state.TurnNumber, duration,
            duration == ControlDuration.WhileEquipped ? link.Activation.Spell : null, link.SourcePlacementId);
        return state.WithBookkeeping(state.Bookkeeping with { ControlGrants = Array.AsReadOnly([.. state.Bookkeeping.ControlGrants, grant]) });
    }

    /// <summary>Two monsters on different sides switch places; nothing happens if either cannot switch.</summary>
    private DuelState SwitchControl(DuelState state, MonsterBinding first, MonsterBinding second, string effectId, List<DuelEvent> events, bool lockPositions = false)
    {
        if (first.Player == second.Player || ControlLocked(state, first) || ControlLocked(state, second)) return state;
        return MoveMonsters(state, [(first, second.Player, second.Zone), (second, first.Player, first.Zone)], effectId, events, lockPositions);
    }

    private IEnumerable<FieldCardTarget> Swappable(DuelState state, PlayerId player) =>
        FieldMatches(state, player, FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.Any, null).Where(t => !ControlLocked(state, Bound(t)));

    /// <summary>Asks the activating player, then the opponent, for the monster each gives; then switches them.</summary>
    private (DuelState State, PendingDefinitionChoice? Pause, bool Stop) SwapChosen(DuelState state, ChainLink link, EffectDefinition definition, int stepIndex,
        DuelCommand? choice, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        var opponent = Other(controller);
        var own = Swappable(state, controller).ToArray();
        var theirs = Swappable(state, opponent).ToArray();
        if (own.Length == 0 || theirs.Length == 0)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
            return (state, null, true);
        }
        if (choice is not ChooseDefinitionCard pick)
            return (WithResults(state, Results(state) with { SwapFirst = null }),
                new PendingDefinitionCardChoice(checked(State.Revision + 1), controller, definition.EffectId, link.Number, stepIndex, own.Select(t => t.Card)), false);
        if (pick.Player == controller)
        {
            var mine = own.FirstOrDefault(t => t.Card.InstanceId == pick.CardInstanceId) ?? own[0];
            return (WithResults(state, Results(state) with { SwapFirst = mine.Card }),
                new PendingDefinitionCardChoice(checked(State.Revision + 1), opponent, definition.EffectId, link.Number, stepIndex, theirs.Select(t => t.Card)), false);
        }
        var given = own.FirstOrDefault(t => t.Card == Results(state).SwapFirst) ?? own[0];
        var taken = theirs.FirstOrDefault(t => t.Card.InstanceId == pick.CardInstanceId) ?? theirs[0];
        state = SwitchControl(state, Bound(given), Bound(taken), definition.EffectId, events, lockPositions: true);
        return (WithResults(state, Results(state) with { SwapFirst = null }), null, false);
    }

    private static bool EquipStillAttached(DuelState state, ControlGrant grant, MonsterBinding monster) =>
        state.Players.SelectMany(p => p.SpellTrapFields).Any(f => f is { } field && field.Card == grant.Equip && field.PlacementId == grant.EquipPlacementId &&
            field.EquippedTo == monster);

    /// <summary>
    /// Ends control grants whose time is up: the monster goes back to the player it was taken from, or to its owner's Graveyard
    /// when that player has no free zone. A grant ends quietly if its monster left the field or changed sides again.
    /// </summary>
    private DuelState ApplyControlReturns(DuelState next, List<DuelEvent> events)
    {
        if (next.Bookkeeping.ControlGrants.Count == 0 || next.Outcome is not null) return next;
        List<ControlGrant> kept = [];
        foreach (var grant in next.Bookkeeping.ControlGrants)
        {
            var (binding, monster) = FieldMonsters(next).FirstOrDefault(e => e.Monster.Card == grant.Card && e.Monster.PlacementId == grant.PlacementId);
            if (monster is null || binding.Player != grant.To) continue;
            bool ended = grant.Duration switch
            {
                ControlDuration.UntilEndPhase => next.TurnNumber > grant.TurnNumber || next.Phase == DuelPhase.End,
                ControlDuration.WhileEquipped => !EquipStillAttached(next, grant, binding),
                _ => false
            };
            if (!ended) { kept.Add(grant); continue; }
            if (FreeZone(next, grant.From) is int zone) next = MoveMonsters(next, [(binding, grant.From, zone)], "", events);
            else next = SendFieldCardToGraveyard(RemovePlacement(next, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, binding.Card, binding.PlacementId)),
                binding.Card, new MonsterSentToGraveyardByRule(binding.Player, binding.Zone, binding.Card, binding.PlacementId), events);
        }
        return next.WithBookkeeping(next.Bookkeeping with { ControlGrants = Array.AsReadOnly(kept.ToArray()) });
    }
}
