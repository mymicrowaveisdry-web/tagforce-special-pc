namespace TagForceSpecial.Core;

/// <summary>Which field cards a target or "all matching" step considers.</summary>
public enum FieldCardKind { Monster, SpellTrap, Any }

/// <summary>Whose field, relative to the activating player.</summary>
public enum TargetSide { Either, Own, Opponent }

/// <summary>Position requirement on a field card; Spell/Trap cards only use <see cref="FaceUp"/>.</summary>
public enum FieldCardFilter { Any, FaceUp, AttackPosition, DefensePosition, FaceUpAttack }

/// <summary>Where a field card sits.</summary>
public enum FieldLocation { MonsterZone, SpellTrapZone, PendulumZone }

/// <summary>A field card bound by instance and uninterrupted placement.</summary>
public sealed record FieldCardTarget(PlayerId Player, FieldLocation Location, int Zone, CardInstance Card, long PlacementId)
{
    public bool IsStillPresent(DuelState state)
    {
        var player = state.GetPlayer(Player);
        (CardInstance? card, long placement) = Location switch
        {
            FieldLocation.MonsterZone => (player.MonsterZones[Zone]?.Card, player.MonsterZones[Zone]?.PlacementId ?? 0),
            FieldLocation.SpellTrapZone => (player.SpellTrapFields[Zone]?.Card, player.SpellTrapFields[Zone]?.PlacementId ?? 0),
            _ => (player.PendulumZones[Zone]?.Card, player.PendulumZones[Zone]?.PlacementId ?? 0)
        };
        return card == Card && placement == PlacementId;
    }
}

/// <summary><see cref="Count"/> distinct field cards chosen at activation; each must still qualify at resolution.</summary>
public sealed record FieldTarget : EffectTarget
{
    public FieldCardKind Kind { get; }
    public TargetSide Side { get; }
    public FieldCardFilter Filter { get; }
    public int Count { get; }

    public FieldTarget(FieldCardKind kind, TargetSide side = TargetSide.Either, FieldCardFilter filter = FieldCardFilter.Any, int count = 1)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(side) || !Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (count is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(count));
        Kind = kind; Side = side; Filter = filter; Count = count;
    }
}

/// <summary>Pay LP at activation; the player needs more LP than the amount.</summary>
public sealed record PayLifePointsCost : EffectCost
{
    public int Amount { get; }
    public PayLifePointsCost(int amount) => Amount = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
}

/// <summary>Destroys every bound target that still qualifies.</summary>
public sealed record DestroyTargetsStep : EffectStep;

/// <summary>Returns every bound target that still qualifies to its owner's hand (Extra Deck monsters to the Extra Deck).</summary>
public sealed record ReturnTargetsToHandStep : EffectStep;

/// <summary>Changes every bound monster target that still qualifies to face-up Defense Position.</summary>
public sealed record ChangeTargetsToDefenseStep : EffectStep;

/// <summary>Destroys every matching field card (never the resolving source), activating player's side first.</summary>
public sealed record DestroyAllStep(FieldCardKind Kind, TargetSide Side = TargetSide.Either, FieldCardFilter Filter = FieldCardFilter.Any) : EffectStep;

/// <summary>Returns every matching field card (never the resolving source) to its owner's hand.</summary>
public sealed record ReturnAllToHandStep(FieldCardKind Kind, TargetSide Side = TargetSide.Either) : EffectStep;

/// <summary>Changes every Attack Position monster on the given side to face-up Defense Position.</summary>
public sealed record ChangeAllToDefenseStep(TargetSide Side = TargetSide.Either) : EffectStep;

public enum MonsterStat { Attack, Defense }
public enum StatExtreme { Lowest, Highest }

/// <summary>
/// Destroys the one matching face-up monster with the lowest or highest current stat. Ties are chosen by the
/// activating player at resolution.
/// </summary>
public sealed record DestroyExtremeMonsterStep(TargetSide Side, MonsterStat Stat, StatExtreme Extreme,
    FieldCardFilter Filter = FieldCardFilter.FaceUp) : EffectStep;

public sealed record ChooseDecisionFieldTarget(PlayerId Player, long DecisionId, PlayerId TargetPlayer, FieldLocation Location,
    int Zone, int CardInstanceId) : DecisionCommand(Player, DecisionId);

/// <summary>An effect returned a field card to its owner's hand or Extra Deck.</summary>
public sealed record CardReturnedToHand(PlayerId Owner, CardInstance Card, string EffectId) : DuelEvent;

/// <summary>LP paid as an activation cost.</summary>
public sealed record LifePointsPaid(PlayerId Player, int Amount, int LifePointsBefore, int LifePointsAfter, string EffectId) : DuelEvent;
