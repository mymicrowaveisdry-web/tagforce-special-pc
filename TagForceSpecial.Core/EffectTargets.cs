namespace TagForceSpecial.Core;

/// <summary>Which field cards a target or "all matching" step considers.</summary>
public enum FieldCardKind { Monster, SpellTrap, Any }

/// <summary>Whose field, relative to the activating player.</summary>
public enum TargetSide { Either, Own, Opponent }

/// <summary>Position requirement on a field card; Spell/Trap cards only use <see cref="FaceUp"/>.</summary>
public enum FieldCardFilter { Any, FaceUp, AttackPosition, DefensePosition, FaceUpAttack }

/// <summary>Where a targeted card sits; Graveyard and banished cards have no zone or placement.</summary>
public enum FieldLocation { MonsterZone, SpellTrapZone, PendulumZone, Graveyard, Banished }

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
            FieldLocation.PendulumZone => (player.PendulumZones[Zone]?.Card, player.PendulumZones[Zone]?.PlacementId ?? 0),
            FieldLocation.Graveyard => (player.Graveyard.Contains(Card) ? Card : null, 0),
            _ => (player.Banished.Contains(Card) ? Card : null, 0)
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

public enum CardZone { Graveyard, Banished }

/// <summary>
/// Between <see cref="MinCount"/> and <see cref="MaxCount"/> distinct cards in a Graveyard or banished zone, chosen at
/// activation. Only the last target of a definition may vary in count.
/// </summary>
public sealed record ZoneTarget : EffectTarget
{
    public CardZone Zone { get; }
    public TargetSide Side { get; }
    public CardFilter Filter { get; }
    public int MinCount { get; }
    public int MaxCount { get; }
    /// <summary>Only cards that could be Special Summoned to the activating player's field qualify.</summary>
    public bool MustBeSummonable { get; }

    public ZoneTarget(CardZone zone, TargetSide side, CardFilter filter, int minCount = 1, int maxCount = 1, bool mustBeSummonable = false)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!Enum.IsDefined(zone) || !Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(zone));
        if (minCount < 1 || maxCount < minCount || maxCount > 5) throw new ArgumentOutOfRangeException(nameof(maxCount));
        Zone = zone; Side = side; Filter = filter; MinCount = minCount; MaxCount = maxCount; MustBeSummonable = mustBeSummonable;
    }
}

/// <summary>Matches any card with explicit card rules.</summary>
public sealed record AnyCardFilter : CardFilter;

/// <summary>Adds every qualifying Graveyard or banished target to its owner's hand.</summary>
public sealed record AddTargetsToHandStep : EffectStep;

/// <summary>Banishes every qualifying Graveyard target.</summary>
public sealed record BanishTargetsStep : EffectStep;

/// <summary>Returns every qualifying banished target to its owner's Graveyard.</summary>
public sealed record ReturnTargetsToGraveyardStep : EffectStep;

/// <summary>
/// Shuffles every qualifying target into its owner's Deck. With <see cref="StopUnlessAll"/>, later steps resolve
/// only if every target was shuffled in.
/// </summary>
public sealed record ShuffleTargetsIntoDeckStep(bool StopUnlessAll = false) : EffectStep;

/// <summary>Special Summons the single Graveyard target to the activating player's field; zone and position chosen at resolution.</summary>
public sealed record SpecialSummonTargetStep : EffectStep;

/// <summary>Ends an optional run of target choices early.</summary>
public sealed record FinishDecision(PlayerId Player, long DecisionId) : DecisionCommand(Player, DecisionId);

public sealed record CardMovedToHand(PlayerId Owner, CardInstance Card, CardZone From, string EffectId) : DuelEvent;
public sealed record CardBanishedFromGraveyard(PlayerId Owner, CardInstance Card, string EffectId) : DuelEvent;
public sealed record CardReturnedToGraveyard(PlayerId Owner, CardInstance Card, string EffectId) : DuelEvent;
public sealed record CardShuffledIntoDeck(PlayerId Owner, CardInstance Card, string EffectId) : DuelEvent;
/// <summary>"When an opponent's monster declares an attack": the activating player's opponent has an attack in progress at declaration.</summary>
public sealed record OpponentAttackDeclaredCondition : EffectCondition;

/// <summary>The attacking monster, bound automatically at activation.</summary>
public sealed record AttackingMonsterTarget : EffectTarget;

/// <summary>Negates the current attack by the bound attacker; if it cannot, the rest of the effect does not resolve.</summary>
public sealed record NegateAttackStep : EffectStep;

/// <summary>Inflicts damage to the opponent equal to the bound target's current ATK.</summary>
public sealed record DamageByTargetAttackStep : EffectStep;

/// <summary>The activating player gains LP equal to the bound target's current ATK.</summary>
public sealed record GainByTargetAttackStep : EffectStep;

/// <summary>Banishes every bound field target that still qualifies; Tokens cease to exist.</summary>
public sealed record BanishFieldTargetsStep : EffectStep;

/// <summary>Banishes every matching field monster (never the resolving source), activating player's side first.</summary>
public sealed record BanishAllStep(TargetSide Side = TargetSide.Either, FieldCardFilter Filter = FieldCardFilter.Any) : EffectStep;

/// <summary>Summon methods a summon-response condition accepts; a Tribute Summon is a Normal Summon.</summary>
[Flags]
public enum SummonKinds { Normal = 1, Flip = 2, Special = 4, Any = Normal | Flip | Special }

/// <summary>
/// A monster was just Summoned with its current stat in [Min, Max]; by the opponent unless ByOpponent is false.
/// Single requires that exactly one monster was Summoned.
/// </summary>
public sealed record MonsterSummonedCondition(SummonKinds Kinds, bool ByOpponent = true, bool Single = false,
    MonsterStat Stat = MonsterStat.Attack, int Min = 0, int Max = int.MaxValue) : EffectCondition;

/// <summary>The just-Summoned monsters that meet the summon condition, bound automatically at activation.</summary>
public sealed record SummonedMonsterTarget : EffectTarget;

/// <summary>Destroys each bound target and banishes it instead of sending it to the Graveyard.</summary>
public sealed record DestroyAndBanishTargetsStep : EffectStep;

/// <summary>A monster that was just Summoned, and how.</summary>
public sealed record SummonedMonster(MonsterBinding Monster, SummonKinds Kind);

/// <summary>After a successful Summon, the opponent may activate a set card before play continues.</summary>
public sealed record SummonResponseWindowRequested(PlayerId Responder, long DecisionId, IReadOnlyList<SummonedMonster> Summoned) : DuelEvent;

/// <summary>
/// A monster would be Normal or Special Summoned and no chain has started; by the opponent when ByOpponent is set.
/// With OrSpellTrapActivation, a preceding Spell/Trap activation also qualifies.
/// </summary>
public sealed record MonsterWouldBeSummonedCondition(SummonKinds Kinds, bool ByOpponent = false, bool OrSpellTrapActivation = false) : EffectCondition;

/// <summary>Negates the pending Summon and destroys that monster; as a chain response, negates that activation and destroys the card.</summary>
public sealed record NegateSummonStep : EffectStep;

/// <summary>Tribute 1 monster you control, chosen at activation (never refunded).</summary>
public sealed record TributeCost : EffectCost;

/// <summary>Pay half your LP, rounded down (never refunded).</summary>
public sealed record PayHalfLifePointsCost : EffectCost;
