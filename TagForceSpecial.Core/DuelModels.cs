namespace TagForceSpecial.Core;

public enum PlayerId { One, Two }
public enum DuelPhase { Draw, Standby, Main1, Battle, Main2, End }
public enum CardCategory { Unknown, Monster, Spell, Trap }
public enum BattlePosition { FaceUpAttack, FaceUpDefense, FaceDownDefense }
public enum DuelEndReason { UnableToDraw, LifePointsDepleted }

/// <summary>
/// Explicit, trusted gameplay metadata; never inferred from unverified binary kind codes.
/// AllowsBasicBattle opts into unmodified combat with no effects resolved; callers must not
/// enable it where omitted mandatory/continuous effects would change the result.
/// AllowsSpellTrapSet opts into ordinary back-row placement, excluding Field Spells.
/// </summary>
public sealed record DuelCardRules(CardCategory Category, bool AllowsNormalSummon = false,
    bool AllowsBasicBattle = false, bool AllowsSpellTrapSet = false);
public sealed record CardInstance(int InstanceId, uint GameCardId, PlayerId Owner);
public sealed record FieldMonster(CardInstance Card, BattlePosition Position, int SummonedOnTurn = 0,
    int PositionChangedOnTurn = 0, int LastAttackTurn = 0);
public sealed record DuelOutcome(PlayerId Winner, PlayerId Loser, DuelEndReason Reason);

/// <summary>Immutable full-information engine snapshot. A future UI must hide private zones.</summary>
public sealed class DuelPlayerState
{
    public PlayerId Id { get; }
    public int LifePoints { get; }
    /// <summary>Top of deck is index zero. Setup accepts an already ordered deck.</summary>
    public IReadOnlyList<CardInstance> Deck { get; }
    public IReadOnlyList<CardInstance> Hand { get; }
    public IReadOnlyList<FieldMonster?> MonsterZones { get; }
    /// <summary>Compatibility card view; inspect SpellTrapFields for face-up versus set state.</summary>
    public IReadOnlyList<CardInstance?> SpellTrapZones { get; }
    public IReadOnlyList<FieldSpellTrap?> SpellTrapFields { get; }
    public CardInstance? FieldZone => null;
    public IReadOnlyList<CardInstance> Graveyard { get; }
    public IReadOnlyList<CardInstance> Banished { get; }

    internal DuelPlayerState(PlayerId id, IEnumerable<CardInstance> deck, IEnumerable<CardInstance> hand,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null,
        IEnumerable<FieldSpellTrap?>? spellTraps = null, int lifePoints = 8000,
        IEnumerable<CardInstance>? banished = null)
    {
        Id = id;
        LifePoints = lifePoints;
        Deck = Array.AsReadOnly(deck.ToArray());
        Hand = Array.AsReadOnly(hand.ToArray());
        MonsterZones = Array.AsReadOnly(monsters?.ToArray() ?? new FieldMonster?[5]);
        SpellTrapFields = Array.AsReadOnly(spellTraps?.ToArray() ?? new FieldSpellTrap?[5]);
        SpellTrapZones = Array.AsReadOnly(SpellTrapFields.Select(s => s?.Card).ToArray());
        Graveyard = Array.AsReadOnly(graveyard?.ToArray() ?? []);
        Banished = Array.AsReadOnly(banished?.ToArray() ?? []);
    }

    internal DuelPlayerState With(IEnumerable<CardInstance>? deck = null, IEnumerable<CardInstance>? hand = null,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null,
        IEnumerable<FieldSpellTrap?>? spellTraps = null, int? lifePoints = null,
        IEnumerable<CardInstance>? banished = null) =>
        new(Id, deck ?? Deck, hand ?? Hand, monsters ?? MonsterZones, graveyard ?? Graveyard,
            spellTraps ?? SpellTrapFields, lifePoints ?? LifePoints, banished ?? Banished);
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
    public bool ResearchSliceComplete { get; }
    public PendingSpellActivation? PendingSpellActivation { get; }
    public IReadOnlyList<PendingEndPhaseBanishment> PendingEndPhaseBanishments { get; }
    public CommittedActivation? CommittedActivation { get; }
    public PendingChain? PendingChain { get; }

    internal DuelState(IEnumerable<DuelPlayerState> players, PlayerId activePlayer, int turnNumber = 1,
        DuelPhase phase = DuelPhase.Draw, bool normalSummonUsed = false, DuelOutcome? outcome = null,
        long revision = 0, PendingDecision? pendingDecision = null,
        DecisionSequenceResult? decisionResult = null, bool researchSliceComplete = false,
        PendingSpellActivation? pendingSpellActivation = null,
        IEnumerable<PendingEndPhaseBanishment>? pendingEndPhaseBanishments = null,
        CommittedActivation? committedActivation = null, PendingChain? pendingChain = null)
    {
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
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, CommittedActivation, PendingChain);
    }

    internal DuelState WithDecision(PendingDecision? pending, DecisionSequenceResult? result = null,
        bool researchSliceComplete = false) => new(Players, ActivePlayer, TurnNumber, Phase,
            NormalSummonUsed, Outcome, Revision, pending, result, researchSliceComplete, PendingSpellActivation,
            PendingEndPhaseBanishments, CommittedActivation, PendingChain);

    internal DuelState WithActivation(PendingSpellActivation? activation) => new(Players, ActivePlayer,
        TurnNumber, Phase, NormalSummonUsed, Outcome, Revision, PendingDecision, DecisionResult,
        ResearchSliceComplete, activation, PendingEndPhaseBanishments, CommittedActivation, PendingChain);

    internal DuelState WithEndPhaseBanishments(IEnumerable<PendingEndPhaseBanishment> banishments) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, banishments, CommittedActivation, PendingChain);

    internal DuelState WithChain(PendingChain? chain) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, CommittedActivation, chain);

    internal DuelState WithCommittedActivation(CommittedActivation activation) =>
        new(Players, ActivePlayer, TurnNumber, Phase, NormalSummonUsed, Outcome, Revision,
            PendingDecision, DecisionResult, ResearchSliceComplete, PendingSpellActivation, PendingEndPhaseBanishments, activation, PendingChain);
}

public abstract record DuelCommand(PlayerId Player);
public sealed record ResolveDrawStep(PlayerId Player) : DuelCommand(Player);
public sealed record ChangePhase(PlayerId Player, DuelPhase Target) : DuelCommand(Player);
public sealed record NormalSummon(PlayerId Player, int CardInstanceId, int MonsterZone) : DuelCommand(Player);
public sealed record SetMonster(PlayerId Player, int CardInstanceId, int MonsterZone) : DuelCommand(Player);
public sealed record SetSpellTrap(PlayerId Player, int CardInstanceId, int SpellTrapZone) : DuelCommand(Player);
public sealed record ChangeMonsterPosition(PlayerId Player, int MonsterZone, BattlePosition Target) : DuelCommand(Player);
/// <summary>Null target declares a direct attack. Resolves an effect-free battle atomically.</summary>
public sealed record DeclareAttack(PlayerId Player, int AttackerZone, int? TargetZone = null) : DuelCommand(Player);
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
    UnsupportedChainResponse, UnsupportedChainResolution, ChainResponseSpeedNotAllowed
}

public abstract record DuelEvent;
public sealed record CardDrawn(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record FirstDrawSkipped(PlayerId Player) : DuelEvent;
public sealed record PhaseChanged(DuelPhase From, DuelPhase To) : DuelEvent;
public sealed record MonsterNormalSummoned(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record MonsterSet(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record SpellTrapSet(PlayerId Player, CardInstance Card, int Zone) : DuelEvent;
public sealed record MonsterPositionChanged(PlayerId Player, int Zone, BattlePosition From, BattlePosition To) : DuelEvent;
public sealed record MonsterFlipSummoned(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record AttackDeclared(PlayerId Player, int AttackerZone, int? TargetZone) : DuelEvent;
public sealed record MonsterRevealed(PlayerId Player, int Zone, CardInstance Card) : DuelEvent;
public sealed record BattleDamageDealt(PlayerId Player, int Damage, int LifePointsBefore, int LifePointsAfter) : DuelEvent;
public sealed record MonsterDestroyed(PlayerId Player, int Zone, CardInstance Card) : DuelEvent;
public sealed record CardDiscarded(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record TurnStarted(PlayerId Player, int TurnNumber) : DuelEvent;
public sealed record DuelEnded(DuelOutcome Outcome) : DuelEvent;
public sealed record DuelActionResult(bool Succeeded, DuelError Error, DuelState State,
    IReadOnlyList<DuelEvent> Events);
