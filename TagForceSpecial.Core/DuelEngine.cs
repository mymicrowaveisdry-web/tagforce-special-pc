using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Core;

/// <summary>
/// Deterministic two-player rules subset: opening hands, draws, phases, basic normal summons,
/// sets, position changes, effect-free battles, hand-limit discards and defeat.
/// No RNG, chains, effects, attack replays or AI. Battles resolve in one command.
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
                (profile.AllowsNormalSummon || profile.AllowsBasicBattle) && profile.Category != CardCategory.Monster ||
                profile.AllowsSpellTrapSet && profile.Category is not (CardCategory.Spell or CardCategory.Trap))
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
            NormalSummon summon => Summon(summon.CardInstanceId, summon.MonsterZone, false),
            SetMonster set => Summon(set.CardInstanceId, set.MonsterZone, true),
            SetSpellTrap set => SetBackRow(set.CardInstanceId, set.SpellTrapZone),
            ChangeMonsterPosition change => ChangePosition(change.MonsterZone, change.Target),
            DeclareAttack attack => Attack(attack.AttackerZone, attack.TargetZone),
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

    private DuelActionResult Summon(int instanceId, int zone, bool faceDown)
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
        var monster = new FieldMonster(card, faceDown ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack,
            SummonedOnTurn: State.TurnNumber);
        var monsters = player.MonsterZones.ToArray();
        monsters[zone] = monster;
        var next = player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId), monsters: monsters);
        return Accept(State.With(player: next, normalSummonUsed: true), faceDown
            ? new MonsterSet(player.Id, monster, zone) : new MonsterNormalSummoned(player.Id, monster, zone));
    }

    private DuelActionResult SetBackRow(int instanceId, int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category is not (CardCategory.Spell or CardCategory.Trap)) return Reject(DuelError.NotSpellOrTrap);
        if (!rule.AllowsSpellTrapSet) return Reject(DuelError.SpellTrapSetNotAllowed);
        if (player.SpellTrapZones[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        var zones = player.SpellTrapZones.ToArray();
        zones[zone] = card;
        return Accept(State.With(player: player.With(hand: player.Hand.Where(c => c.InstanceId != instanceId),
            spellTraps: zones)), new SpellTrapSet(player.Id, card, zone));
    }

    private DuelActionResult ChangePosition(int zone, BattlePosition target)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer);
        var monster = player.MonsterZones[zone];
        if (monster is null) return Reject(DuelError.MonsterZoneEmpty);
        if (target is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense) || target == monster.Position ||
            monster.Position == BattlePosition.FaceDownDefense && target != BattlePosition.FaceUpAttack)
            return Reject(DuelError.InvalidBattlePosition);
        if (monster.SummonedOnTurn == State.TurnNumber || monster.PositionChangedOnTurn == State.TurnNumber ||
            monster.LastAttackTurn == State.TurnNumber) return Reject(DuelError.PositionChangeNotAllowed);
        // Flip effects and persistent effects that restrict position changes are not implemented.
        if (!rules[monster.Card.GameCardId].AllowsBasicBattle) return Reject(DuelError.UnsupportedBattleCard);
        bool flip = monster.Position == BattlePosition.FaceDownDefense;
        var changed = monster with { Position = target, PositionChangedOnTurn = State.TurnNumber,
            SummonedOnTurn = flip ? State.TurnNumber : monster.SummonedOnTurn };
        var zones = player.MonsterZones.ToArray();
        zones[zone] = changed;
        return Accept(State.With(player: player.With(monsters: zones)), flip
            ? new MonsterFlipSummoned(player.Id, changed, zone)
            : new MonsterPositionChanged(player.Id, zone, monster.Position, target));
    }

    private DuelActionResult Attack(int attackerZone, int? targetZone)
    {
        if (State.Phase != DuelPhase.Battle) return Reject(DuelError.WrongPhase);
        if (State.TurnNumber == 1) return Reject(DuelError.FirstTurnBattleNotAllowed);
        if (attackerZone is < 0 or >= 5 || targetZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var attackerPlayer = State.GetPlayer(State.ActivePlayer);
        var defenderPlayer = State.GetPlayer(Other(State.ActivePlayer));
        var attacker = attackerPlayer.MonsterZones[attackerZone];
        if (attacker is null) return Reject(DuelError.MonsterZoneEmpty);
        if (attacker.Position != BattlePosition.FaceUpAttack) return Reject(DuelError.AttackerNotInAttackPosition);
        if (attacker.LastAttackTurn == State.TurnNumber) return Reject(DuelError.AlreadyAttacked);
        FieldMonster? defender = targetZone is int zone ? defenderPlayer.MonsterZones[zone] : null;
        if (targetZone is not null && defender is null) return Reject(DuelError.MonsterZoneEmpty);
        if (targetZone is null && defenderPlayer.MonsterZones.Any(m => m is not null))
            return Reject(DuelError.DirectAttackBlocked);
        if (!rules[attacker.Card.GameCardId].AllowsBasicBattle ||
            defender is not null && !rules[defender.Card.GameCardId].AllowsBasicBattle)
            return Reject(DuelError.UnsupportedBattleCard);
        var attackStats = catalog[attacker.Card.GameCardId].Statistics;
        var defenseStats = defender is null ? null : catalog[defender.Card.GameCardId].Statistics;
        int? defendingValue = defender?.Position == BattlePosition.FaceUpAttack ? defenseStats?.Attack : defenseStats?.Defense;
        if (attackStats.Attack is not int attack || defender is not null && defendingValue is null)
            return Reject(DuelError.UnsupportedBattleStatistics);

        var events = new List<DuelEvent> { new AttackDeclared(attackerPlayer.Id, attackerZone, targetZone) };
        var attackZones = attackerPlayer.MonsterZones.ToArray();
        var defenseZones = defenderPlayer.MonsterZones.ToArray();
        attackZones[attackerZone] = attacker with { LastAttackTurn = State.TurnNumber };
        if (defender?.Position == BattlePosition.FaceDownDefense)
        {
            defenseZones[targetZone!.Value] = defender with { Position = BattlePosition.FaceUpDefense };
            events.Add(new MonsterRevealed(defenderPlayer.Id, targetZone.Value, defender.Card));
        }
        bool destroyAttacker = false, destroyDefender = false;
        int damage = 0;
        PlayerId damagedPlayer = defenderPlayer.Id;
        if (defender is null) damage = attack;
        else
        {
            int difference = attack - defendingValue!.Value;
            if (defender.Position == BattlePosition.FaceUpAttack)
            {
                destroyAttacker = difference < 0 || difference == 0 && attack > 0;
                destroyDefender = difference > 0 || difference == 0 && attack > 0;
                damage = Math.Abs(difference);
            }
            else
            {
                destroyDefender = difference > 0;
                damage = Math.Max(0, -difference); // No piercing damage in this subset.
            }
            if (difference < 0) damagedPlayer = attackerPlayer.Id;
        }
        attackerPlayer = attackerPlayer.With(monsters: attackZones);
        defenderPlayer = defenderPlayer.With(monsters: defenseZones);
        DuelState next = State.With(player: attackerPlayer).With(player: defenderPlayer);
        if (damage > 0)
        {
            var damaged = next.GetPlayer(damagedPlayer);
            int remaining = Math.Max(0, damaged.LifePoints - damage);
            next = next.With(player: damaged.With(lifePoints: remaining));
            events.Add(new BattleDamageDealt(damagedPlayer, damage, damaged.LifePoints, remaining));
            // LP defeat occurs during damage calculation, before sending battle-destroyed cards to the graveyard.
            if (remaining == 0)
            {
                var outcome = new DuelOutcome(Other(damagedPlayer), damagedPlayer, DuelEndReason.LifePointsDepleted);
                events.Add(new DuelEnded(outcome));
                return Accept(next.With(outcome: outcome), events.ToArray());
            }
        }
        void Destroy(PlayerId playerId, int zone)
        {
            var player = next.GetPlayer(playerId);
            var zones = player.MonsterZones.ToArray();
            var card = zones[zone]!.Card;
            zones[zone] = null;
            next = next.With(player: player.With(monsters: zones, graveyard: player.Graveyard.Append(card)));
            events.Add(new MonsterDestroyed(playerId, zone, card));
        }
        if (destroyAttacker) Destroy(attackerPlayer.Id, attackerZone);
        if (destroyDefender) Destroy(defenderPlayer.Id, targetZone!.Value);
        return Accept(next, events.ToArray());
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
