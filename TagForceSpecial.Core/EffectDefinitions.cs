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
    /// <summary>A count filter needs a count that enumerates cards.</summary>
    private static bool Filterable(EffectCount count, CardFilter? filter) => filter is null || count is EffectCount.OpponentMonsters or EffectCount.AllMonsters or
        EffectCount.OpponentHand or EffectCount.OpponentGraveyard or EffectCount.OpponentBanished or EffectCount.OwnMonsters or EffectCount.OwnFaceUpMonsters;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var snapshot = steps.Take(33).ToArray();
        var lasting = (lastingModifiers ?? []).Take(9).ToArray();
        bool lastingKind = kind is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell;
        bool monsterKind = kind is EffectKind.MonsterIgnition or EffectKind.MonsterQuick;
        bool unionEquip = snapshot.Any(s => s is UnionEquipStep);
        if (lasting.Length > 8 || lasting.Any(m => m is null) || lasting.Distinct().Count() != lasting.Length ||
            lasting.Length != 0 && !lastingKind && !unionEquip)
            throw new ArgumentException("Only lasting cards and Union equips take up to 8 distinct lasting modifiers.", nameof(lastingModifiers));
        if (!Enum.IsDefined(negation) || upkeepLifePoints < 0 || attackPerCounter < 0 ||
            (negation != LastingNegation.None || upkeepLifePoints != 0) && !lastingKind ||
            oncePerTurn && !monsterKind && kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) || oncePerTurnKey is not null && (!oncePerTurn || oncePerTurnKey.Length == 0) ||
            (attackPerCounter != 0 || locksOwnAttack) && !monsterKind)
            throw new ArgumentException("Negation and upkeep belong to lasting cards; once-per-turn and counter stats to activated monster effects.", nameof(negation));
        if (upkeepPhase is not (DuelPhase.Standby or DuelPhase.End) || unionReplaces is not (DestructionCause.Battle or DestructionCause.Any) ||
            spellCounterMax < 0 || spellCounterMax != 0 && !lastingKind && !monsterKind)
            throw new ArgumentException("Upkeep runs in the Standby or End Phase; a Union replaces battle or any destruction; Spell counters need a card that stays.", nameof(upkeepPhase));
        // A marker has no effect of its own: other cards' rules read it on the field.
        if (snapshot.Length > 32 || snapshot.Length == 0 && lasting.Length == 0 && negation == LastingNegation.None && !(isMarker && lastingKind) ||
            isMarker && (snapshot.Length != 0 || lasting.Length != 0 || negation != LastingNegation.None))
            throw new ArgumentException("A definition needs 1–32 resolution steps, or a lasting effect instead.", nameof(steps));
        foreach (var step in snapshot)
            if (step switch
                {
                    GainLifePointsStep s => s.Amount <= 0 || !Enum.IsDefined(s.Players) || !Enum.IsDefined(s.PerCount) || !Filterable(s.PerCount, s.CountFilter),
                    DealDamageStep s => s.Amount <= 0 || !Enum.IsDefined(s.PerCount) || !Filterable(s.PerCount, s.CountFilter),
                    DamageBothPlayersStep s => s.ToOpponent <= 0 || s.ToSelf <= 0,
                    TakeDamageStep s => s.Amount <= 0,
                    BanishSourceWhenItLeavesStep => false,
                    ModifyTargetsStep s => s.Attack == 0 && s.Defense == 0,
                    WeakenEventMonsterStep s => s.Attack <= 0 || kind != EffectKind.MonsterTrigger,
                    PayDownToGainStep s => s.Remaining < 1 || kind != EffectKind.MonsterTrigger,
                    SetDeclaredLevelStep s => s.Filter is null || s.MinLevel < 1 || s.MaxLevel > 12 || s.MinLevel > s.MaxLevel,
                    AddFromDeckOrGraveyardStep s => s.Filter is null,
                    AddFromGraveyardToHandStep s => s.Filter is null,
                    AddFromFaceUpExtraDeckStep s => s.Filter is null,
                    ReduceSourceScaleStep s => s.By < 1 || s.Minimum < 0 || kind != EffectKind.MonsterTrigger,
                    DestroyAboveSourceScaleStep s => s.Except is null || kind != EffectKind.MonsterTrigger,
                    BanishEventMonsterStep or BanishSourceCardStep => kind != EffectKind.MonsterTrigger,
                    RedirectRespondedDamageStep => false,
                    DiscardRandomStep s => s.Player is not (TargetSide.Own or TargetSide.Opponent),
                    // Validated by their constructors; the engine checks Token rules and the seed.
                    SummonTokensStep or ShuffleDeckStep or SpecialSummonStep => false,
                    DrawCardsStep s => s.Count is < 1 or > 10 || !Enum.IsDefined(s.Players),
                    MillTopOfDeckStep s => s.Count is < 1 or > 60,
                    AddFromDeckToHandStep s => s.Filter is null,
                    SendFromDeckToGraveyardStep s => s.Filter is null,
                    DestroyTargetStep or DestroyOpponentFaceUpMonstersStep or NegateActivationStep or NegateEffectStep or NegateSummonStep => false,
                    DestroyTargetsStep or ReturnTargetsToHandStep or ChangeTargetsToDefenseStep => false,
                    AddTargetsToHandStep or BanishTargetsStep or ReturnTargetsToGraveyardStep or ShuffleTargetsIntoDeckStep => false,
                    SpecialSummonTargetStep s => s.Position is not (null or BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense) || !Enum.IsDefined(s.Link),
                    EquipTargetStep or UnionEquipStep or PlaceCounterStep or FlipTargetsFaceDownStep or GrantNormalSummonStep or StopUnlessNoOwnMonstersStep => false,
                    SpecialSummonSelfStep s => s.Position is not (null or BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense),
                    ApplyRuleLockStep s => s.Lock.Actions == LockedAction.None || s.Lock.Scope != LockScope.Side || s.Lock.UsesPerTurn != 0,
                    ModifyTargetsUntilEndStep s => s.Attack == 0 && s.Defense == 0,
                    FlipTargetsFaceUpStep s => s.Position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense),
                    ReturnSourceToDeckTopStep => kind != EffectKind.MonsterTrigger,
                    BanishFieldTargetsStep or NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep or DestroyAndBanishTargetsStep => false,
                    AttachSourceStep or AttachTargetsToSelfStep or DetachAllStep or TakeOpponentMaterialStep => false,
                    AttachFromStep s => s.Filter is null || !s.Hand && !s.Graveyard,
                    RankUpStep s => s.Into is null || s.RankChange is 0 or < -12 or > 12,
                    ApplyGraveyardRedirectStep s => s.Rule is null || !Enum.IsDefined(s.Rule.To) || !Enum.IsDefined(s.Rule.Owner),
                    RollDieStep or LookAtOpponentHandStep => false,
                    TossCoinsStep s => s.Count is < 1 or > 5,
                    StopUnlessRollStep s => s.Values.Count == 0 || s.Values.Any(v => v is < 1 or > 6),
                    StopUnlessHeadsStep s => s.Minimum < 1,
                    ModifyAllUntilEndStep s => s.Attack == 0 && s.Defense == 0 || !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.PerCount),
                    ChooseFromOpponentHandStep s => !Enum.IsDefined(s.To),
                    EachPlayerTossesCoinStep s => s.HeadsGain < 0 || s.TailsDamage < 0 || s.HeadsGain == 0 && s.TailsDamage == 0,
                    DiceDuelStep s => s.PerPip < 1 || s.OnSix < 1,
                    OpponentCallsRandomHandCardStep or ReturnAttackerToHandStep => false,
                    StopUnlessHandHoldsStep s => s.Filter is null || !Enum.IsDefined(s.Side),
                    ExcavateStep s => s.Count is < 1 or > 10,
                    TakeExcavatedStep or SendExcavatedToGraveyardStep => false,
                    OpponentSendsFromDeckStep s => s.Filter is null,
                    OpponentAddsFromDeckStep s => s.Filter is null,
                    OpponentDiscardsMatchingStep s => s.Filter is null,
                    TakeControlStep s => !Enum.IsDefined(s.Duration),
                    GiveControlStep s => !Enum.IsDefined(s.Duration),
                    SwitchControlStep or SwapChosenMonstersStep => false,
                    DestroyTargetAtStep s => s.Target is < 0 or > 5,
                    GiveTargetControlStep s => s.Target is < 0 or > 5,
                    TakeChosenControlStep s => s.Filter is null || !Enum.IsDefined(s.Duration) || s.Duration == ControlDuration.WhileEquipped,
                    StopUnlessActivatedThisTurnStep => false,
                    SkipPhaseStep s => s.Phase is not (DuelPhase.Draw or DuelPhase.Standby or DuelPhase.Battle or DuelPhase.Main2) ||
                        s.Count is < 1 or > 3 || !Enum.IsDefined(s.Player),
                    EndBattlePhaseStep => false,
                    LockTargetsUntilEndStep s => s.Actions == LockedAction.None,
                    StopUnlessOwnTurnStep => false,
                    ChangeTargetsTraitStep s => !Enum.IsDefined(s.Trait),
                    ChangeTargetsNameStep => false,
                    SetTargetScaleStep => false,
                    StopUnlessSpecialSummonedStep or SendSourceToGraveyardStep => kind != EffectKind.MonsterTrigger,
                    StopUnlessMilledStep s => s.Filter is null,
                    ApplyDamageRuleStep s => s.Rule is null || !Enum.IsDefined(s.Rule.Kind) || (s.Rule.Applies & DamageKinds.Any) == 0 || !Enum.IsDefined(s.Scope),
                    ModifyCombatantStatsStep s => s.Attack == 0 && s.Defense == 0,
                    BanishAllStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
                    DestroyAllStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
                    ReturnAllToHandStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side),
                    ChangeAllToDefenseStep s => !Enum.IsDefined(s.Side),
                    DestroyExtremeMonsterStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Stat) || !Enum.IsDefined(s.Extreme) || !Enum.IsDefined(s.Filter),
                    _ => true
                })
                throw new ArgumentException("Steps must be supported Core operations with positive amounts.", nameof(steps));
        // A step that reads a die roll or coin toss must follow one in the same effect, or it would read an earlier effect's result.
        int roll = Array.FindIndex(snapshot, s => s is RollDieStep), toss = Array.FindIndex(snapshot, s => s is TossCoinsStep),
            mill = Array.FindIndex(snapshot, s => s is MillTopOfDeckStep), dig = Array.FindIndex(snapshot, s => s is ExcavateStep);
        static EffectCount CountOf(EffectStep s) => s switch
        {
            GainLifePointsStep g => g.PerCount, DealDamageStep d => d.PerCount, ModifyAllUntilEndStep m => m.PerCount, _ => EffectCount.None
        };
        for (int i = 0; i < snapshot.Length; i++)
            if ((snapshot[i] is StopUnlessRollStep || CountOf(snapshot[i]) == EffectCount.DieRoll) && (roll < 0 || roll > i) ||
                (snapshot[i] is StopUnlessHeadsStep || CountOf(snapshot[i]) == EffectCount.Heads) && (toss < 0 || toss > i) ||
                (snapshot[i] is StopUnlessMilledStep || CountOf(snapshot[i]) == EffectCount.MilledLevel) && (mill < 0 || mill > i) ||
                snapshot[i] is TakeExcavatedStep or SendExcavatedToGraveyardStep && (dig < 0 || dig > i))
                throw new ArgumentException("A die or coin result is read only after the effect rolls or tosses.", nameof(steps));
        var required = (conditions ?? []).Take(9).ToArray();
        if (required.Length > 8 || required.Any(c => c is null) || required.Distinct().Count() != required.Length)
            throw new ArgumentException("Supply up to 8 distinct conditions.", nameof(conditions));
        if (required.OfType<MonsterSummonedCondition>().Any(c => c.Kinds is <= 0 or > SummonKinds.Any || !Enum.IsDefined(c.Stat) || c.Min < 0 || c.Min > c.Max))
            throw new ArgumentException("A summon condition needs summon kinds and a valid stat range.", nameof(conditions));
        if (snapshot.Any(s => s is RedirectRespondedDamageStep) != required.Any(c => c is RespondsToDamageCondition) ||
            required.Any(c => c is RespondsToDamageCondition) && kind is not (EffectKind.QuickPlaySpell or EffectKind.NormalTrap or EffectKind.CounterTrap) ||
            required.Any(c => c is DirectAttackCondition) && !required.Any(c => c is OpponentAttackDeclaredCondition) ||
            required.Any(c => c is TributeCountCondition or XyzSummonedCondition) && !required.Any(c => c is MonsterSummonedCondition) ||
            required.OfType<TributeCountCondition>().Any(c => c.Tributes is < 1 or > 3))
            throw new ArgumentException("How a monster was Summoned narrows a summon condition.", nameof(conditions));
        if (required.OfType<BattleTimingCondition>().Any(c => c.Windows == BattleTiming.None || (c.Windows & ~BattleTiming.All) != 0 ||
                c.Windows.HasFlag(BattleTiming.Calculation) && !c.DuringCalculation))
            throw new ArgumentException("A battle timing condition needs supported timings; during calculation needs an explicit exception.", nameof(conditions));
        if (cost is not (null or DiscardCost or PayLifePointsCost or TributeCost or PayHalfLifePointsCost or TributeSelfCost or RemoveCounterCost or BanishFromGraveyardCost or DetachCost or SendSelfCost or RevealSelfCost) ||
            cost is TributeSelfCost or RemoveCounterCost or DetachCost && !monsterKind ||
            cost is DiscardCost { Count: < 1 or > 5 } or TributeCost { Count: < 1 or > 5 }) throw new ArgumentOutOfRangeException(nameof(cost));
        if (!Enum.IsDefined(origin) || origin is EffectOrigin.Hand or EffectOrigin.Graveyard && !monsterKind && kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) &&
                !(kind == EffectKind.MonsterTrigger && origin == EffectOrigin.Hand) ||
            origin == EffectOrigin.PendulumZone && (kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick or EffectKind.ContinuousSpell) ||
                cost is SendSelfCost or RevealSelfCost) ||
            cost is SendSelfCost self && (!Enum.IsDefined(self.Destination) || origin == EffectOrigin.Field || origin == EffectOrigin.Hand && self.Destination == SelfDestination.Banish ||
                origin == EffectOrigin.Graveyard && self.Destination != SelfDestination.Banish) || cost is RevealSelfCost && origin != EffectOrigin.Hand ||
            snapshot.Any(s => s is SpecialSummonSelfStep) && origin == EffectOrigin.Field)
            throw new ArgumentException("Hand and Graveyard effects belong to activated effects, with a self-cost that matches where the card is.", nameof(origin));
        if (required.OfType<MonsterWouldBeSummonedCondition>().Any(c => c.Kinds is <= 0 or > SummonKinds.Any) ||
            snapshot.Any(s => s is NegateSummonStep) != required.Any(c => c is MonsterWouldBeSummonedCondition) || snapshot.Any(s => s is NegateSummonStep) && kind != EffectKind.CounterTrap)
            throw new ArgumentException("Summon negation needs a Counter Trap with a would-be-Summoned condition.", nameof(steps));
        if (target is not (null or SpellTrapOnFieldTarget)) throw new ArgumentOutOfRangeException(nameof(target));
        bool responds = required.Any(c => c is RespondsToSpellActivationCondition or RespondsToActivationCondition);
        if (required.OfType<RespondsToActivationCondition>().Any(c => c.Category is not (null or CardCategory.Monster or CardCategory.Spell or CardCategory.Trap)))
            throw new ArgumentOutOfRangeException(nameof(conditions));
        if (responds && kind is EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell or EffectKind.SpellTrapIgnition)
            throw new ArgumentException("A Spell Speed 1 Spell cannot be a chain response.", nameof(conditions));
        if (snapshot.Any(s => s is NegateActivationStep or NegateEffectStep) && !responds)
            throw new ArgumentException("Negating an activation requires a response condition.", nameof(steps));
        if ((target is not null) != snapshot.Any(s => s is DestroyTargetStep))
            throw new ArgumentException("A Spell/Trap target and a target-destruction step go together.", nameof(target));
        var fields = (fieldTargets ?? []).Take(6).ToArray();
        bool fieldSteps = snapshot.Any(IsFieldTargetStep), zoneSteps = snapshot.Any(IsZoneTargetStep);
        if (fields.Any(f => f is not (FieldTarget or ZoneTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster or TriggerSourceMonster)) ||
            fields.Any(f => f is SummonedMonsterTarget) && (fields.Length != 1 || !required.Any(c => c is MonsterSummonedCondition)) ||
            fields.Any(f => f is AttackingMonsterTarget or DefendingMonsterTarget) && (fields.Length != 1 ||
                !required.Any(c => c is BattleTimingCondition || c is OpponentAttackDeclaredCondition && fields[0] is AttackingMonsterTarget)) ||
            snapshot.Any(s => s is ModifyCombatantStatsStep) && fields is not [AttackingMonsterTarget or DefendingMonsterTarget] ||
            snapshot.Any(s => s is NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep) && fields is not [AttackingMonsterTarget] || fields.Sum(MaxCount) > 5 || fields.Length != 0 && target is not null ||
            (fields.Length != 0) != (fieldSteps || zoneSteps) || fieldSteps && zoneSteps ||
            fieldSteps && fields.Any(f => f is not (FieldTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster or TriggerSourceMonster)) || zoneSteps && fields.Any(f => f is not ZoneTarget) ||
            fields.SkipLast(1).Any(f => MinCount(f) != MaxCount(f)) ||
            snapshot.Any(s => s is ChangeTargetsToDefenseStep) && fields.Any(f => f is FieldTarget { Kind: not FieldCardKind.Monster }) ||
            snapshot.Any(s => s is SpecialSummonTargetStep) && (fields.Length != 1 || fields[0] is not ZoneTarget { MaxCount: 1, MustBeSummonable: true, Zone: CardZone.Graveyard }))
            throw new ArgumentException("Field targets (up to 5) and the steps that use them go together.", nameof(fieldTargets));
        bool revivalEquip = snapshot.Any(s => s is SpecialSummonTargetStep { Link: RevivalLink.Equip });
        bool attaches = snapshot.Any(s => s is EquipTargetStep);
        if (kind == EffectKind.EquipSpell && attaches == revivalEquip || attaches && kind != EffectKind.EquipSpell || revivalEquip && kind != EffectKind.EquipSpell ||
            attaches && fields is not [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Filter: FieldCardFilter.FaceUp }] ||
            snapshot.Any(s => s is SpecialSummonTargetStep { Link: RevivalLink.Haunted }) && kind != EffectKind.ContinuousTrap ||
            unionEquip && (kind != EffectKind.MonsterIgnition || !oncePerTurn || fields is not [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own, Filter: FieldCardFilter.FaceUp, NoUnionEquipped: true }]) ||
            snapshot.Any(s => s is PlaceCounterStep) && fields is not ([TriggerSourceMonster] or [FieldTarget { Count: 1 }]) ||
            (snapshot.Any(s => s is AttachSourceStep or SpecialSummonTargetStep { Link: RevivalLink.Material }) &&
                kind is not (EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap)) ||
            snapshot.Any(s => s is AttachSourceStep or DetachAllStep or RankUpStep or AttachFromStep { ToTarget: true }) &&
                (fields.Length == 0 || fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster, Count: 1 }) ||
                 snapshot.Any(s => s is AttachSourceStep a && a.Target >= fields.Length || s is DetachAllStep d && d.Target >= fields.Length)) ||
            snapshot.Any(s => s is RankUpStep) && (fields is not [FieldTarget { Side: TargetSide.Own }] ||
                kind is not (EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap)) ||
            snapshot.Any(s => s is TakeOpponentMaterialStep) && !snapshot.Any(s => s is RankUpStep) ||
            snapshot.Any(s => s is AttachFromStep { ToTarget: false }) && (!monsterKind || fields.Length != 0) ||
            snapshot.Any(s => s is AttachTargetsToSelfStep) && (!monsterKind || fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster })))
            throw new ArgumentException("Equip, revival, Union and counter steps need their card kinds and bound targets.", nameof(steps));
        if (snapshot.OfType<DestroyTargetAtStep>().Any(s => s.Target >= fields.Length) ||
            snapshot.OfType<GiveTargetControlStep>().Any(s => s.Target >= fields.Length || fields[s.Target] is not FieldTarget { Kind: FieldCardKind.Monster, Side: TargetSide.Own, Count: 1 }) ||
            snapshot.Any(s => s is DestroyTargetAtStep or GiveTargetControlStep) && fields.Any(f => f is FieldTarget { Count: not 1 }))
            throw new ArgumentException("A step on one target needs a single target at that place; giving control needs your own monster.", nameof(steps));
        if (fields.Any(f => f is FieldTarget { CardFilter: SharedTraitFilter } || f is ZoneTarget { Filter: SharedTraitFilter } z && (fields.Length != 1 || z.MinCount != z.MaxCount || z.MinCount < 2)))
            throw new ArgumentException("A shared-trait filter belongs to a single Graveyard or banished target slot of a fixed count.", nameof(fieldTargets));
        // Taking a monster the opponent just Summoned binds it automatically.
        bool takesSummoned = snapshot.Any(s => s is TakeControlStep) && fields is [SummonedMonsterTarget] && required.OfType<MonsterSummonedCondition>().All(c => c.ByOpponent);
        if (snapshot.Any(s => s is TakeControlStep or GiveControlStep) && !takesSummoned && fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster }) ||
            snapshot.Any(s => s is TakeControlStep) && fields.Any(f => f is FieldTarget { Side: not TargetSide.Opponent }) ||
            snapshot.Any(s => s is GiveControlStep) && fields.Any(f => f is FieldTarget { Side: not TargetSide.Own }) ||
            snapshot.Any(s => s is SwitchControlStep) && fields is not ([FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Opponent },
                FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own }] or [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own },
                FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Opponent }]) ||
            snapshot.Any(s => s is TakeControlStep { Duration: ControlDuration.WhileEquipped } or GiveControlStep { Duration: ControlDuration.WhileEquipped }) &&
                kind != EffectKind.EquipSpell ||
            snapshot.Any(s => s is TakeControlStep { Duration: ControlDuration.WhileSourceFaceUp } or GiveControlStep { Duration: ControlDuration.WhileSourceFaceUp }
                or TakeChosenControlStep { Duration: ControlDuration.WhileSourceFaceUp }) && !monsterKind && kind != EffectKind.MonsterTrigger)
            throw new ArgumentException("Control steps need monster targets on the side they take from or give, one per side to switch.", nameof(steps));
        if (kind == EffectKind.MonsterTrigger && (required.Length != 0 || cost is not null || target is not null || fields is not ([] or [TriggerTargetedMonster] or [TriggerSourceMonster] or [ZoneTarget { MinCount: 1, MaxCount: 1 }] or [FieldTarget { Count: 1, ForTribute: false }]) || locksOtherSummonsThisTurn) ||
            kind != EffectKind.MonsterTrigger && fields.Any(f => f is TriggerTargetedMonster or TriggerSourceMonster))
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

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0) : DuelCommand(Player);
/// <summary>The turn player activates a Quick-Play Spell from the hand as the next chain link.</summary>
public sealed record ActivateHandChainResponse(PlayerId Player, long DecisionId, int CardInstanceId, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0)
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
