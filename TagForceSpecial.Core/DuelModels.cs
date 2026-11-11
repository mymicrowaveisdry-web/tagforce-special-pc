namespace TagForceSpecial.Core;

public enum PlayerId { One, Two }
public enum DuelPhase { Draw, Standby, Main1, Battle, Main2, End }
public enum CardCategory { Unknown, Monster, Spell, Trap }
public enum BattlePosition { FaceUpAttack, FaceUpDefense, FaceDownDefense }
public enum DuelEndReason { UnableToDraw, LifePointsDepleted, BothLifePointsDepleted }

/// <summary>
/// Explicit gameplay rules for a card; each flag opts into one supported behavior. IsToken marks an effect-generated
/// Token that ceases to exist when it leaves the field.
/// </summary>
public sealed record DuelCardRules(CardCategory Category, bool AllowsNormalSummon = false,
    bool AllowsBasicBattle = false, bool AllowsSpellTrapSet = false,
    bool AllowsTributeSummon = false, bool AllowsTributeForSummon = false,
    bool RequiresRegisteredTributeProcedure = false, bool AllowsExtraDeck = false,
    bool AllowsSummonMaterial = false, bool AllowsPendulumSummon = false, bool IsToken = false,
    bool CannotBeSpecialSummoned = false, bool IsGemini = false, bool IsToon = false, bool IsToonWorld = false, bool IsTuner = false);
public sealed record CardInstance(int InstanceId, uint GameCardId, PlayerId Owner);
public sealed record FieldMonster(CardInstance Card, BattlePosition Position, int SummonedOnTurn = 0,
    int PositionChangedOnTurn = 0, int LastAttackTurn = 0, long PlacementId = 0, bool BattleDestroyed = false)
{
    public IReadOnlyList<CardInstance> XyzMaterials { get; internal init; } = Array.AsReadOnly(Array.Empty<CardInstance>());
    /// <summary>The turn this monster was last flipped face-up by battle.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int FlippedOnTurn { get; internal init; }
    /// <summary>A Gemini monster that has been Normal Summoned again and has its effects.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool GeminiActive { get; internal init; }
    /// <summary>How many monsters were Tributed to Normal Summon or Set it; null when it was not Normal Summoned or Set.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? SummonTributes { get; internal init; }
    /// <summary>A Level an effect gave it; null for its own Level.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public uint? Level { get; internal init; }
    /// <summary>Counters on this monster; a card effect places and removes them.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Counters { get; internal init; }
    /// <summary>Counters removed instead of this monster being destroyed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ProtectionCounters { get; internal init; }
    /// <summary>The turn this monster's own effect stopped it from attacking.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int AttackLockedTurn { get; internal init; }
}
/// <summary>A finished duel. A draw (both players at 0 LP at once) has no winner or loser.</summary>
public sealed record DuelOutcome(PlayerId? Winner, PlayerId? Loser, DuelEndReason Reason)
{
    /// <summary>Derived; excluded from serialization so traces and replays keep their existing shape.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDraw => Winner is null;
}

/// <summary>Immutable full-information engine snapshot. A future UI must hide private zones.</summary>
public sealed class DuelPlayerState
{
    public PlayerId Id { get; }
    public int LifePoints { get; }
    /// <summary>Top of deck is index zero. Setup accepts an already ordered deck.</summary>
    public IReadOnlyList<CardInstance> Deck { get; }
    public IReadOnlyList<CardInstance> Hand { get; }
    /// <summary>Ordered face-down Extra Deck, separate from public FaceUpExtraDeck entries.</summary>
    public IReadOnlyList<CardInstance> ExtraDeck { get; }
    public IReadOnlyList<CardInstance> FaceUpExtraDeck { get; }
    public IReadOnlyList<FieldSpellTrap?> PendulumZones { get; }
    public int LastPendulumSummonTurn { get; }
    public IReadOnlyList<FieldMonster?> MonsterZones { get; }
    /// <summary>Compatibility card view; inspect SpellTrapFields for face-up versus set state.</summary>
    public IReadOnlyList<CardInstance?> SpellTrapZones { get; }
    public IReadOnlyList<FieldSpellTrap?> SpellTrapFields { get; }
    /// <summary>The zone index that addresses the Field Zone in Spell/Trap commands, targets and events.</summary>
    public const int FieldZoneIndex = 5;
    public CardInstance? FieldZone => FieldSpell?.Card;
    /// <summary>The card in the Field Zone, face-up or set.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public FieldSpellTrap? FieldSpell => FieldZones[0];
    internal IReadOnlyList<FieldSpellTrap?> FieldZones { get; }
    internal FieldSpellTrap? SpellTrapAt(int zone) => zone == FieldZoneIndex ? FieldZones[0] : SpellTrapFields[zone];

    internal DuelPlayerState WithSpellTrapAt(int zone, FieldSpellTrap? card)
    {
        if (zone == FieldZoneIndex) return With(fieldZones: new[] { card });
        var slots = SpellTrapFields.ToArray();
        slots[zone] = card;
        return With(spellTraps: slots);
    }
    public IReadOnlyList<CardInstance> Graveyard { get; }
    public IReadOnlyList<CardInstance> Banished { get; }

    internal DuelPlayerState(PlayerId id, IEnumerable<CardInstance> deck, IEnumerable<CardInstance> hand,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null,
        IEnumerable<FieldSpellTrap?>? spellTraps = null, int lifePoints = 8000,
        IEnumerable<CardInstance>? banished = null, IEnumerable<CardInstance>? extraDeck = null,
        IEnumerable<CardInstance>? faceUpExtraDeck = null, IEnumerable<FieldSpellTrap?>? pendulumZones = null,
        int lastPendulumSummonTurn = 0, IEnumerable<FieldSpellTrap?>? fieldZones = null)
    {
        FieldZones = Array.AsReadOnly(fieldZones?.ToArray() ?? new FieldSpellTrap?[1]);
        Id = id;
        LifePoints = lifePoints;
        Deck = Array.AsReadOnly(deck.ToArray());
        Hand = Array.AsReadOnly(hand.ToArray());
        ExtraDeck = Array.AsReadOnly(extraDeck?.ToArray() ?? []);
        FaceUpExtraDeck = Array.AsReadOnly(faceUpExtraDeck?.ToArray() ?? []);
        PendulumZones = Array.AsReadOnly(pendulumZones?.ToArray() ?? new FieldSpellTrap?[2]);
        LastPendulumSummonTurn = lastPendulumSummonTurn;
        MonsterZones = Array.AsReadOnly(monsters?.ToArray() ?? new FieldMonster?[5]);
        SpellTrapFields = Array.AsReadOnly(spellTraps?.ToArray() ?? new FieldSpellTrap?[5]);
        SpellTrapZones = Array.AsReadOnly(SpellTrapFields.Select(s => s?.Card).ToArray());
        Graveyard = Array.AsReadOnly(graveyard?.ToArray() ?? []);
        Banished = Array.AsReadOnly(banished?.ToArray() ?? []);
    }

    internal DuelPlayerState With(IEnumerable<CardInstance>? deck = null, IEnumerable<CardInstance>? hand = null,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null,
        IEnumerable<FieldSpellTrap?>? spellTraps = null, int? lifePoints = null,
        IEnumerable<CardInstance>? banished = null, IEnumerable<CardInstance>? extraDeck = null,
        IEnumerable<CardInstance>? faceUpExtraDeck = null, IEnumerable<FieldSpellTrap?>? pendulumZones = null,
        int? lastPendulumSummonTurn = null, IEnumerable<FieldSpellTrap?>? fieldZones = null) =>
        new(Id, deck ?? Deck, hand ?? Hand, monsters ?? MonsterZones, graveyard ?? Graveyard,
            spellTraps ?? SpellTrapFields, lifePoints ?? LifePoints, banished ?? Banished, extraDeck ?? ExtraDeck,
            faceUpExtraDeck ?? FaceUpExtraDeck, pendulumZones ?? PendulumZones, lastPendulumSummonTurn ?? LastPendulumSummonTurn,
            fieldZones ?? FieldZones);
}

public sealed class DuelState
{
    public IReadOnlyList<DuelPlayerState> Players { get; }
    public PlayerId ActivePlayer { get; }
    public int TurnNumber { get; }
    public DuelPhase Phase { get; }
    public bool NormalSummonUsed { get; }
    public DuelOutcome? Outcome { get; }
    public long Revision { get; }
    public PendingDecision? PendingDecision { get; }
    public DecisionSequenceResult? DecisionResult { get; }
    /// <summary>Set only when a selection-only <see cref="DecisionSequence"/> with StopAfterCompletion finishes.</summary>
    public bool ResearchSliceComplete { get; }
    public PendingSpellActivation? PendingSpellActivation { get; }
    public IReadOnlyList<PendingEndPhaseBanishment> PendingEndPhaseBanishments { get; }
    public CommittedActivation? CommittedActivation { get; }
    public PendingChain? PendingChain { get; }
    public EventEffectState EventEffects { get; }
    public BattleState? Battle { get; }
    public PendingSummon? PendingSummon { get; }
    public IReadOnlyList<SummonRecord> SummonHistory { get; }
    internal DuelBookkeeping Bookkeeping { get; }
    /// <summary>Next card instance ID to allocate (e.g. for a Token). Monotonic: IDs are never reused.</summary>
    public int NextInstanceId => Bookkeeping.NextInstanceId;
    /// <summary>Seeded shuffle generator state; null when the duel has no seed (shuffling steps are then unavailable).
    /// Full-information engine state: client views must never expose it.</summary>
    public DuelRandomState? Random => Bookkeeping.Random;
    /// <summary>Non-turn-player activation window opened by a phase-change request, if any.</summary>
    public PendingOpenWindow? OpenWindow => Bookkeeping.OpenWindow;
    /// <summary>Summon locks applied by activations; each holds only during its own turn.</summary>
    public IReadOnlyList<SummonLock> SummonLocks => Bookkeeping.SummonLocks;

    internal DuelState(IEnumerable<DuelPlayerState> players, PlayerId activePlayer, int turnNumber = 1,
        DuelPhase phase = DuelPhase.Draw, bool normalSummonUsed = false, DuelOutcome? outcome = null,
        long revision = 0, PendingDecision? pendingDecision = null,
        DecisionSequenceResult? decisionResult = null, bool researchSliceComplete = false,
        PendingSpellActivation? pendingSpellActivation = null,
        IEnumerable<PendingEndPhaseBanishment>? pendingEndPhaseBanishments = null,
        CommittedActivation? committedActivation = null, PendingChain? pendingChain = null,
        EventEffectState? eventEffects = null, BattleState? battle = null, PendingSummon? pendingSummon = null,
        IEnumerable<SummonRecord>? summonHistory = null, DuelBookkeeping? bookkeeping = null)
    {
        Bookkeeping = bookkeeping ?? new();
        if (Bookkeeping.NextInstanceId < 1) throw new ArgumentOutOfRangeException(nameof(bookkeeping));
        Players = Array.AsReadOnly(players.ToArray());
        ActivePlayer = activePlayer;
        TurnNumber = turnNumber;
        Phase = phase;
        NormalSummonUsed = normalSummonUsed;
        Outcome = outcome;
        Revision = revision;
        PendingDecision = pendingDecision;
        DecisionResult = decisionResult;
        ResearchSliceComplete = researchSliceComplete;
        PendingSpellActivation = pendingSpellActivation;
        PendingEndPhaseBanishments = Array.AsReadOnly(pendingEndPhaseBanishments?.ToArray() ?? []);
        CommittedActivation = committedActivation;
        PendingChain = pendingChain;
        EventEffects = eventEffects ?? new();
        Battle = battle;
        PendingSummon = pendingSummon;
        SummonHistory = Array.AsReadOnly(summonHistory?.ToArray() ?? []);
    }

    public DuelPlayerState GetPlayer(PlayerId player) => player is PlayerId.One or PlayerId.Two
        ? Players[(int)player] : throw new ArgumentOutOfRangeException(nameof(player));

    internal DuelState With(DuelPlayerState? player = null, PlayerId? activePlayer = null,
        int? turnNumber = null, DuelPhase? phase = null, bool? normalSummonUsed = null, DuelOutcome? outcome = null,
        long? revision = null)
    {
        var players = Players.ToArray();
        if (player is not null) players[(int)player.Id] = player;
        return new(players, activePlayer ?? ActivePlayer, turnNumber ?? TurnNumber, phase ?? Phase,
            normalSummonUsed ?? NormalSummonUsed, outcome ?? Outcome, revision ?? Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);
    }

    internal DuelState WithDecision(PendingDecision? pending, DecisionSequenceResult? result = null,
        bool researchSliceComplete = false) => new(Players, ActivePlayer, TurnNumber, Phase,
            NormalSummonUsed, Outcome, Revision, pending, result, researchSliceComplete, PendingSpellActivation,
            PendingEndPhaseBanishments, CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithActivation(PendingSpellActivation? activation) => new(Players, ActivePlayer,
        TurnNumber, Phase, NormalSummonUsed, Outcome, Revision, PendingDecision, DecisionResult,
        ResearchSliceComplete, activation, PendingEndPhaseBanishments, CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithEndPhaseBanishments(IEnumerable<PendingEndPhaseBanishment> banishments) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, banishments, CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithChain(PendingChain? chain) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, CommittedActivation, chain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithBattle(BattleState? battle) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation,
            PendingEndPhaseBanishments, CommittedActivation, PendingChain, EventEffects, battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithEventEffects(EventEffectState eventEffects) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation,
            PendingEndPhaseBanishments, CommittedActivation, PendingChain, eventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithCommittedActivation(CommittedActivation activation) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, activation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping);

    internal DuelState WithSummon(PendingSummon? summon) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments,
            CommittedActivation, PendingChain, EventEffects, Battle, summon, SummonHistory, Bookkeeping);

    internal DuelState WithBookkeeping(DuelBookkeeping bookkeeping) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments,
            CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, bookkeeping);

    internal DuelState WithNextInstanceId(int next) =>
        next <= NextInstanceId ? throw new InvalidOperationException("Instance IDs are monotonic.")
        : new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments,
            CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, SummonHistory, Bookkeeping with { NextInstanceId = next });

    internal DuelState WithSummonHistory(IEnumerable<SummonRecord> history) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments,
            CommittedActivation, PendingChain, EventEffects, Battle, PendingSummon, history, Bookkeeping);
}

public abstract record DuelCommand(PlayerId Player);
public sealed record ResolveDrawStep(PlayerId Player) : DuelCommand(Player);
public sealed record ChangePhase(PlayerId Player, DuelPhase Target) : DuelCommand(Player);
public sealed record NormalSummon(PlayerId Player, int CardInstanceId, int MonsterZone) : DuelCommand(Player);
public sealed record SetMonster(PlayerId Player, int CardInstanceId, int MonsterZone) : DuelCommand(Player);
public sealed record SetSpellTrap(PlayerId Player, int CardInstanceId, int SpellTrapZone) : DuelCommand(Player);
public sealed record ChangeMonsterPosition(PlayerId Player, int MonsterZone, BattlePosition Target) : DuelCommand(Player);
/// <summary>Null target declares a direct attack. Commits a declaration in staged mode; otherwise resolves compatibility combat atomically.</summary>
public sealed record DeclareAttack(PlayerId Player, int AttackerZone, int? TargetZone = null) : DuelCommand(Player);
/// <summary>Normal Summons a face-up Gemini monster again so it gains its effects.</summary>
public sealed record GeminiSummon(PlayerId Player, int MonsterZone) : DuelCommand(Player);
public sealed record DiscardForHandLimit(PlayerId Player, int CardInstanceId) : DuelCommand(Player);
public sealed record EndTurn(PlayerId Player) : DuelCommand(Player);

public enum DuelError
{
    None, DuelFinished, InvalidPlayer, NotActivePlayer, WrongPhase, InvalidPhaseTransition,
    FirstTurnBattleNotAllowed, CardNotInHand, UnknownCardRules, NotMonster, NormalSummonNotAllowed,
    UnsupportedMonsterLevel, TributeSummonNotSupported, InvalidMonsterZone, MonsterZoneOccupied,
    NormalSummonLimitReached, HandLimitExceeded, DiscardNotRequired, UnsupportedCommand,
    NotSpellOrTrap, SpellTrapSetNotAllowed, InvalidSpellTrapZone, SpellTrapZoneOccupied,
    MonsterZoneEmpty, InvalidBattlePosition, PositionChangeNotAllowed, AlreadyAttacked,
    AttackerNotInAttackPosition, DirectAttackBlocked, UnsupportedBattleCard, UnsupportedBattleStatistics,
    UnsupportedDecisionSequence, DecisionPending, NoPendingDecision, StaleDecision,
    WrongDecisionKind, InvalidDecisionChoice, DecisionUnavailable, ResearchSliceComplete,
    UnsupportedSpellEffect, InvalidEffectState, UnsupportedLifecycleContinuation,
    ChainPending, NoPendingChain, StaleChainDecision, NotChainResponder, ChainResolving,
    UnsupportedChainResponse, UnsupportedChainResolution, ChainResponseSpeedNotAllowed,
    TriggerOrderPending, NoPendingTriggers, StaleTriggerDecision, NotTriggerPlayer, InvalidTriggerOrder,
    TriggerCommitmentPending, NoPendingTriggerCommitment, WrongTriggerCommitment, InvalidTriggerCommitment,
    BattleFlowNotEnabled, NoBattleInProgress, BattleWindowPending, BattleWindowClosed,
    StaleBattleDecision, NotBattleResponder, WrongBattleStep, AttackReplayPending,
    NoAttackReplay, UnsupportedBattleResponse,
    StaleSummonSelection, InvalidTributeSelection, TributeNotAllowed,
    UnsupportedTributeProcedure, SummonWindowPending, NoPendingSummon, StaleSummonDecision,
    NotSummonResponder, UnsupportedSummonResponse, InsufficientLifePoints,
    UnsupportedSpecialSummon, SpecialSummonSourceUnavailable, ProperSummonRequired, SpecialSummonConditionsNotMet,
    UnsupportedMaterialProcedure, InvalidSummonMaterials, SummonMaterialNotAllowed,
    UnsupportedPendulumCard, InvalidPendulumZone, PendulumZoneOccupied, PendulumScalesUnavailable, PendulumSummonLimitReached,
    ActivationTimingNotAllowed, SetThisTurn, OpenWindowPending, ActivationConditionNotMet, SummonLocked, NotGeminiMonster, GeminiAlreadyActive,
    UnsupportedMonsterEffect, EffectAlreadyUsed, UpkeepPending, NoUpkeepDue, ToonSummonUnavailable, ToonCannotAttack, ToonMustAttackToon, AttackLocked, LockedByEffect, MustAttack, PhaseSkipped
}

public abstract record DuelEvent
{
    public FieldCardDestination? FieldDestination { get; internal init; }
    // Rules timing, not renderer/log order. Absent means the transition's simultaneous group zero.
    internal int? TimingGroup { get; init; }
}
public sealed record CardDrawn(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record FirstDrawSkipped(PlayerId Player) : DuelEvent;
public sealed record PhaseChanged(DuelPhase From, DuelPhase To) : DuelEvent;
public sealed record MonsterNormalSummoned(PlayerId Player, FieldMonster Monster, int Zone, long? AttemptId = null) : DuelEvent
{
    /// <summary>The monsters Tributed for this Summon.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<MonsterBinding> Tributes { get; internal init; } = Array.AsReadOnly(Array.Empty<MonsterBinding>());
}
public sealed record MonsterSet(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record SpellTrapSet(PlayerId Player, CardInstance Card, int Zone) : DuelEvent;
public sealed record MonsterPositionChanged(PlayerId Player, int Zone, BattlePosition From, BattlePosition To) : DuelEvent;
public sealed record MonsterFlipSummoned(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record AttackDeclared(PlayerId Player, int AttackerZone, int? TargetZone) : DuelEvent;
public sealed record MonsterRevealed(PlayerId Player, int Zone, CardInstance Card) : DuelEvent;
public sealed record BattleDamageDealt(PlayerId Player, int Damage, int LifePointsBefore, int LifePointsAfter) : DuelEvent;
public sealed record MonsterDestroyed(PlayerId Player, int Zone, CardInstance Card) : DuelEvent
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ByBattle { get; internal init; }
    /// <summary>The monster that destroyed this one by battle.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MonsterBinding? DestroyedBy { get; internal init; }
}
/// <summary>Ordinary summon procedure, not destruction or an activation cost.</summary>
public sealed record MonsterTributed(MonsterBinding Monster) : DuelEvent;
public sealed record CardDiscarded(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record TurnStarted(PlayerId Player, int TurnNumber) : DuelEvent;
public sealed record DuelEnded(DuelOutcome Outcome) : DuelEvent;
public sealed record DuelActionResult(bool Succeeded, DuelError Error, DuelState State,
    IReadOnlyList<DuelEvent> Events);
