namespace TagForceSpecial.Core;

/// <summary>Actions a continuous "cannot" effect can forbid.</summary>
[Flags]
public enum LockedAction
{
    None = 0,
    Attack = 1,
    BeAttacked = 2,
    ChangePosition = 4,
    NormalSummon = 8,
    SetMonster = 16,
    SpecialSummon = 32,
    FusionSummon = 64,
    ActivateSpell = 128,
    ActivateTrap = 256,
    ActivateMonsterEffect = 512,
    SetSpellTrap = 1024,
    BeDestroyedByBattle = 2048,
    BeDestroyedByEffect = 4096,
    BeTargeted = 8192,
    BeTributed = 16384,
    BeBanished = 32768,
    BeReturnedToHand = 65536,
    BeReturnedToDeck = 131072,
    Draw = 262144,
    /// <summary>Keeping the hand hidden: the locked side plays with its hand revealed.</summary>
    HideHand = 524288,
    /// <summary>Control of the card switching to the other player.</summary>
    SwitchControl = 1048576
}

/// <summary>Who or what a lock applies to.</summary>
public enum LockScope { Side, Self, Equipped }

/// <summary>
/// A "cannot" rule. Side is relative to the lock's controller and names who is restricted (for BeAttacked, BeTargeted,
/// moves and destruction, whose card is protected). The optional fields narrow when it applies.
/// </summary>
public sealed record RuleLock(LockedAction Actions, TargetSide Side = TargetSide.Either, CardFilter? Cards = null, LockScope Scope = LockScope.Side,
    FieldCardFilter Position = FieldCardFilter.Any, bool SummonedThisTurnOnly = false, DuelPhase? Phase = null, EffectKind? Kind = null,
    SpecialSummonSources From = SpecialSummonSources.None, int UsesPerTurn = 0, bool ByOpponentOnly = false, CardCategory? ByCategory = null,
    DuelPhase? ExceptPhase = null, BattleOpponent? Versus = null, bool OnFieldOnly = false);

/// <summary>The monster a battle is with, by card filter and current ATK; used for battle destruction.</summary>
public sealed record BattleOpponent(CardFilter? Cards = null, int MinAttack = 0, int MaxAttack = int.MaxValue);

/// <summary>A lock that holds while the card is face-up: a monster not negated, or a Spell or Trap whose effect is active.</summary>
public sealed record RuleLockProfile(string EffectId, uint CardId, RuleLock Lock)
{
    /// <summary>A Pendulum Effect: it holds while the card is an active scale, and Spell negation does not stop it.</summary>
    public bool PendulumZone { get; init; }
}

/// <summary>Applies a lock that lasts until the end of this turn, relative to the activating player.</summary>
public sealed record ApplyRuleLockStep(RuleLock Lock) : EffectStep;

internal sealed record TimedRuleLock(PlayerId Controller, int TurnNumber, RuleLock Lock);
internal sealed record RuleLockUse(int InstanceId, string EffectId, int TurnNumber);

public sealed record RuleLockApplied(PlayerId Controller, string EffectId) : DuelEvent;
/// <summary>A "cannot be destroyed" effect kept the card on the field.</summary>
public sealed record DestructionPrevented(CardInstance Card, string EffectId) : DuelEvent;

public sealed partial class DuelEngine
{
    private readonly record struct ActiveLock(PlayerId Controller, RuleLock Lock, FieldCardRef? Self, MonsterBinding? Equipped, string EffectId, int InstanceId);
    private readonly record struct FieldCardRef(CardInstance Card, long PlacementId);

    private IEnumerable<ActiveLock> ActiveLocks(DuelState state)
    {
        foreach (var timed in state.Bookkeeping.TimedLocks.Where(t => t.TurnNumber == state.TurnNumber))
            yield return new(timed.Controller, timed.Lock, null, null, "", 0);
        if (ruleLockProfiles.Count == 0) yield break;
        foreach (var (binding, monster) in FieldMonsters(state))
            if (monster.Position != BattlePosition.FaceDownDefense && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                ruleLockProfiles.TryGetValue(monster.Card.GameCardId, out var monsterLocks))
                foreach (var profile in monsterLocks.Where(p => !p.PendulumZone))
                    yield return new(binding.Player, profile.Lock, new(monster.Card, monster.PlacementId), null, profile.EffectId, monster.Card.InstanceId);
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (player, _, field) in ActiveCards(state))
            if (!(spellsNegated && IsSpellCard(field)) && ruleLockProfiles.TryGetValue(field.Card.GameCardId, out var cardLocks))
                foreach (var profile in cardLocks.Where(p => !p.PendulumZone))
                    yield return new(player, profile.Lock, null, field.EquippedTo, profile.EffectId, field.Card.InstanceId);
        foreach (var (player, _, scale) in ActiveScales(state))
            if (ruleLockProfiles.TryGetValue(scale.Card.GameCardId, out var scaleLocks))
                foreach (var profile in scaleLocks.Where(p => p.PendulumZone))
                    yield return new(player, profile.Lock, null, null, profile.EffectId, scale.Card.InstanceId);
    }

    private bool LockApplies(DuelState state, ActiveLock active, LockedAction action, PlayerId subject, CardInstance? card, FieldMonster? monster,
        DecisionCardSource? from, EffectKind? kind, PlayerId? targeter, CardCategory? byCategory, MonsterBinding? versus = null, bool onField = true)
    {
        var rule = active.Lock;
        if (!rule.Actions.HasFlag(action)) return false;
        if (rule.Side != TargetSide.Either && rule.Side == TargetSide.Own != (subject == active.Controller)) return false;
        if (rule.Cards is not null && (card is null || !MatchesFilter(card, rule.Cards))) return false;
        if (rule.Scope == LockScope.Self && (monster is null || active.Self is not { } self || self.Card != monster.Card || self.PlacementId != monster.PlacementId)) return false;
        if (rule.Scope == LockScope.Equipped && (monster is null || active.Equipped is not { } equipped || equipped.Card != monster.Card || equipped.PlacementId != monster.PlacementId)) return false;
        if (rule.Position != FieldCardFilter.Any && (monster is null || !PositionMatches(monster.Position, rule.Position))) return false;
        if (rule.SummonedThisTurnOnly && (monster is null || monster.SummonedOnTurn != state.TurnNumber)) return false;
        if (rule.Phase is { } phase && phase != state.Phase) return false;
        if (rule.ExceptPhase is { } except && except == state.Phase) return false;
        if (rule.OnFieldOnly && !onField) return false;
        if (rule.Versus is { } opponent && (versus is not { } other || !VersusMatches(state, opponent, other))) return false;
        if (rule.Kind is { } required && required != kind) return false;
        if (rule.From != SpecialSummonSources.None && (from is not { } source || !rule.From.HasFlag(SourceFlag(source)))) return false;
        if (rule.ByOpponentOnly && targeter == active.Controller) return false;
        if (rule.ByCategory is { } category && category != byCategory) return false;
        return rule.UsesPerTurn == 0 || state.Bookkeeping.LockUses.Count(u => u.InstanceId == active.InstanceId && u.EffectId == active.EffectId && u.TurnNumber == state.TurnNumber) < rule.UsesPerTurn;
    }

    private bool VersusMatches(DuelState state, BattleOpponent opponent, MonsterBinding other) =>
        (opponent.Cards is null || MatchesFilter(other.Card, opponent.Cards)) &&
        GetMonsterStatistics(state, other.Player, other.Zone)?.Attack is int attack && attack >= opponent.MinAttack && attack <= opponent.MaxAttack;

    private static bool PositionMatches(BattlePosition position, FieldCardFilter filter) => filter switch
    {
        FieldCardFilter.Any => true,
        FieldCardFilter.FaceUp => position != BattlePosition.FaceDownDefense,
        FieldCardFilter.FaceDown => position == BattlePosition.FaceDownDefense,
        FieldCardFilter.AttackPosition or FieldCardFilter.FaceUpAttack => position == BattlePosition.FaceUpAttack,
        FieldCardFilter.DefensePosition => position != BattlePosition.FaceUpAttack,
        _ => false
    };

    /// <summary>True when an active "cannot" rule forbids this action.</summary>
    private bool RuleLocked(DuelState state, LockedAction action, PlayerId subject, CardInstance? card, FieldMonster? monster = null,
        DecisionCardSource? from = null, EffectKind? kind = null, PlayerId? targeter = null, CardCategory? byCategory = null, bool onField = true) =>
        ActiveLocks(state).Any(active => LockApplies(state, active, action, subject, card, monster, from, kind, targeter, byCategory, onField: onField));

    /// <summary>True when a lock stops this card from being moved: banished, or returned to the hand or Deck.</summary>
    private bool MoveLocked(DuelState state, LockedAction action, FieldCardTarget target) =>
        RuleLocked(state, action, target.Player, target.Card,
            target.Location == FieldLocation.MonsterZone ? state.GetPlayer(target.Player).MonsterZones[target.Zone] : null,
            onField: target.Location is not (FieldLocation.Graveyard or FieldLocation.Banished));

    /// <summary>The move a choice for this effect makes; a card that cannot make it cannot be chosen.</summary>
    private LockedAction ChoiceMove(EffectDefinition definition, int index, CardInstance card)
    {
        if (index < definition.CostTargetSteps) return definition.Cost is BanishFromGraveyardCost ? LockedAction.BeBanished : LockedAction.None;
        if (definition.Steps.Any(s => s is TakeControlStep or GiveControlStep or SwitchControlStep)) return LockedAction.SwitchControl;
        if (definition.Steps.Any(s => s is BanishFieldTargetsStep or DestroyAndBanishTargetsStep or BanishTargetsStep)) return LockedAction.BeBanished;
        if (definition.Steps.Any(s => s is ShuffleTargetsIntoDeckStep)) return LockedAction.BeReturnedToDeck;
        if (definition.Steps.Any(s => s is ReturnTargetsToHandStep))
            return rules.TryGetValue(card.GameCardId, out var rule) && rule.AllowsExtraDeck ? LockedAction.BeReturnedToDeck : LockedAction.BeReturnedToHand;
        return LockedAction.None;
    }

    private bool MoveAllowed(DuelState state, DecisionSequence sequence, int index, FieldCardTarget target) =>
        SequenceDefinition(sequence) is not { } definition ||
        // A Token cannot become Xyz Material, so an attaching effect cannot choose one.
        !(index >= definition.CostTargetSteps && definition.Steps.Any(s => s is AttachTargetsToSelfStep) && IsTokenId(target.Card.GameCardId)) &&
        // A Rank-Up cannot target a monster that has no legal Extra Deck monster to become.
        !(index >= definition.CostTargetSteps && definition.Steps.OfType<RankUpStep>().FirstOrDefault() is { } rankUp && !RankUpOptions(state, target, rankUp).Any()) &&
        ChoiceMove(definition, index, target.Card) is var move && (move == LockedAction.None || !MoveLocked(state, move, target));

    private EffectDefinition? SequenceDefinition(DecisionSequence sequence)
    {
        const string prefix = "activation:";
        return sequence.Id.StartsWith(prefix, StringComparison.Ordinal) && effectsById.TryGetValue(sequence.Id[prefix.Length..], out var effect) ? effect : null;
    }

    private static FieldMonster? MonsterOf(DuelState state, CardInstance card) =>
        state.Players.SelectMany(p => p.MonsterZones).FirstOrDefault(m => m?.Card == card);

    private bool ActivationLocked(DuelState state, PlayerId player, CardInstance card, EffectDefinition definition)
    {
        // A player cannot activate a card whose own draw they could not perform.
        if (definition.Steps.Any(s => s is DrawCardsStep { Players: EffectPlayers.Self }) && RuleLocked(state, LockedAction.Draw, player, null)) return true;
        if (definition.Cost is SendSelfCost { Destination: SelfDestination.Banish } && RuleLocked(state, LockedAction.BeBanished, player, card, onField: false)) return true;
        if (definition.IsMonsterEffect) return RuleLocked(state, LockedAction.ActivateMonsterEffect, player, card, kind: definition.Kind);
        // A face-up card's own effects and Graveyard effects are not activations of a Spell or Trap Card.
        if (definition.IsFaceUpEffect || definition.Origin != EffectOrigin.Field) return false;
        var action = rules[card.GameCardId].Category == CardCategory.Trap ? LockedAction.ActivateTrap : LockedAction.ActivateSpell;
        return RuleLocked(state, action, player, card, kind: definition.Kind);
    }

    /// <summary>Whether this field card can be chosen. Protection gained after a target was chosen does not undo it, so this is checked only here.</summary>
    private bool SelectionAllowed(DuelState state, FieldTarget spec, FieldCardTarget target, PlayerId chooser, DecisionSequence sequence)
    {
        var monster = target.Location == FieldLocation.MonsterZone ? state.GetPlayer(target.Player).MonsterZones[target.Zone] : null;
        if (spec.ForTribute) return !RuleLocked(state, LockedAction.BeTributed, target.Player, target.Card, monster);
        CardCategory? category = SequenceDefinition(sequence) is { } effect ? rules[effect.CardId].Category : null;
        return !RuleLocked(state, LockedAction.BeTargeted, target.Player, target.Card, monster, targeter: chooser, byCategory: category);
    }

    /// <summary>The state after a "cannot be destroyed" effect kept the card, spending any per-turn use; null when none applies.</summary>
    private DuelState? PreventDestruction(DuelState state, LockedAction action, PlayerId subject, CardInstance card, FieldMonster? monster, List<DuelEvent> events,
        MonsterBinding? versus = null)
    {
        foreach (var active in ActiveLocks(state))
        {
            if (!LockApplies(state, active, action, subject, card, monster, null, null, null, null, versus)) continue;
            events.Add(new DestructionPrevented(card, active.EffectId));
            return active.Lock.UsesPerTurn == 0 ? state
                : state.WithBookkeeping(state.Bookkeeping with { LockUses = Array.AsReadOnly(state.Bookkeeping.LockUses.Where(u => u.TurnNumber == state.TurnNumber)
                    .Append(new(active.InstanceId, active.EffectId, state.TurnNumber)).ToArray()) });
        }
        return null;
    }

    private static DuelState ApplyTimedLock(DuelState state, PlayerId controller, RuleLock rule, string effectId, List<DuelEvent> events)
    {
        events.Add(new RuleLockApplied(controller, effectId));
        return state.WithBookkeeping(state.Bookkeeping with { TimedLocks = Array.AsReadOnly(state.Bookkeeping.TimedLocks.Where(t => t.TurnNumber == state.TurnNumber)
            .Append(new(controller, state.TurnNumber, rule)).ToArray()) });
    }
}
