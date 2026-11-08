namespace TagForceSpecial.Core;

/// <summary>Card activation kinds. Spell Speed and set-turn rules derive from the kind.</summary>
public enum EffectKind { NormalSpell, QuickPlaySpell, NormalTrap, CounterTrap, MonsterTrigger }

/// <summary>One ordered resolution operation from a closed, Core-defined set.</summary>
public abstract record EffectStep
{
    private protected EffectStep() { }
}

/// <summary>Which players an LP step applies to, relative to the activating player.</summary>
public enum EffectPlayers { Self, Opponent, Both }

/// <summary>
/// A public count that multiplies an LP amount at resolution. A counted definition can be activated only while each
/// count is at least 1.
/// </summary>
public enum EffectCount
{
    None,
    /// <summary>Monsters the opponent controls (face-down included).</summary>
    OpponentMonsters,
    /// <summary>Monsters on the field, both sides.</summary>
    AllMonsters,
    OpponentHand,
    /// <summary>Cards in the opponent's hand plus every card they control (monster, Spell/Trap and Pendulum zones).</summary>
    OpponentHandAndField,
    OpponentGraveyard,
    OpponentBanished
}

/// <summary>
/// The listed players gain <see cref="Amount"/> LP (times <see cref="PerCount"/> when set), controller first, with
/// the existing integer saturation policy.
/// </summary>
public sealed record GainLifePointsStep(int Amount, EffectPlayers Players = EffectPlayers.Self,
    EffectCount PerCount = EffectCount.None) : EffectStep;

/// <summary>
/// Effect damage to the activating player's opponent (times <see cref="PerCount"/> when set); zero LP is an
/// immediate terminal defeat.
/// </summary>
public sealed record DealDamageStep(int Amount, EffectCount PerCount = EffectCount.None) : EffectStep;

/// <summary>
/// Simultaneous effect damage to both players. If both reach 0 LP, the duel is a draw; if one does, that player loses.
/// </summary>
public sealed record DamageBothPlayersStep(int ToOpponent, int ToSelf) : EffectStep;

/// <summary>Shuffles the activating player's Deck with the seeded generator; requires an engine seed.</summary>
public sealed record ShuffleDeckStep : EffectStep;

/// <summary>
/// Special Summons the listed Tokens to the lowest free Monster Zones, all or none. Each ID needs IsToken card rules.
/// </summary>
public sealed record SummonTokensStep : EffectStep
{
    public IReadOnlyList<uint> TokenCardIds { get; }
    public BattlePosition Position { get; }

    public SummonTokensStep(IEnumerable<uint> tokenCardIds, BattlePosition position)
    {
        ArgumentNullException.ThrowIfNull(tokenCardIds);
        var ids = tokenCardIds.Take(6).ToArray();
        if (ids.Length is 0 or > 5 || ids.Any(id => id == 0))
            throw new ArgumentException("Summon 1–5 Tokens with nonzero IDs.", nameof(tokenCardIds));
        if (position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense))
            throw new ArgumentOutOfRangeException(nameof(position), "Tokens are summoned face-up.");
        TokenCardIds = Array.AsReadOnly(ids);
        Position = position;
    }
}

/// <summary>
/// Destroys the Spell/Trap bound at activation (<see cref="SpellTrapOnFieldTarget"/>). Skipped if the target left or
/// was replaced.
/// </summary>
public sealed record DestroyTargetStep : EffectStep;

/// <summary>Destroys every face-up monster the activating player's opponent controls, in zone order.</summary>
public sealed record DestroyOpponentFaceUpMonstersStep : EffectStep;

/// <summary>
/// Negates the activation of the link this one responded to and, when <paramref name="DestroySource"/>, destroys
/// that card if it is still in place. Requires a response condition.
/// </summary>
public sealed record NegateActivationStep(bool DestroySource) : EffectStep;

/// <summary>Negates the effect of the link this one responded to; <paramref name="DestroySource"/> also destroys its card or monster.</summary>
public sealed record NegateEffectStep(bool DestroySource) : EffectStep;

/// <summary>
/// Special Summons one eligible hand or Deck card chosen at resolution, shuffling after a Deck summon. Optionally
/// banishes it in that turn's End Phase.
/// </summary>
public sealed record SpecialSummonStep : EffectStep
{
    public SpecialSummonSources Sources { get; }
    public IReadOnlyList<uint> EligibleCardIds { get; }
    public bool BanishAtEndPhase { get; }
    /// <summary>When set, replaces the ID list as the eligibility test.</summary>
    public CardFilter? Filter { get; }
    /// <summary>When set, the only position the monster can be Summoned in.</summary>
    public BattlePosition? Position { get; }
    /// <summary>The player may Summon several eligible monsters at once.</summary>
    public bool AnyNumber { get; }
    /// <summary>With <see cref="AnyNumber"/>, how many must be chosen: 0 or 1.</summary>
    public int MinCount { get; } = 1;

    /// <summary>Eligibility by filter, optionally in one fixed position and for any number of monsters.</summary>
    public SpecialSummonStep(SpecialSummonSources sources, CardFilter filter, BattlePosition? position = null, bool anyNumber = false, int minCount = 1)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (sources == SpecialSummonSources.None || (sources & ~(SpecialSummonSources.Hand | SpecialSummonSources.Deck)) != 0)
            throw new ArgumentOutOfRangeException(nameof(sources), "Only hand and Deck sources are supported.");
        if (position is not (null or BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense or BattlePosition.FaceDownDefense))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (minCount is < 0 or > 1 || !anyNumber && minCount != 1) throw new ArgumentOutOfRangeException(nameof(minCount));
        AnyNumber = anyNumber;
        MinCount = minCount;
        Sources = sources;
        EligibleCardIds = Array.AsReadOnly(Array.Empty<uint>());
        Filter = filter;
        Position = position;
    }

    public SpecialSummonStep(SpecialSummonSources sources, IEnumerable<uint> eligibleCardIds, bool banishAtEndPhase = false)
    {
        ArgumentNullException.ThrowIfNull(eligibleCardIds);
        if (sources == SpecialSummonSources.None || (sources & ~(SpecialSummonSources.Hand | SpecialSummonSources.Deck)) != 0)
            throw new ArgumentOutOfRangeException(nameof(sources), "Only hand and Deck sources are supported.");
        var ids = eligibleCardIds.Take(1025).ToArray();
        if (ids.Length is 0 or > 1024 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Supply 1–1024 distinct nonzero eligible card IDs.", nameof(eligibleCardIds));
        Sources = sources;
        EligibleCardIds = Array.AsReadOnly(ids);
        BanishAtEndPhase = banishAtEndPhase;
    }
}

/// <summary>A closed card filter for Deck choices. Cards without explicit rules never match.</summary>
public abstract record CardFilter
{
    private protected CardFilter() { }
}

/// <summary>Matches the listed card IDs only (a reviewed list, e.g. every artwork ID of a named card).</summary>
public sealed record CardIdFilter : CardFilter
{
    public IReadOnlyList<uint> CardIds { get; }

    public CardIdFilter(IEnumerable<uint> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        var ids = cardIds.Take(2049).ToArray();
        if (ids.Length is 0 or > 2048 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Supply 1–2048 distinct nonzero card IDs.", nameof(cardIds));
        CardIds = Array.AsReadOnly(ids);
    }

    // Value semantics over the list contents, so equal filters compare equal.
    public bool Equals(CardIdFilter? other) => other is not null && CardIds.SequenceEqual(other.CardIds);
    public override int GetHashCode() => CardIds.Aggregate(17, (hash, id) => hash * 31 + id.GetHashCode());
}

/// <summary>Matches any card with explicit Monster card rules.</summary>
public sealed record AnyMonsterFilter : CardFilter;

/// <summary>
/// The listed players draw <see cref="Count"/> cards each (activating player first). Activation needs that many
/// cards in each drawing Deck; a player who cannot draw them all at resolution loses (existing draw rule).
/// </summary>
public sealed record DrawCardsStep(int Count, EffectPlayers Players = EffectPlayers.Self) : EffectStep;

/// <summary>
/// The activating player chooses one matching card in their Deck at resolution, reveals it and adds it to the hand,
/// then shuffles the Deck. Activation needs a matching card; none at resolution skips the step without shuffling.
/// </summary>
public sealed record AddFromDeckToHandStep(CardFilter Filter) : EffectStep;

/// <summary>As <see cref="AddFromDeckToHandStep"/>, but the chosen card is sent to its owner's Graveyard.</summary>
public sealed record SendFromDeckToGraveyardStep(CardFilter Filter) : EffectStep;

/// <summary>
/// Sends the top <see cref="Count"/> cards of the activating player's Deck to the Graveyard, in Deck order.
/// Activation needs that many cards; fewer at resolution sends what remains (no loss).
/// </summary>
public sealed record MillTopOfDeckStep(int Count) : EffectStep;

/// <summary>An authored activation condition, checked in preflight. The set is closed.</summary>
public abstract record EffectCondition
{
    private protected EffectCondition() { }
}

/// <summary>The activating player has at least <see cref="Count"/> unoccupied Monster Zones.</summary>
public sealed record FreeMonsterZonesCondition : EffectCondition
{
    public int Count { get; }
    public FreeMonsterZonesCondition(int count) =>
        Count = count is >= 1 and <= 5 ? count : throw new ArgumentOutOfRangeException(nameof(count));
}

/// <summary>
/// Requires the preceding link to be a Spell activation (Pendulum scales count), so the definition is a response only.
/// </summary>
public sealed record RespondsToSpellActivationCondition : EffectCondition;

/// <summary>Requires a preceding link (any activation or trigger), of <see cref="Category"/> when set.</summary>
public sealed record RespondsToActivationCondition(CardCategory? Category = null) : EffectCondition;

/// <summary>The activating player has not Summoned a monster this turn; Sets do not count.</summary>
public sealed record NoSummonThisTurnCondition : EffectCondition;

/// <summary>An activation cost, paid at commit and never refunded.</summary>
public abstract record EffectCost
{
    private protected EffectCost() { }
}

/// <summary>Discard 1 card from the hand other than the source; <see cref="Filter"/> limits which cards qualify.</summary>
public sealed record DiscardCost(CardIdFilter? Filter = null) : EffectCost;

/// <summary>An activation-time target, bound by instance and placement.</summary>
public abstract record EffectTarget
{
    private protected EffectTarget() { }
}

/// <summary>1 Spell/Trap card on the field (either player's zones, Pendulum zones included), never the source.</summary>
public sealed record SpellTrapOnFieldTarget : EffectTarget;

/// <summary>
/// Explicit, validated card effect: identity, kind, activation requirements and ordered steps. Activation
/// (preflight, commit with cost/target decisions, response window) and resolution (last-first, negation,
/// cleanup) are shared by every definition.
/// </summary>
public sealed class EffectDefinition
{
    public string EffectId { get; }
    public uint CardId { get; }
    public EffectKind Kind { get; }
    public IReadOnlyList<EffectStep> Steps { get; }
    public IReadOnlyList<EffectCondition> Conditions { get; }
    public EffectCost? Cost { get; }
    public EffectTarget? Target { get; }
    /// <summary>Field, Graveyard or banished targets chosen in order at activation.</summary>
    public IReadOnlyList<EffectTarget> FieldTargets { get; }
    /// <summary>
    /// On activation, the activating player cannot Summon other monsters for the rest of the turn (Sets remain
    /// legal). Applied at commit, so it holds even if the activation is later negated.
    /// </summary>
    public bool LocksOtherSummonsThisTurn { get; }

    public ChainSpellSpeed SpellSpeed => Kind switch
    {
        EffectKind.NormalSpell or EffectKind.MonsterTrigger => ChainSpellSpeed.One,
        EffectKind.CounterTrap => ChainSpellSpeed.Three,
        _ => ChainSpellSpeed.Two
    };

    internal bool SummonsMonsters => Steps.Any(s => s is SummonTokensStep or SpecialSummonStep);
    internal IEnumerable<EffectCount> Counts => Steps.Select(s => s switch
    {
        GainLifePointsStep g => g.PerCount,
        DealDamageStep d => d.PerCount,
        _ => EffectCount.None
    }).Where(c => c != EffectCount.None);
    internal static bool IsFieldTargetStep(EffectStep s) => s is DestroyTargetsStep or ReturnTargetsToHandStep or ChangeTargetsToDefenseStep or
        BanishFieldTargetsStep or NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep or DestroyAndBanishTargetsStep or ModifyCombatantStatsStep;
    internal static bool IsAutomatic(EffectTarget t) => t is AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster;
    internal static bool IsZoneTargetStep(EffectStep s) => s is AddTargetsToHandStep or BanishTargetsStep or ReturnTargetsToGraveyardStep or
        ShuffleTargetsIntoDeckStep or SpecialSummonTargetStep;
    internal static int MinCount(EffectTarget t) => t switch { FieldTarget f => f.Count, ZoneTarget z => z.MinCount, _ => 1 };
    internal static int MaxCount(EffectTarget t) => t switch { FieldTarget f => f.Count, ZoneTarget z => z.MaxCount, SummonedMonsterTarget => 5, _ => 1 };
    internal bool RespondsOnly => Conditions.Any(c => c is RespondsToSpellActivationCondition or RespondsToActivationCondition);

    public EffectDefinition(string effectId, uint cardId, EffectKind kind, IEnumerable<EffectStep> steps,
        IEnumerable<EffectCondition>? conditions = null, EffectCost? cost = null, EffectTarget? target = null,
        bool locksOtherSummonsThisTurn = false, IEnumerable<EffectTarget>? fieldTargets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var snapshot = steps.Take(33).ToArray();
        if (snapshot.Length is 0 or > 32)
            throw new ArgumentException("A definition needs 1–32 resolution steps.", nameof(steps));
        foreach (var step in snapshot)
            if (step switch
                {
                    GainLifePointsStep s => s.Amount <= 0 || !Enum.IsDefined(s.Players) || !Enum.IsDefined(s.PerCount),
                    DealDamageStep s => s.Amount <= 0 || !Enum.IsDefined(s.PerCount),
                    DamageBothPlayersStep s => s.ToOpponent <= 0 || s.ToSelf <= 0,
                    // Validated by their constructors; the engine checks Token rules and the seed.
                    SummonTokensStep or ShuffleDeckStep or SpecialSummonStep => false,
                    DrawCardsStep s => s.Count is < 1 or > 10 || !Enum.IsDefined(s.Players),
                    MillTopOfDeckStep s => s.Count is < 1 or > 60,
                    AddFromDeckToHandStep s => s.Filter is null,
                    SendFromDeckToGraveyardStep s => s.Filter is null,
                    DestroyTargetStep or DestroyOpponentFaceUpMonstersStep or NegateActivationStep or NegateEffectStep or NegateSummonStep => false,
                    DestroyTargetsStep or ReturnTargetsToHandStep or ChangeTargetsToDefenseStep => false,
                    AddTargetsToHandStep or BanishTargetsStep or ReturnTargetsToGraveyardStep or ShuffleTargetsIntoDeckStep or SpecialSummonTargetStep => false,
                    BanishFieldTargetsStep or NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep or DestroyAndBanishTargetsStep => false,
                    ModifyCombatantStatsStep s => s.Attack == 0 && s.Defense == 0,
                    BanishAllStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
                    DestroyAllStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
                    ReturnAllToHandStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side),
                    ChangeAllToDefenseStep s => !Enum.IsDefined(s.Side),
                    DestroyExtremeMonsterStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Stat) || !Enum.IsDefined(s.Extreme) || !Enum.IsDefined(s.Filter),
                    _ => true
                })
                throw new ArgumentException("Steps must be supported Core operations with positive amounts.", nameof(steps));
        var required = (conditions ?? []).Take(9).ToArray();
        if (required.Length > 8 || required.Any(c => c is null) || required.Distinct().Count() != required.Length)
            throw new ArgumentException("Supply up to 8 distinct conditions.", nameof(conditions));
        if (required.OfType<MonsterSummonedCondition>().Any(c => c.Kinds is <= 0 or > SummonKinds.Any || !Enum.IsDefined(c.Stat) || c.Min < 0 || c.Min > c.Max))
            throw new ArgumentException("A summon condition needs summon kinds and a valid stat range.", nameof(conditions));
        if (required.OfType<BattleTimingCondition>().Any(c => c.Windows == BattleTiming.None || (c.Windows & ~BattleTiming.All) != 0 ||
                c.Windows.HasFlag(BattleTiming.Calculation) && !c.DuringCalculation))
            throw new ArgumentException("A battle timing condition needs supported timings; during calculation needs an explicit exception.", nameof(conditions));
        if (cost is not (null or DiscardCost or PayLifePointsCost or TributeCost or PayHalfLifePointsCost)) throw new ArgumentOutOfRangeException(nameof(cost));
        if (required.OfType<MonsterWouldBeSummonedCondition>().Any(c => c.Kinds is <= 0 || (c.Kinds & ~(SummonKinds.Normal | SummonKinds.Special)) != 0) ||
            snapshot.Any(s => s is NegateSummonStep) != required.Any(c => c is MonsterWouldBeSummonedCondition) || snapshot.Any(s => s is NegateSummonStep) && kind != EffectKind.CounterTrap)
            throw new ArgumentException("Summon negation needs a Counter Trap with a Normal/Special would-be-Summoned condition.", nameof(steps));
        if (target is not (null or SpellTrapOnFieldTarget)) throw new ArgumentOutOfRangeException(nameof(target));
        bool responds = required.Any(c => c is RespondsToSpellActivationCondition or RespondsToActivationCondition);
        if (required.OfType<RespondsToActivationCondition>().Any(c => c.Category is not (null or CardCategory.Monster or CardCategory.Spell or CardCategory.Trap)))
            throw new ArgumentOutOfRangeException(nameof(conditions));
        if (responds && kind == EffectKind.NormalSpell)
            throw new ArgumentException("A Normal Spell cannot be a chain response.", nameof(conditions));
        if (snapshot.Any(s => s is NegateActivationStep or NegateEffectStep) && !responds)
            throw new ArgumentException("Negating an activation requires a response condition.", nameof(steps));
        if ((target is not null) != snapshot.Any(s => s is DestroyTargetStep))
            throw new ArgumentException("A Spell/Trap target and a target-destruction step go together.", nameof(target));
        var fields = (fieldTargets ?? []).Take(6).ToArray();
        bool fieldSteps = snapshot.Any(IsFieldTargetStep), zoneSteps = snapshot.Any(IsZoneTargetStep);
        if (fields.Any(f => f is not (FieldTarget or ZoneTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster)) ||
            fields.Any(f => f is SummonedMonsterTarget) && (fields.Length != 1 || !required.Any(c => c is MonsterSummonedCondition)) ||
            fields.Any(f => f is AttackingMonsterTarget or DefendingMonsterTarget) && (fields.Length != 1 ||
                !required.Any(c => c is BattleTimingCondition || c is OpponentAttackDeclaredCondition && fields[0] is AttackingMonsterTarget)) ||
            snapshot.Any(s => s is ModifyCombatantStatsStep) && fields is not [AttackingMonsterTarget or DefendingMonsterTarget] ||
            snapshot.Any(s => s is NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep) && fields is not [AttackingMonsterTarget] || fields.Sum(MaxCount) > 5 || fields.Length != 0 && target is not null ||
            (fields.Length != 0) != (fieldSteps || zoneSteps) || fieldSteps && zoneSteps ||
            fieldSteps && fields.Any(f => f is not (FieldTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster)) || zoneSteps && fields.Any(f => f is not ZoneTarget) ||
            fields.SkipLast(1).Any(f => MinCount(f) != MaxCount(f)) ||
            snapshot.Any(s => s is ChangeTargetsToDefenseStep) && fields.Any(f => f is FieldTarget { Kind: not FieldCardKind.Monster }) ||
            snapshot.Any(s => s is SpecialSummonTargetStep) && (fields.Length != 1 || fields[0] is not ZoneTarget { MaxCount: 1, MustBeSummonable: true, Zone: CardZone.Graveyard }))
            throw new ArgumentException("Field targets (up to 5) and the steps that use them go together.", nameof(fieldTargets));
        if (kind == EffectKind.MonsterTrigger && (required.Length != 0 || cost is not null || target is not null || fields is not ([] or [TriggerTargetedMonster]) || locksOtherSummonsThisTurn) ||
            kind != EffectKind.MonsterTrigger && fields.Any(f => f is TriggerTargetedMonster))
            throw new ArgumentException("A monster trigger takes its timing and costs from its trigger profile.", nameof(kind));
        EffectId = effectId;
        CardId = cardId;
        Kind = kind;
        Steps = Array.AsReadOnly(snapshot);
        Conditions = Array.AsReadOnly(required);
        Cost = cost;
        Target = target;
        FieldTargets = Array.AsReadOnly(fields);
        LocksOtherSummonsThisTurn = locksOtherSummonsThisTurn;
    }
}

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone) : DuelCommand(Player);
/// <summary>The turn player activates a Quick-Play Spell from the hand as the next chain link.</summary>
public sealed record ActivateHandChainResponse(PlayerId Player, long DecisionId, int CardInstanceId, int SpellTrapZone)
    : DuelCommand(Player);
public sealed record EffectDamageDealt(PlayerId Player, int Damage, int LifePointsBefore,
    int LifePointsAfter, string EffectId) : DuelEvent;
/// <summary>The player's Deck was shuffled; carries no card order.</summary>
public sealed record DeckShuffled(PlayerId Player, string EffectId) : DuelEvent;
/// <summary>A Token left the field and ceased to exist; follows the departure's cause event.</summary>
public sealed record TokenRemoved(PlayerId Owner, CardInstance Card) : DuelEvent;

/// <summary>A turn-scoped "cannot Summon other monsters" restriction from an activated definition.</summary>
public sealed record SummonLock(PlayerId Player, int TurnNumber, CardInstance Source, string EffectId);
public sealed record SummonLockApplied(SummonLock Lock) : DuelEvent;

/// <summary>A definition step paused at resolution for its controller's choice.</summary>
public abstract class PendingDefinitionChoice
{
    public long DecisionId { get; }
    public PlayerId Player { get; }
    public string EffectId { get; }
    public int LinkNumber { get; }
    public int StepIndex { get; }

    private protected PendingDefinitionChoice(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex)
    {
        DecisionId = decisionId; Player = player; EffectId = effectId; LinkNumber = linkNumber; StepIndex = stepIndex;
    }
}

/// <summary>Resolution-time Special Summon choice; deck options are visible only to the choosing player.</summary>
public sealed record DefinitionSummonOption(CardInstance Card, DecisionCardSource Source,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    PlayerId? From = null);
public sealed class PendingDefinitionSummon : PendingDefinitionChoice
{
    public IReadOnlyList<DefinitionSummonOption> Options { get; }
    public IReadOnlyList<int> MonsterZoneOptions { get; }
    public IReadOnlyList<BattlePosition> PositionOptions { get; }
    /// <summary>Monsters already chosen for an any-number Summon; all are placed together.</summary>
    public IReadOnlyList<ChosenDefinitionSummon> Picks { get; }
    public bool CanFinish { get; }

    internal PendingDefinitionSummon(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex,
        IEnumerable<DefinitionSummonOption> options, IEnumerable<int> zones, BattlePosition? onlyPosition = null,
        IEnumerable<ChosenDefinitionSummon>? picks = null, bool canFinish = false)
        : base(decisionId, player, effectId, linkNumber, stepIndex)
    {
        Picks = Array.AsReadOnly(picks?.ToArray() ?? []);
        CanFinish = canFinish;
        Options = Array.AsReadOnly(options.ToArray());
        MonsterZoneOptions = Array.AsReadOnly(zones.ToArray());
        PositionOptions = Array.AsReadOnly(onlyPosition is { } only ? [only] : new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense });
    }
}
public sealed record ChooseDefinitionSummon(PlayerId Player, long DecisionId, int CardInstanceId, int MonsterZone,
    BattlePosition Position) : DuelCommand(Player);
public sealed record ChosenDefinitionSummon(CardInstance Card, DecisionCardSource Source, int MonsterZone, BattlePosition Position);
/// <summary>Ends an any-number Summon with the monsters chosen so far.</summary>
public sealed record FinishDefinitionSummon(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record DefinitionSummonRequested(PendingDefinitionSummon Decision) : DuelEvent;

/// <summary>Resolution-time choice of one Deck card (search or send); options are visible only to the choosing player.</summary>
public sealed class PendingDefinitionCardChoice : PendingDefinitionChoice
{
    public IReadOnlyList<CardInstance> Options { get; }

    internal PendingDefinitionCardChoice(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex,
        IEnumerable<CardInstance> options) : base(decisionId, player, effectId, linkNumber, stepIndex) =>
        Options = Array.AsReadOnly(options.ToArray());
}
public sealed record ChooseDefinitionCard(PlayerId Player, long DecisionId, int CardInstanceId) : DuelCommand(Player);
public sealed record DefinitionCardChoiceRequested(PendingDefinitionCardChoice Decision) : DuelEvent;
/// <summary>A Deck card was revealed and added to its owner's hand by an effect.</summary>
public sealed record CardSearched(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
/// <summary>A Deck card went to its owner's Graveyard by an effect (a chosen card or the top of the Deck).</summary>
public sealed record CardSentFromDeckToGraveyard(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
