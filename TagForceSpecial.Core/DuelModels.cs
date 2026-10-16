namespace TagForceSpecial.Core;

public enum PlayerId { One, Two }
public enum DuelPhase { Draw, Standby, Main1, Battle, Main2, End }
public enum CardCategory { Unknown, Monster, Spell, Trap }
public enum BattlePosition { FaceUpAttack }
public enum DuelEndReason { UnableToDraw }

/// <summary>Explicit, trusted gameplay metadata; never inferred from unverified binary kind codes.</summary>
public sealed record DuelCardRules(CardCategory Category, bool AllowsNormalSummon = false);
public sealed record CardInstance(int InstanceId, uint GameCardId, PlayerId Owner);
public sealed record FieldMonster(CardInstance Card, BattlePosition Position);
public sealed record DuelOutcome(PlayerId Winner, PlayerId Loser, DuelEndReason Reason);

/// <summary>Immutable full-information engine snapshot. A future UI must hide private zones.</summary>
public sealed class DuelPlayerState
{
    public PlayerId Id { get; }
    public int LifePoints => 8000;
    /// <summary>Top of deck is index zero. Setup accepts an already ordered deck.</summary>
    public IReadOnlyList<CardInstance> Deck { get; }
    public IReadOnlyList<CardInstance> Hand { get; }
    public IReadOnlyList<FieldMonster?> MonsterZones { get; }
    public IReadOnlyList<CardInstance?> SpellTrapZones { get; }
    public CardInstance? FieldZone => null;
    public IReadOnlyList<CardInstance> Graveyard { get; }

    internal DuelPlayerState(PlayerId id, IEnumerable<CardInstance> deck, IEnumerable<CardInstance> hand,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null)
    {
        Id = id;
        Deck = Array.AsReadOnly(deck.ToArray());
        Hand = Array.AsReadOnly(hand.ToArray());
        MonsterZones = Array.AsReadOnly(monsters?.ToArray() ?? new FieldMonster?[5]);
        SpellTrapZones = Array.AsReadOnly(new CardInstance?[5]);
        Graveyard = Array.AsReadOnly(graveyard?.ToArray() ?? []);
    }

    internal DuelPlayerState With(IEnumerable<CardInstance>? deck = null, IEnumerable<CardInstance>? hand = null,
        IEnumerable<FieldMonster?>? monsters = null, IEnumerable<CardInstance>? graveyard = null) =>
        new(Id, deck ?? Deck, hand ?? Hand, monsters ?? MonsterZones, graveyard ?? Graveyard);
}

public sealed class DuelState
{
    public IReadOnlyList<DuelPlayerState> Players { get; }
    public PlayerId ActivePlayer { get; }
    public int TurnNumber { get; }
    public DuelPhase Phase { get; }
    public bool NormalSummonUsed { get; }
    public DuelOutcome? Outcome { get; }

    internal DuelState(IEnumerable<DuelPlayerState> players, PlayerId activePlayer, int turnNumber = 1,
        DuelPhase phase = DuelPhase.Draw, bool normalSummonUsed = false, DuelOutcome? outcome = null)
    {
        Players = Array.AsReadOnly(players.ToArray());
        ActivePlayer = activePlayer;
        TurnNumber = turnNumber;
        Phase = phase;
        NormalSummonUsed = normalSummonUsed;
        Outcome = outcome;
    }

    public DuelPlayerState GetPlayer(PlayerId player) => player is PlayerId.One or PlayerId.Two
        ? Players[(int)player] : throw new ArgumentOutOfRangeException(nameof(player));

    internal DuelState With(DuelPlayerState? player = null, PlayerId? activePlayer = null,
        int? turnNumber = null, DuelPhase? phase = null, bool? normalSummonUsed = null, DuelOutcome? outcome = null)
    {
        var players = Players.ToArray();
        if (player is not null) players[(int)player.Id] = player;
        return new(players, activePlayer ?? ActivePlayer, turnNumber ?? TurnNumber, phase ?? Phase,
            normalSummonUsed ?? NormalSummonUsed, outcome ?? Outcome);
    }
}

public abstract record DuelCommand(PlayerId Player);
public sealed record ResolveDrawStep(PlayerId Player) : DuelCommand(Player);
public sealed record ChangePhase(PlayerId Player, DuelPhase Target) : DuelCommand(Player);
public sealed record NormalSummon(PlayerId Player, int CardInstanceId, int MonsterZone) : DuelCommand(Player);
public sealed record DiscardForHandLimit(PlayerId Player, int CardInstanceId) : DuelCommand(Player);
public sealed record EndTurn(PlayerId Player) : DuelCommand(Player);

public enum DuelError
{
    None, DuelFinished, InvalidPlayer, NotActivePlayer, WrongPhase, InvalidPhaseTransition,
    FirstTurnBattleNotAllowed, CardNotInHand, UnknownCardRules, NotMonster, NormalSummonNotAllowed,
    UnsupportedMonsterLevel, TributeSummonNotSupported, InvalidMonsterZone, MonsterZoneOccupied,
    NormalSummonLimitReached, HandLimitExceeded, DiscardNotRequired, UnsupportedCommand
}

public abstract record DuelEvent;
public sealed record CardDrawn(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record FirstDrawSkipped(PlayerId Player) : DuelEvent;
public sealed record PhaseChanged(DuelPhase From, DuelPhase To) : DuelEvent;
public sealed record MonsterNormalSummoned(PlayerId Player, FieldMonster Monster, int Zone) : DuelEvent;
public sealed record CardDiscarded(PlayerId Player, CardInstance Card) : DuelEvent;
public sealed record TurnStarted(PlayerId Player, int TurnNumber) : DuelEvent;
public sealed record DuelEnded(DuelOutcome Outcome) : DuelEvent;
public sealed record DuelActionResult(bool Succeeded, DuelError Error, DuelState State,
    IReadOnlyList<DuelEvent> Events);
