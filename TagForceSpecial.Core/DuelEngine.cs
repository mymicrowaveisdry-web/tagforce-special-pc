using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Core;

/// <summary>
/// Deterministic two-player rules subset: opening hands, draws, phases, basic normal summons,
/// hand-limit discards and deck-out. No RNG, battle resolution, chains, effects or AI.
/// </summary>
public sealed class DuelEngine
{
    private readonly CardCatalog catalog;
    private readonly FrozenDictionary<uint, DuelCardRules> rules;
    public DuelState State { get; private set; }

    /// <summary>
    /// Decks are supplied top-first, after any external shuffle. Only size and catalog membership
    /// are checked here; banlists, copy limits, extra decks and complete deck legality are later work.
    /// Rule metadata is explicit; missing classifications fail closed when attempting to summon.
    /// </summary>
    public DuelEngine(CardCatalog catalog, IReadOnlyDictionary<uint, DuelCardRules> rules,
        IEnumerable<uint> playerOneDeck, IEnumerable<uint> playerTwoDeck, PlayerId startingPlayer = PlayerId.One)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(playerOneDeck);
        ArgumentNullException.ThrowIfNull(playerTwoDeck);
        if (startingPlayer is not (PlayerId.One or PlayerId.Two))
            throw new ArgumentOutOfRangeException(nameof(startingPlayer));
        this.catalog = catalog;
        var knownIds = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        foreach (var (id, profile) in rules)
            if (!knownIds.Contains(id) || profile is null || !Enum.IsDefined(profile.Category) ||
                profile.AllowsNormalSummon && profile.Category != CardCategory.Monster)
                throw new ArgumentException("Invalid explicit card rules.", nameof(rules));
        this.rules = rules.ToFrozenDictionary();
        int nextInstance = 1;
        DuelPlayerState CreatePlayer(PlayerId id, IEnumerable<uint> ids)
        {
            // Limit enumeration too: malformed/infinite sequences cannot allocate indefinitely.
            var deck = ids.Take(61).ToArray();
            if (deck.Length is < 40 or > 60 || deck.Any(cardId => !knownIds.Contains(cardId)))
                throw new ArgumentException("Each main deck needs 40–60 catalog IDs.");
            var instances = deck.Select(cardId => new CardInstance(nextInstance++, cardId, id)).ToArray();
            return new(id, instances.Skip(5), instances.Take(5));
        }
        State = new([CreatePlayer(PlayerId.One, playerOneDeck), CreatePlayer(PlayerId.Two, playerTwoDeck)], startingPlayer);
    }

    public DuelActionResult Execute(DuelCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (State.Outcome is not null) return Reject(DuelError.DuelFinished);
        if (command.Player is not (PlayerId.One or PlayerId.Two)) return Reject(DuelError.InvalidPlayer);
        if (command.Player != State.ActivePlayer) return Reject(DuelError.NotActivePlayer);
        return command switch
        {
            ResolveDrawStep => Draw(),
            ChangePhase phase => Advance(phase.Target),
            NormalSummon summon => Summon(summon.CardInstanceId, summon.MonsterZone),
            DiscardForHandLimit discard => Discard(discard.CardInstanceId),
            EndTurn => FinishTurn(),
            _ => Reject(DuelError.UnsupportedCommand)
        };
    }

    private DuelActionResult Draw()
    {
        if (State.Phase != DuelPhase.Draw) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(State.ActivePlayer);
        if (State.TurnNumber == 1)
            return Accept(State.With(phase: DuelPhase.Standby), new FirstDrawSkipped(player.Id),
                new PhaseChanged(DuelPhase.Draw, DuelPhase.Standby));
        if (player.Deck.Count == 0)
        {
            var outcome = new DuelOutcome(Other(player.Id), player.Id, DuelEndReason.UnableToDraw);
            return Accept(State.With(outcome: outcome), new DuelEnded(outcome));
        }
        var card = player.Deck[0];
        var next = player.With(deck: player.Deck.Skip(1), hand: player.Hand.Append(card));
        return Accept(State.With(player: next, phase: DuelPhase.Standby), new CardDrawn(player.Id, card),
            new PhaseChanged(DuelPhase.Draw, DuelPhase.Standby));
    }

    private DuelActionResult Advance(DuelPhase target)
    {
        if (State.Phase == DuelPhase.Main1 && target == DuelPhase.Battle && State.TurnNumber == 1)
            return Reject(DuelError.FirstTurnBattleNotAllowed);
        bool valid = (State.Phase, target) is
            (DuelPhase.Standby, DuelPhase.Main1) or
            (DuelPhase.Main1, DuelPhase.Battle) or (DuelPhase.Main1, DuelPhase.End) or
            (DuelPhase.Battle, DuelPhase.Main2) or (DuelPhase.Main2, DuelPhase.End);
        if (!valid) return Reject(DuelError.InvalidPhaseTransition);
        return Accept(State.With(phase: target), new PhaseChanged(State.Phase, target));
    }

    private DuelActionResult Summon(int instanceId, int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category != CardCategory.Monster) return Reject(DuelError.NotMonster);
        if (!rule.AllowsNormalSummon) return Reject(DuelError.NormalSummonNotAllowed);
        uint level = catalog[card.GameCardId].Statistics.Level;
        if (level == 0) return Reject(DuelError.UnsupportedMonsterLevel);
        if (level > 4) return Reject(DuelError.TributeSummonNotSupported);
        if (player.MonsterZones[zone] is not null) return Reject(DuelError.MonsterZoneOccupied);
        if (State.NormalSummonUsed) return Reject(DuelError.NormalSummonLimitReached);
        var monster = new FieldMonster(card, BattlePosition.FaceUpAttack);
        var monsters = player.MonsterZones.ToArray();
        monsters[zone] = monster;
        var next = player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), monsters: monsters);
        return Accept(State.With(player: next, normalSummonUsed: true), new MonsterNormalSummoned(player.Id, monster, zone));
    }

    private DuelActionResult Discard(int instanceId)
    {
        if (State.Phase != DuelPhase.End) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(State.ActivePlayer);
        if (player.Hand.Count <= 6) return Reject(DuelError.DiscardNotRequired);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        var next = player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), graveyard: player.Graveyard.Append(card));
        return Accept(State.With(player: next), new CardDiscarded(player.Id, card));
    }

    private DuelActionResult FinishTurn()
    {
        if (State.Phase != DuelPhase.End) return Reject(DuelError.WrongPhase);
        if (State.GetPlayer(State.ActivePlayer).Hand.Count > 6) return Reject(DuelError.HandLimitExceeded);
        var player = Other(State.ActivePlayer);
        var next = State.With(activePlayer: player, turnNumber: State.TurnNumber + 1,
            phase: DuelPhase.Draw, normalSummonUsed: false);
        return Accept(next, new TurnStarted(player, next.TurnNumber), new PhaseChanged(DuelPhase.End, DuelPhase.Draw));
    }

    private DuelActionResult Accept(DuelState state, params DuelEvent[] events)
    {
        State = state;
        return new(true, DuelError.None, state, Array.AsReadOnly(events));
    }

    private DuelActionResult Reject(DuelError error) => new(false, error, State, Array.Empty<DuelEvent>());
    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;
}
