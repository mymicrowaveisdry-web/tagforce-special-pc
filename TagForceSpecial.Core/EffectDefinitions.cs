namespace TagForceSpecial.Core;

/// <summary>Card activation kinds. Spell Speed and set-turn rules derive from the kind.</summary>
public enum EffectKind
{
    NormalSpell, QuickPlaySpell, NormalTrap, CounterTrap, MonsterTrigger, ContinuousSpell, ContinuousTrap, FieldSpell, EquipSpell,
    /// <summary>A face-up monster's Spell Speed 1 effect, activated in its controller's open Main Phase.</summary>
    MonsterIgnition,
    /// <summary>A face-up monster's Spell Speed 2 effect, also usable as a chain response.</summary>
    MonsterQuick,
    /// <summary>A face-up Continuous, Field or Equip card's Spell Speed 1 effect, activated in its controller's open Main Phase.</summary>
    SpellTrapIgnition,
    /// <summary>A face-up Continuous, Field or Equip card's Spell Speed 2 effect, also usable as a response.</summary>
    SpellTrapQuick
}

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
    OpponentBanished,
    /// <summary>The effect's latest die roll; it cannot be known at activation.</summary>
    DieRoll,
    /// <summary>Heads in the effect's latest coin toss; it cannot be known at activation.</summary>
    Heads,
    /// <summary>The Level of the monster the effect last sent from the top of the Deck; it cannot be known at activation.</summary>
    MilledLevel,
    /// <summary>Monsters the activating player controls (face-down included).</summary>
    OwnMonsters,
    /// <summary>Face-up monsters the activating player controls.</summary>
    OwnFaceUpMonsters
}

/// <summary>
/// The listed players gain <see cref="Amount"/> LP (times <see cref="PerCount"/> when set), controller first, with
/// the existing integer saturation policy.
/// </summary>
public sealed record GainLifePointsStep(int Amount, EffectPlayers Players = EffectPlayers.Self,
    EffectCount PerCount = EffectCount.None) : EffectStep
{
    /// <summary>When set, only matching cards are counted.</summary>
    public CardFilter? CountFilter { get; init; }
}

/// <summary>
/// Effect damage to the activating player's opponent (times <see cref="PerCount"/> when set); zero LP is an
/// immediate terminal defeat.
/// </summary>
public sealed record DealDamageStep(int Amount, EffectCount PerCount = EffectCount.None) : EffectStep
{
    /// <summary>When set, only matching cards are counted.</summary>
    public CardFilter? CountFilter { get; init; }
}

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
/// Special Summons one eligible hand, Deck or Graveyard card chosen at resolution, shuffling after a Deck summon. Optionally
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
        if (sources == SpecialSummonSources.None || (sources & ~(SpecialSummonSources.Hand | SpecialSummonSources.Deck | SpecialSummonSources.Graveyard)) != 0)
            throw new ArgumentOutOfRangeException(nameof(sources), "Only hand, Deck and Graveyard sources are supported.");
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
        if (sources == SpecialSummonSources.None || (sources & ~(SpecialSummonSources.Hand | SpecialSummonSources.Deck | SpecialSummonSources.Graveyard)) != 0)
            throw new ArgumentOutOfRangeException(nameof(sources), "Only hand, Deck and Graveyard sources are supported.");
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

/// <summary>Each affected player chooses cards from their own hand at resolution; all chosen cards are discarded together.</summary>
public sealed record DiscardCardsStep(int Count, EffectPlayers Players = EffectPlayers.Self, CardFilter? Filter = null, int? Minimum = null) : EffectStep;

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

/// <summary>The activating player controls no monsters.</summary>
public sealed record ControlsNoMonstersCondition : EffectCondition;

/// <summary>
/// "If [you/your opponent] control(s) [a face-up X]": the number of monsters matching <paramref name="Filter"/> on <paramref name="Side"/>
/// (relative to the activating player) is within [<paramref name="Min"/>, <paramref name="Max"/>]; checked when activating only. Only face-up
/// monsters count unless <paramref name="FaceUpOnly"/> is false ("controls 2 or more monsters").
/// </summary>
public sealed record ControlsMonstersCondition(CardFilter Filter, TargetSide Side = TargetSide.Own, int Min = 1, int Max = int.MaxValue,
    bool FaceUpOnly = true) : EffectCondition;

/// <summary>Ends the effect unless the activating player controls no monsters at this point of resolution.</summary>
public sealed record StopUnlessNoOwnMonstersStep : EffectStep;

/// <summary>The activating player has not Summoned a monster this turn; Sets do not count.</summary>
public sealed record NoSummonThisTurnCondition : EffectCondition;

/// <summary>An activation cost, paid at commit and never refunded.</summary>
public abstract record EffectCost
{
    private protected EffectCost() { }
}

/// <summary>Discard cards from the hand other than the source; <see cref="Filter"/> limits which cards qualify.</summary>
public sealed record DiscardCost(CardFilter? Filter = null, int Count = 1) : EffectCost;

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
    /// <summary>Stat changes that hold while a lasting card's effect is active.</summary>
    public IReadOnlyList<LastingStatModifier> LastingModifiers { get; }
    /// <summary>The card stays face-up on the field after resolving, unless its activation was negated.</summary>
    public bool IsLasting => Kind is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell;
    public bool IsMonsterEffect => Kind is EffectKind.MonsterIgnition or EffectKind.MonsterQuick;
    /// <summary>What this card negates while its effect is active.</summary>
    public LastingNegation Negation { get; }
    /// <summary>LP its controller pays each of their Standby Phases, or destroys the card; 0 for none.</summary>
    public int UpkeepLifePoints { get; }
    /// <summary>This activated effect can be used once per turn, per card.</summary>
    public bool OncePerTurn { get; }
    /// <summary>Effects with the same key share one use per turn; by default each effect has its own.</summary>
    public string OncePerTurnKey { get; }
    /// <summary>A limit shared by every copy the player controls, checked before activation.</summary>
    public NameLimit? NameLimit { get; private set; }

    /// <summary>Type or Attribute changes that apply while this lasting card is face-up, using its declared value.</summary>
    public IReadOnlyList<LastingTraitChange> LastingTraits { get; private set; } = [];

    /// <summary>A copy of this definition whose lasting card also applies <paramref name="change"/>.</summary>
    public EffectDefinition WithLastingTrait(LastingTraitChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!Enum.IsDefined(change.Trait) || !Enum.IsDefined(change.Scope) || Kind is not (EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.EquipSpell))
            throw new ArgumentException("A lasting Type or Attribute change belongs to a Continuous or Equip card.", nameof(change));
        var copy = (EffectDefinition)MemberwiseClone();
        copy.LastingTraits = Array.AsReadOnly([.. LastingTraits, change]);
        return copy;
    }

    /// <summary>What this effect declares when activated; steps read it through <see cref="DeclaredFilter"/>.</summary>
    public DeclarationKind? Declares { get; private set; }

    /// <summary>A copy of this definition that declares <paramref name="kind"/> when activated.</summary>
    public EffectDefinition WithDeclaration(DeclarationKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var copy = (EffectDefinition)MemberwiseClone();
        copy.Declares = kind;
        return copy;
    }

    /// <summary>The declaration is made as the link starts resolving, not as it is activated (text without a ";").</summary>
    public bool DeclaresAtResolution { get; private set; }

    /// <summary>A copy of this Continuous card that declares <paramref name="kind"/> as it resolves; its triggers read the value.</summary>
    public EffectDefinition WithDeclarationAtResolution(DeclarationKind kind)
    {
        if (Kind is not (EffectKind.ContinuousSpell or EffectKind.ContinuousTrap))
            throw new ArgumentException("Only a Continuous Spell or Trap declares as it resolves.", nameof(kind));
        var copy = WithDeclaration(kind);
        copy.DeclaresAtResolution = true;
        return copy;
    }

    /// <summary>A copy of this definition with a by-name limit.</summary>
    public EffectDefinition WithNameLimit(NameLimit limit)
    {
        ArgumentNullException.ThrowIfNull(limit);
        var copy = (EffectDefinition)MemberwiseClone();
        copy.NameLimit = limit;
        return copy;
    }
    /// <summary>Where this effect is used from.</summary>
    public EffectOrigin Origin { get; }
    public bool IsFaceUpEffect => Kind is EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick;
    /// <summary>ATK this monster gains for each counter on it.</summary>
    public int AttackPerCounter { get; }
    /// <summary>The phase of its controller's turn in which the upkeep is owed.</summary>
    public DuelPhase UpkeepPhase { get; }
    /// <summary>The destructions of the equipped monster this Union takes instead.</summary>
    public DestructionCause UnionReplaces { get; }
    /// <summary>When above zero, this card gains a counter each time a Spell resolves, up to this total.</summary>
    public int SpellCounterMax { get; }
    /// <summary>This monster cannot attack the turn this effect is activated, and cannot activate it after attacking.</summary>
    public bool LocksOwnAttack { get; }

    public ChainSpellSpeed SpellSpeed => Kind switch
    {
        EffectKind.NormalSpell or EffectKind.MonsterTrigger or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell or
            EffectKind.MonsterIgnition or EffectKind.SpellTrapIgnition => ChainSpellSpeed.One,
        EffectKind.CounterTrap => ChainSpellSpeed.Three,
        _ => ChainSpellSpeed.Two
    };

    internal bool SummonsMonsters => Steps.Any(s => s is SummonTokensStep or SpecialSummonStep or SpecialSummonSelfStep);

    internal IEnumerable<(EffectCount Count, CardFilter? Filter)> Counts => Steps.Select(s => s switch
    {
        GainLifePointsStep g => (g.PerCount, g.CountFilter),
        DealDamageStep d => (d.PerCount, d.CountFilter),
        _ => (EffectCount.None, null)
    }).Where(c => c.Item1 != EffectCount.None);
    internal static bool IsFieldTargetStep(EffectStep s) => s is DestroyTargetsStep or ReturnTargetsToHandStep or ChangeTargetsToDefenseStep or
        BanishFieldTargetsStep or NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep or DestroyAndBanishTargetsStep or ModifyCombatantStatsStep or
        EquipTargetStep or UnionEquipStep or PlaceCounterStep or ModifyTargetsUntilEndStep or FlipTargetsFaceDownStep or FlipTargetsFaceUpStep or
        AttachSourceStep or AttachTargetsToSelfStep or DetachAllStep or RankUpStep or AttachFromStep { ToTarget: true } or
        TakeControlStep or GiveControlStep or SwitchControlStep or LockTargetsUntilEndStep or ChangeTargetsTraitStep or ChangeTargetsNameStep or SetTargetScaleStep or
        DestroyTargetAtStep or GiveTargetControlStep or ModifyTargetsStep;
    /// <summary>Cost choices answered before the target choices, each binding a field target answer.</summary>
    internal int CostTargetSteps => Cost switch { TributeCost tribute => tribute.Count, BanishFromGraveyardCost banish => banish.Count, _ => 0 };
    internal static bool IsAutomatic(EffectTarget t) => t is AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster or TriggerSourceMonster;
    internal static bool IsZoneTargetStep(EffectStep s) => s is AddTargetsToHandStep or BanishTargetsStep or ReturnTargetsToGraveyardStep or
        ShuffleTargetsIntoDeckStep or SpecialSummonTargetStep;
    internal static int MinCount(EffectTarget t) => t switch { FieldTarget f => f.Count, ZoneTarget z => z.MinCount, _ => 1 };
    internal static int MaxCount(EffectTarget t) => t switch { FieldTarget f => f.Count, ZoneTarget z => z.MaxCount, SummonedMonsterTarget => 5, _ => 1 };
    internal bool RespondsOnly => Conditions.Any(c => c is RespondsToSpellActivationCondition or RespondsToActivationCondition);

    public EffectDefinition(string effectId, uint cardId, EffectKind kind, IEnumerable<EffectStep> steps,
        IEnumerable<EffectCondition>? conditions = null, EffectCost? cost = null, EffectTarget? target = null,
        bool locksOtherSummonsThisTurn = false, IEnumerable<EffectTarget>? fieldTargets = null,
        IEnumerable<LastingStatModifier>? lastingModifiers = null, LastingNegation negation = LastingNegation.None, int upkeepLifePoints = 0,
        bool oncePerTurn = false, int attackPerCounter = 0, bool isMarker = false, DuelPhase upkeepPhase = DuelPhase.Standby,
        DestructionCause unionReplaces = DestructionCause.Battle, int spellCounterMax = 0, bool locksOwnAttack = false, string? oncePerTurnKey = null, EffectOrigin origin = EffectOrigin.Field)
    {
        var (snapshot, lasting, required, fields) = EffectDefinitionValidation.Validate(effectId, cardId, kind, steps, conditions, cost, target,
            locksOtherSummonsThisTurn, fieldTargets, lastingModifiers, negation, upkeepLifePoints, oncePerTurn, attackPerCounter, isMarker,
            upkeepPhase, unionReplaces, spellCounterMax, locksOwnAttack, oncePerTurnKey, origin);
        EffectId = effectId;
        CardId = cardId;
        Kind = kind;
        Steps = Array.AsReadOnly(snapshot);
        Conditions = Array.AsReadOnly(required);
        Cost = cost;
        Target = target;
        FieldTargets = Array.AsReadOnly(fields);
        LocksOtherSummonsThisTurn = locksOtherSummonsThisTurn;
        LastingModifiers = Array.AsReadOnly(lasting);
        Negation = negation;
        UpkeepLifePoints = upkeepLifePoints;
        OncePerTurn = oncePerTurn;
        OncePerTurnKey = oncePerTurnKey ?? effectId;
        Origin = origin;
        AttackPerCounter = attackPerCounter;
        UpkeepPhase = upkeepPhase;
        UnionReplaces = unionReplaces;
        SpellCounterMax = spellCounterMax;
        LocksOwnAttack = locksOwnAttack;
    }
}
