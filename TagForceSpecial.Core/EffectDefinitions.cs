namespace TagForceSpecial.Core;

/// <summary>Card activation kinds. Spell Speed and set-turn rules derive from the kind.</summary>
public enum EffectKind { NormalSpell, QuickPlaySpell, NormalTrap, CounterTrap }

/// <summary>
/// One ordered resolution operation. The set is closed: only Core defines steps, each with its own
/// contract and tests. Values are authored, never parsed from card text.
/// </summary>
public abstract record EffectStep
{
    private protected EffectStep() { }
}

/// <summary>Which players an LP step applies to, relative to the activating player.</summary>
public enum EffectPlayers { Self, Opponent, Both }

/// <summary>
/// A public count an LP amount is multiplied by, read at resolution. A definition with a counted step can be
/// activated only while every counted step's count is at least 1 (deliberate PC reading of the original-era
/// rulings; no PSP observation). The set is closed.
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

/// <summary>
/// Shuffles the activating player's Deck with the duel's seeded generator. The event carries no order; views
/// expose only the Deck count. Definitions using it require an engine seed.
/// </summary>
public sealed record ShuffleDeckStep : EffectStep;

/// <summary>
/// Special Summons the listed Tokens (one per entry, in order) to the activating player's lowest free Monster
/// Zones. All or nothing: with fewer free zones than Tokens at resolution, none are summoned (link skipped,
/// <see cref="ChainSkipReason.SummonUnavailable"/>). Every ID needs explicit <c>IsToken</c> card rules.
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
/// Destroys the Spell/Trap bound at activation (<see cref="SpellTrapOnFieldTarget"/>). A target that left or was
/// replaced is not retargeted: the step is skipped (<see cref="ChainSkipReason.TargetUnavailable"/>), as the native
/// Typhoon validator returns without destroying.
/// </summary>
public sealed record DestroyTargetStep : EffectStep;

/// <summary>Destroys every face-up monster the activating player's opponent controls, in zone order.</summary>
public sealed record DestroyOpponentFaceUpMonstersStep : EffectStep;

/// <summary>
/// Negates the activation of the link this one responded to and, when <paramref name="DestroySource"/>, destroys
/// that card if it is still in place. Requires a <see cref="RespondsToSpellActivationCondition"/>.
/// </summary>
public sealed record NegateActivationStep(bool DestroySource) : EffectStep;

/// <summary>
/// Special Summons one eligible card chosen at resolution from the listed sources (face-up Attack or Defense, any
/// free zone). Eligibility is an authored card-ID list, never decoded at duel time. A Deck summon shuffles the
/// Deck afterwards (the definition then requires an engine seed). With
/// <see cref="BanishAtEndPhase"/>, the summoned placement is banished in that turn's End Phase. No eligible card
/// at resolution skips the step (<see cref="ChainSkipReason.SummonUnavailable"/>).
/// </summary>
public sealed record SpecialSummonStep : EffectStep
{
    public SpecialSummonSources Sources { get; }
    public IReadOnlyList<uint> EligibleCardIds { get; }
    public bool BanishAtEndPhase { get; }

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
/// "When a Spell Card is activated": the preceding chain link is a Spell card's activation (a Pendulum scale
/// activation counts). Monster and trigger effects do not. Makes the definition a response only.
/// </summary>
public sealed record RespondsToSpellActivationCondition : EffectCondition;

/// <summary>The activating player has not Summoned a monster this turn; Sets do not count.</summary>
public sealed record NoSummonThisTurnCondition : EffectCondition;

/// <summary>An activation cost, paid at commit and never refunded.</summary>
public abstract record EffectCost
{
    private protected EffectCost() { }
}

/// <summary>Discard 1 card from the hand (any card other than the activated source).</summary>
public sealed record DiscardCost : EffectCost;

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
    /// <summary>
    /// On activation, the activating player cannot Summon other monsters for the rest of the turn (Sets remain
    /// legal). Applied at commit, so it holds even if the activation is later negated.
    /// </summary>
    public bool LocksOtherSummonsThisTurn { get; }

    public ChainSpellSpeed SpellSpeed => Kind switch
    {
        EffectKind.NormalSpell => ChainSpellSpeed.One,
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
    internal bool RespondsOnly => Conditions.Any(c => c is RespondsToSpellActivationCondition);

    public EffectDefinition(string effectId, uint cardId, EffectKind kind, IEnumerable<EffectStep> steps,
        IEnumerable<EffectCondition>? conditions = null, EffectCost? cost = null, EffectTarget? target = null,
        bool locksOtherSummonsThisTurn = false)
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
                    DestroyTargetStep or DestroyOpponentFaceUpMonstersStep or NegateActivationStep => false,
                    _ => true
                })
                throw new ArgumentException("Steps must be supported Core operations with positive amounts.", nameof(steps));
        var required = (conditions ?? []).Take(9).ToArray();
        if (required.Length > 8 || required.Any(c => c is null) || required.Distinct().Count() != required.Length)
            throw new ArgumentException("Supply up to 8 distinct conditions.", nameof(conditions));
        if (cost is not (null or DiscardCost)) throw new ArgumentOutOfRangeException(nameof(cost));
        if (target is not (null or SpellTrapOnFieldTarget)) throw new ArgumentOutOfRangeException(nameof(target));
        bool responds = required.Any(c => c is RespondsToSpellActivationCondition);
        if (responds && kind == EffectKind.NormalSpell)
            throw new ArgumentException("A Normal Spell cannot be a chain response.", nameof(conditions));
        if (snapshot.OfType<NegateActivationStep>().Any() && !responds)
            throw new ArgumentException("Negating an activation requires a response condition.", nameof(steps));
        if ((target is not null) != snapshot.Any(s => s is DestroyTargetStep))
            throw new ArgumentException("A Spell/Trap target and a target-destruction step go together.", nameof(target));
        EffectId = effectId;
        CardId = cardId;
        Kind = kind;
        Steps = Array.AsReadOnly(snapshot);
        Conditions = Array.AsReadOnly(required);
        Cost = cost;
        Target = target;
        LocksOtherSummonsThisTurn = locksOtherSummonsThisTurn;
    }
}

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone) : DuelCommand(Player);
/// <summary>The turn player activates a Quick-Play Spell from the hand as the next chain link.</summary>
public sealed record ActivateHandChainResponse(PlayerId Player, long DecisionId, int CardInstanceId, int SpellTrapZone)
    : DuelCommand(Player);
public sealed record EffectDamageDealt(PlayerId Player, int Damage, int LifePointsBefore,
    int LifePointsAfter, string EffectId) : DuelEvent;
/// <summary>The player's Deck was shuffled. Deliberately carries no card order.</summary>
public sealed record DeckShuffled(PlayerId Player, string EffectId) : DuelEvent;
/// <summary>A Token left the field and ceased to exist; follows the departure's cause event.</summary>
public sealed record TokenRemoved(PlayerId Owner, CardInstance Card) : DuelEvent;

/// <summary>A turn-scoped "cannot Summon other monsters" restriction from an activated definition.</summary>
public sealed record SummonLock(PlayerId Player, int TurnNumber, CardInstance Source, string EffectId);
public sealed record SummonLockApplied(SummonLock Lock) : DuelEvent;

/// <summary>Resolution-time Special Summon choice; deck options are visible only to the choosing player.</summary>
public sealed record DefinitionSummonOption(CardInstance Card, DecisionCardSource Source);
public sealed class PendingDefinitionSummon
{
    public long DecisionId { get; }
    public PlayerId Player { get; }
    public string EffectId { get; }
    public int LinkNumber { get; }
    public int StepIndex { get; }
    public IReadOnlyList<DefinitionSummonOption> Options { get; }
    public IReadOnlyList<int> MonsterZoneOptions { get; }
    public IReadOnlyList<BattlePosition> PositionOptions { get; } =
        Array.AsReadOnly(new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense });

    internal PendingDefinitionSummon(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex,
        IEnumerable<DefinitionSummonOption> options, IEnumerable<int> zones)
    {
        DecisionId = decisionId; Player = player; EffectId = effectId; LinkNumber = linkNumber; StepIndex = stepIndex;
        Options = Array.AsReadOnly(options.ToArray());
        MonsterZoneOptions = Array.AsReadOnly(zones.ToArray());
    }
}
public sealed record ChooseDefinitionSummon(PlayerId Player, long DecisionId, int CardInstanceId, int MonsterZone,
    BattlePosition Position) : DuelCommand(Player);
public sealed record DefinitionSummonRequested(PendingDefinitionSummon Decision) : DuelEvent;
