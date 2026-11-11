namespace TagForceSpecial.Core;

/// <summary>Rolls a six-sided die; later steps read it through <see cref="EffectCount.DieRoll"/> or <see cref="StopUnlessRollStep"/>.</summary>
public sealed record RollDieStep : EffectStep;

/// <summary>Tosses <paramref name="Count"/> coins; later steps read the Heads through <see cref="EffectCount.Heads"/> or <see cref="StopUnlessHeadsStep"/>.</summary>
public sealed record TossCoinsStep(int Count = 1) : EffectStep;

/// <summary>Ends the effect unless the last die roll is one of the listed values.</summary>
public sealed record StopUnlessRollStep : EffectStep
{
    public IReadOnlyList<int> Values { get; }
    public StopUnlessRollStep(params int[] values) => Values = Array.AsReadOnly(values ?? throw new ArgumentNullException(nameof(values)));
}

/// <summary>Ends the effect unless the last coin toss gave at least <paramref name="Minimum"/> Heads.</summary>
public sealed record StopUnlessHeadsStep(int Minimum) : EffectStep;

/// <summary>Every face-up monster on <paramref name="Side"/> gains these stats until the End Phase, times <paramref name="PerCount"/> when set.</summary>
public sealed record ModifyAllUntilEndStep(TargetSide Side, int Attack, int Defense, EffectCount PerCount = EffectCount.None) : EffectStep;

/// <summary>The activating player looks at the opponent's hand, and at their Set cards when <paramref name="IncludeSetCards"/>.</summary>
public sealed record LookAtOpponentHandStep(bool IncludeSetCards = false) : EffectStep;

public enum HandMove { Discard, ShuffleIntoDeck }

/// <summary>The activating player chooses a matching card in the opponent's hand and moves it; nothing happens if none matches.</summary>
public sealed record ChooseFromOpponentHandStep(CardFilter? Filter, HandMove To) : EffectStep;

/// <summary>Each player tosses a coin: Heads gains <paramref name="HeadsGain"/> LP, Tails takes <paramref name="TailsDamage"/> damage, at the same time.</summary>
public sealed record EachPlayerTossesCoinStep(int HeadsGain, int TailsDamage) : EffectStep;

/// <summary>
/// Both players roll until the results differ; the lower roller takes the higher roll times <paramref name="PerPip"/>,
/// or <paramref name="OnSix"/> when the higher roll is a 6.
/// </summary>
public sealed record DiceDuelStep(int PerPip, int OnSix) : EffectStep;

/// <summary>
/// The opponent calls the card type of a random card in the activating player's hand; the effect ends unless the call is wrong.
/// As a trigger's first step it needs a card in the hand to activate; with none left at resolution, the effect ends.
/// </summary>
public sealed record OpponentCallsRandomHandCardStep : EffectStep;

/// <summary>Returns the monster whose attack is in progress to its owner's hand.</summary>
public sealed record ReturnAttackerToHandStep : EffectStep;

/// <summary>Ends the effect unless the hand on <paramref name="Side"/> holds a matching card. As a trigger's first step it is also needed to activate.</summary>
public sealed record StopUnlessHandHoldsStep(TargetSide Side, CardFilter Filter) : EffectStep;

/// <summary>Reveals the top <paramref name="Count"/> cards of the activating player's Deck; they stay there for <see cref="TakeExcavatedStep"/>.</summary>
public sealed record ExcavateStep(int Count) : EffectStep;

/// <summary>The activating player adds one excavated card still in the Deck, matching <paramref name="Filter"/> when set, to the hand.</summary>
public sealed record TakeExcavatedStep(CardFilter? Filter = null) : EffectStep;

/// <summary>
/// Ends the effect unless the card the effect last sent from the top of the Deck matches <paramref name="Filter"/>;
/// with <paramref name="DestroySourceOtherwise"/>, the source in the hand is destroyed instead.
/// </summary>
public sealed record StopUnlessMilledStep(CardFilter Filter, bool DestroySourceOtherwise = false) : EffectStep;

/// <summary>Reveal this card in the hand; it stays there.</summary>
public sealed record RevealSelfCost : EffectCost;

/// <summary>The opponent holds at least <paramref name="Count"/> cards.</summary>
public sealed record OpponentHandAtLeastCondition(int Count) : EffectCondition;

/// <summary>The activating player has not Special Summoned this turn.</summary>
public sealed record NoSpecialSummonThisTurnCondition : EffectCondition;

public sealed record DieRolled(PlayerId Player, int Result, string EffectId) : DuelEvent;
public sealed record CoinsTossed(PlayerId Player, IReadOnlyList<bool> Heads, string EffectId) : DuelEvent;

/// <summary>Cards a player looked at; only <see cref="Viewer"/> learns which they are.</summary>
public sealed record CardsLookedAt(PlayerId Viewer, PlayerId Owner, IReadOnlyList<CardInstance> Cards, string EffectId) : DuelEvent;

/// <summary>Cards shown to both players.</summary>
public sealed record CardsRevealed(PlayerId Owner, IReadOnlyList<CardInstance> Cards, string EffectId) : DuelEvent;

/// <summary>A card in the hand was destroyed and sent to the Graveyard.</summary>
public sealed record CardDestroyedInHand(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;

public sealed record CardTypeCalled(PlayerId Caller, CardInstance Card, CardCategory Called, bool Right, string EffectId) : DuelEvent;

/// <summary>A player calls the type of a card they cannot see.</summary>
public sealed class PendingTypeCall : PendingDefinitionChoice
{
    public IReadOnlyList<CardCategory> Options { get; }

    internal PendingTypeCall(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex)
        : base(decisionId, player, effectId, linkNumber, stepIndex) =>
        Options = Array.AsReadOnly(new[] { CardCategory.Monster, CardCategory.Spell, CardCategory.Trap });
}
public sealed record CallCardType(PlayerId Player, long DecisionId, CardCategory Category) : DuelCommand(Player);
public sealed record TypeCallRequested(PendingTypeCall Decision) : DuelEvent;

/// <summary>The latest die roll, coin toss, random pick, milled card and excavation, read by later steps of the same effect.</summary>
internal sealed record RandomResult(int Die, int Heads)
{
    public CardInstance? Picked { get; init; }
    public CardInstance? Milled { get; init; }
    public int MilledLevel { get; init; }
    public IReadOnlyList<CardInstance> Excavated { get; init; } = [];
    /// <summary>The monster the activating player chose to give in a swap, while the opponent chooses theirs.</summary>
    public CardInstance? SwapFirst { get; init; }
}

public sealed partial class DuelEngine
{
    private static RandomResult Results(DuelState state) => state.Bookkeeping.LastRandom ?? new(0, 0);

    private static DuelState WithResults(DuelState state, RandomResult results, DuelRandomState? random = null) =>
        state.WithBookkeeping(state.Bookkeeping with { Random = random ?? state.Bookkeeping.Random, LastRandom = results });

    private static DuelRandomState RandomOf(DuelState state) => state.Random ?? throw new InvalidOperationException("Random effects require a seeded engine.");

    private static DuelState RollDie(DuelState state, PlayerId player, string effectId, List<DuelEvent> events)
    {
        var (value, next) = RandomOf(state).NextBelow(6);
        int result = value + 1;
        events.Add(new DieRolled(player, result, effectId));
        return WithResults(state, Results(state) with { Die = result }, next);
    }

    private static DuelState TossCoins(DuelState state, PlayerId player, int count, string effectId, List<DuelEvent> events)
    {
        var random = RandomOf(state);
        var heads = new bool[count];
        for (int i = 0; i < count; i++)
        {
            (int value, random) = random.NextBelow(2);
            heads[i] = value == 0;
        }
        events.Add(new CoinsTossed(player, Array.AsReadOnly(heads), effectId));
        return WithResults(state, Results(state) with { Heads = heads.Count(h => h) }, random);
    }

    private DuelState ModifyAllUntilEnd(DuelState state, PlayerId controller, ModifyAllUntilEndStep step, string effectId, List<DuelEvent> events)
    {
        int factor = CountFor(state, controller, step.PerCount);
        if (factor == 0) return state;
        var stats = new ModifyTargetsUntilEndStep((int)Math.Clamp((long)step.Attack * factor, int.MinValue, int.MaxValue),
            (int)Math.Clamp((long)step.Defense * factor, int.MinValue, int.MaxValue));
        foreach (var (binding, monster) in FieldMonsters(state).ToArray())
            if (monster.Position != BattlePosition.FaceDownDefense &&
                (step.Side == TargetSide.Either || (step.Side == TargetSide.Own) == (binding.Player == controller)))
                state = ModifyUntilEnd(state, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, monster.Card, monster.PlacementId), stats, effectId, events);
        return state;
    }

    private static DuelState LookAtOpponentHand(DuelState state, PlayerId viewer, bool includeSetCards, string effectId, List<DuelEvent> events)
    {
        var opponent = state.GetPlayer(Other(viewer));
        IEnumerable<CardInstance> cards = opponent.Hand;
        if (includeSetCards)
            cards = cards.Concat(opponent.MonsterZones.Where(m => m is { Position: BattlePosition.FaceDownDefense }).Select(m => m!.Card))
                .Concat(opponent.SpellTrapFields.Where(f => f is { IsFaceUp: false }).Select(f => f!.Card))
                .Concat(opponent.FieldSpell is { IsFaceUp: false } field ? [field.Card] : []);
        events.Add(new CardsLookedAt(viewer, opponent.Id, Array.AsReadOnly(cards.ToArray()), effectId));
        return state;
    }

    private DuelState MoveFromOpponentHand(DuelState state, PlayerId controller, CardInstance card, HandMove move, string effectId, List<DuelEvent> events)
    {
        var opponent = state.GetPlayer(Other(controller));
        if (!opponent.Hand.Contains(card)) return state;
        if (move == HandMove.Discard)
        {
            events.Add(new CardDiscarded(opponent.Id, card));
            return state.With(player: opponent.With(hand: opponent.Hand.Where(c => c != card), graveyard: opponent.Graveyard.Append(card)));
        }
        events.Add(new CardShuffledIntoDeck(opponent.Id, card, effectId));
        state = state.With(player: opponent.With(hand: opponent.Hand.Where(c => c != card), deck: opponent.Deck.Append(card)));
        return ShuffleDeck(state, opponent.Id, effectId, events);
    }

    /// <summary>Gains go first; damage to both players lands together, so both reaching 0 is a draw.</summary>
    private DuelState EachPlayerTosses(DuelState state, PlayerId controller, EachPlayerTossesCoinStep step, string effectId, List<DuelEvent> events)
    {
        var random = RandomOf(state);
        Dictionary<PlayerId, bool> heads = [];
        foreach (var player in new[] { controller, Other(controller) })
        {
            (int value, random) = random.NextBelow(2);
            heads[player] = value == 0;
            events.Add(new CoinsTossed(player, Array.AsReadOnly(new[] { heads[player] }), effectId));
        }
        state = WithResults(state, Results(state), random);
        foreach (var (player, won) in heads)
            if (won && step.HeadsGain > 0) state = GainLifePoints(state, player, step.HeadsGain, effectId, events);
        if (step.TailsDamage == 0) return state;
        return (heads[controller], heads[Other(controller)]) switch
        {
            (false, false) => DamageBothPlayers(state, controller, new DamageBothPlayersStep(step.TailsDamage, step.TailsDamage), effectId, events),
            (false, true) => DealEffectDamage(state, controller, step.TailsDamage, effectId, events),
            (true, false) => DealEffectDamage(state, Other(controller), step.TailsDamage, effectId, events),
            _ => state
        };
    }

    private DuelState DiceDuel(DuelState state, PlayerId controller, DiceDuelStep step, string effectId, List<DuelEvent> events)
    {
        var random = RandomOf(state);
        int own, theirs;
        do
        {
            (own, random) = random.NextBelow(6);
            (theirs, random) = random.NextBelow(6);
            events.Add(new DieRolled(controller, ++own, effectId));
            events.Add(new DieRolled(Other(controller), ++theirs, effectId));
        } while (own == theirs);
        state = WithResults(state, Results(state), random);
        int high = Math.Max(own, theirs);
        return DealEffectDamage(state, own < theirs ? controller : Other(controller), high == 6 ? step.OnSix : high * step.PerPip, effectId, events);
    }

    /// <summary>Sends the top cards to the Graveyard and remembers the last one for <see cref="StopUnlessMilledStep"/>.</summary>
    private DuelState MillAndRecord(DuelState state, PlayerId controller, int count, string effectId, List<DuelEvent> events)
    {
        var milled = state.GetPlayer(controller).Deck.Take(count).LastOrDefault();
        state = MillTop(state, controller, count, effectId, events);
        int level = milled is not null && rules.TryGetValue(milled.GameCardId, out var rule) && rule.Category == CardCategory.Monster && !IsXyzCard(milled)
            ? (int)catalog[milled.GameCardId].Statistics.Level : 0;
        return WithResults(state, Results(state) with { Milled = milled, MilledLevel = level });
    }

    private static DuelState Excavate(DuelState state, PlayerId controller, int count, string effectId, List<DuelEvent> events)
    {
        var cards = state.GetPlayer(controller).Deck.Take(count).ToArray();
        events.Add(new CardsRevealed(controller, Array.AsReadOnly(cards), effectId));
        return WithResults(state, Results(state) with { Excavated = Array.AsReadOnly(cards) });
    }

    private bool HandHolds(DuelState state, PlayerId controller, StopUnlessHandHoldsStep step) =>
        (step.Side == TargetSide.Either ? state.Players.SelectMany(p => p.Hand)
            : state.GetPlayer(step.Side == TargetSide.Own ? controller : Other(controller)).Hand).Any(c => MatchesFilter(c, step.Filter));

    private static DuelState DestroyInHand(DuelState state, PlayerId playerId, CardInstance card, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        if (!player.Hand.Contains(card)) return state;
        events.Add(new CardDestroyedInHand(playerId, card, effectId));
        return state.With(player: player.With(hand: player.Hand.Where(c => c != card), graveyard: player.Graveyard.Append(card)));
    }

    private static DuelState TakeFromDeck(DuelState state, PlayerId playerId, CardInstance card, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        events.Add(new CardSearched(playerId, card, effectId));
        return state.With(player: player.With(deck: player.Deck.Where(c => c != card), hand: player.Hand.Append(card)));
    }

    private DuelState ReturnAttacker(DuelState state, ChainLink link, string effectId, List<DuelEvent> events)
    {
        if (state.Battle?.Attack is not { } attack || !attack.Attacker.IsStillPresent(state))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return state;
        }
        var attacker = attack.Attacker;
        return ReturnFieldCardToHand(state, new(attacker.Player, FieldLocation.MonsterZone, attacker.Zone, attacker.Card, attacker.PlacementId), effectId, events);
    }

    /// <summary>Picks the random card and asks the opponent to call; on the answer, ends the effect unless the call was wrong.</summary>
    private (DuelState State, PendingDefinitionChoice? Pause, bool Stop) CallRandomHandCard(DuelState state, ChainLink link, EffectDefinition definition, int stepIndex,
        DuelCommand? choice, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        if (choice is not CallCardType call)
        {
            var hand = state.GetPlayer(controller).Hand;
            if (hand.Count == 0)
            {
                events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
                return (state, null, true);
            }
            var (index, random) = RandomOf(state).NextBelow(hand.Count);
            state = WithResults(state, Results(state) with { Picked = hand[index] }, random);
            return (state, new PendingTypeCall(checked(State.Revision + 1), Other(controller), definition.EffectId, link.Number, stepIndex), false);
        }
        var picked = Results(state).Picked!;
        bool right = rules.TryGetValue(picked.GameCardId, out var rule) && rule.Category == call.Category;
        events.Add(new CardsRevealed(controller, Array.AsReadOnly(new[] { picked }), definition.EffectId));
        events.Add(new CardTypeCalled(call.Player, picked, call.Category, right, definition.EffectId));
        if (right) events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.NoMatchingCard));
        return (state, null, right);
    }

    private DuelActionResult AnswerTypeCall(PendingChain chain, PendingTypeCall pending, CallCardType command)
    {
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!pending.Options.Contains(command.Category)) return Reject(DuelError.InvalidDecisionChoice);
        return ResolveChainLink(chain, definitionChoice: command);
    }

    /// <summary>True when a face-up effect makes this player keep their hand revealed.</summary>
    public bool HandRevealed(PlayerId player) => RuleLocked(State, LockedAction.HideHand, player, null);
}
