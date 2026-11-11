namespace TagForceSpecial.Core;

/// <summary>What an effect declares when it is activated.</summary>
public enum DeclarationKind
{
    /// <summary>Any card name; the value is a card ID, and every card with that name matches.</summary>
    CardName,
    /// <summary>A Monster Card name.</summary>
    MonsterCardName,
    /// <summary>A monster Type; the value is its raw Type code.</summary>
    MonsterType,
    /// <summary>Monster, Spell or Trap; the value is a <see cref="CardCategory"/>.</summary>
    CardType,
    /// <summary>A monster Attribute; the value is its raw Attribute code.</summary>
    Attribute,
    /// <summary>A Pendulum Scale from 1 to 10.</summary>
    PendulumScale,
    /// <summary>A monster Type that a face-up monster on the field currently has (Tribe-Infecting Virus ruling).</summary>
    FaceUpMonsterType
}

/// <summary>
/// Monsters that share <paramref name="Trait"/> with the other targets chosen for the same target slot, for "declare 1 Attribute and target
/// monsters with that Attribute": the declaration can then only be the targets' shared value.
/// </summary>
public sealed record SharedTraitFilter(IdentityTrait Trait) : CardFilter;

/// <summary>Banishes the monster a trigger's event was about, if it is still in place.</summary>
public sealed record BanishEventMonsterStep : EffectStep;

/// <summary>Banishes the face-up Spell or Trap whose trigger resolves, if it is still in place.</summary>
public sealed record BanishSourceCardStep : EffectStep;

/// <summary>Matches cards fitting the effect's declaration. Before anything is declared, any card could match.</summary>
public sealed record DeclaredFilter : CardFilter;

/// <summary>The opponent chooses a matching card in their Deck and sends it to the Graveyard; nothing happens if none matches.</summary>
public sealed record OpponentSendsFromDeckStep(CardFilter Filter) : EffectStep;

/// <summary>The opponent adds a matching card from their Deck to their hand, then shuffles; nothing happens if none matches.</summary>
public sealed record OpponentAddsFromDeckStep(CardFilter Filter) : EffectStep;

/// <summary>Excavated cards still on top of the activating player's Deck go to the Graveyard.</summary>
public sealed record SendExcavatedToGraveyardStep : EffectStep;

/// <summary>
/// The opponent discards every matching card in their hand. With <paramref name="RandomOwnDiscardOtherwise"/>, if none matches,
/// the activating player discards a random card instead.
/// </summary>
public sealed record OpponentDiscardsMatchingStep(CardFilter Filter, bool RandomOwnDiscardOtherwise = false) : EffectStep;

/// <summary>A player declares a card name, Type or card type for an effect being activated.</summary>
public sealed class PendingDeclaration : PendingDefinitionChoice
{
    public DeclarationKind Kind { get; }
    /// <summary>The values that can be declared; empty for card names, where any card in the game can be named.</summary>
    public IReadOnlyList<int> Options { get; }

    internal PendingDeclaration(long decisionId, PlayerId player, string effectId, int linkNumber, DeclarationKind kind, IEnumerable<int> options)
        : base(decisionId, player, effectId, linkNumber, -1) => (Kind, Options) = (kind, Array.AsReadOnly(options.ToArray()));
}

/// <summary>Declares a value for a pending declaration or a trigger being activated.</summary>
public sealed record DeclareValue(PlayerId Player, long DecisionId, int Value) : DuelCommand(Player);
public sealed record DeclarationRequested(PendingDeclaration Decision) : DuelEvent;
public sealed record DeclarationMade(PlayerId Player, DeclarationKind Kind, int Value, string EffectId) : DuelEvent;

public sealed partial class DuelEngine
{
    private int[]? monsterTypeCodes;
    private int[]? monsterAttributeCodes;

    /// <summary>The declaration of the link now resolving, read by <see cref="DeclaredFilter"/>.</summary>
    private (DeclarationKind Kind, int Value)? resolvingDeclaration;

    /// <summary>Values a declaration can take; empty for card names, which accept any non-Token card.</summary>
    public IReadOnlyList<int> DeclarationOptions(DeclarationKind kind) => kind switch
    {
        DeclarationKind.MonsterType => monsterTypeCodes ??= catalog.Cards
            .Where(c => rules.TryGetValue(c.GameCardId, out var rule) && rule.Category == CardCategory.Monster && !IsTokenId(c.GameCardId) && c.Codes is not null)
            .Select(c => (int)c.Codes!.Type).Distinct().Order().ToArray(),
        DeclarationKind.CardType => [(int)CardCategory.Monster, (int)CardCategory.Spell, (int)CardCategory.Trap],
        DeclarationKind.Attribute => monsterAttributeCodes ??= catalog.Cards
            .Where(c => rules.TryGetValue(c.GameCardId, out var rule) && rule.Category == CardCategory.Monster && !IsTokenId(c.GameCardId) && c.Codes is not null)
            .Select(c => (int)c.Codes!.Attribute).Distinct().Order().ToArray(),
        DeclarationKind.PendulumScale => [.. Enumerable.Range(1, 10)],
        DeclarationKind.FaceUpMonsterType => [.. FieldMonsters(State).Where(e => e.Monster.Position != BattlePosition.FaceDownDefense)
            .Select(e => TypeCode(e.Monster.Card)).OfType<uint>().Select(t => (int)t).Distinct().Order()],
        _ => []
    };

    /// <summary>True when <paramref name="value"/> can be declared for <paramref name="kind"/>.</summary>
    public bool DeclarationValid(DeclarationKind kind, int value) => kind switch
    {
        DeclarationKind.CardName or DeclarationKind.MonsterCardName => value > 0 && rules.TryGetValue((uint)value, out var rule) && !IsTokenId((uint)value) &&
            catalog.Cards.Any(c => c.GameCardId == (uint)value) && (kind == DeclarationKind.CardName || rule.Category == CardCategory.Monster),
        _ => DeclarationOptions(kind).Contains(value)
    };

    private bool DeclaredMatches(CardInstance card, DeclarationKind kind, int value) =>
        rules.TryGetValue(card.GameCardId, out var rule) && kind switch
        {
            DeclarationKind.CardName or DeclarationKind.MonsterCardName => catalog[card.GameCardId].Name == catalog[(uint)value].Name,
            DeclarationKind.MonsterType or DeclarationKind.FaceUpMonsterType => rule.Category == CardCategory.Monster && TypeCode(card) == (uint)value,
            DeclarationKind.Attribute => rule.Category == CardCategory.Monster && AttributeCode(card) == (uint)value,
            DeclarationKind.CardType => rule.Category == (CardCategory)value,
            _ => false
        };

    /// <summary>Whether a trigger's event monster matches its subject filter, reading the declaration of the face-up card it comes from.</summary>
    private bool SubjectMatches(TriggerEffectProfile profile, CardInstance subject, FieldSpellTrap? source)
    {
        if (profile.SubjectFilter is not { } filter) return true;
        if (source is null || !effectsByCard.TryGetValue(source.Card.GameCardId, out var effects) || effects.FirstOrDefault(d => d.Declares is not null)?.Declares is not { } kind)
            return MatchesFilter(subject, filter);
        // With nothing declared, nothing matches the declared filter.
        if (source.Declared is not { } value) return false;
        var saved = resolvingDeclaration;
        resolvingDeclaration = (kind, value);
        try { return MatchesFilter(subject, filter); }
        finally { resolvingDeclaration = saved; }
    }

    private DuelState BanishEventMonster(DuelState state, ChainLink link, string effectId, List<DuelEvent> events) =>
        link.Trigger?.Occurrence.Subject is { } subject && subject.IsStillPresent(state)
            ? BanishFieldCard(state, new(subject.Player, FieldLocation.MonsterZone, subject.Zone, subject.Card, subject.PlacementId), effectId, events) : state;

    private DuelState BanishSourceCard(DuelState state, ChainLink link, string effectId, List<DuelEvent> events) =>
        link.Trigger?.Source is { } source && SpellTrapSourcePresent(state, source)
            ? BanishFieldCard(state, new(source.Player, FieldLocation.SpellTrapZone, source.Zone, source.Card, source.PlacementId), effectId, events) : state;

    private uint? TraitCode(CardInstance card, IdentityTrait trait) => trait == IdentityTrait.Attribute ? AttributeCode(card) : TypeCode(card);

    /// <summary>The values this activation can declare: with targets that share a trait, only that trait's value.</summary>
    private IReadOnlyList<int> DeclarationChoices(EffectDefinition definition, CommittedActivation receipt, DeclarationKind kind)
    {
        if (definition.FieldTargets is not [ZoneTarget { Filter: SharedTraitFilter shared }] || receipt.FieldTargets is not [var first, ..] ||
            kind != (shared.Trait == IdentityTrait.Attribute ? DeclarationKind.Attribute : DeclarationKind.MonsterType))
            return DeclarationOptions(kind);
        return TraitCode(first.Card, shared.Trait) is uint code ? [(int)code] : DeclarationOptions(kind);
    }

    /// <summary>
    /// Whether a card can be picked for a shared-trait target slot: it shares the trait with the cards picked so far, and enough other cards
    /// share it to fill the rest of the slot.
    /// </summary>
    private bool SharedTraitPick(SharedTraitFilter shared, FieldCardTarget candidate, IReadOnlyList<FieldCardTarget> matches, IReadOnlyList<CardInstance> picked, int required)
    {
        var code = TraitCode(candidate.Card, shared.Trait);
        if (code is null || picked.Any(p => TraitCode(p, shared.Trait) != code)) return false;
        int partners = matches.Count(m => m.Card != candidate.Card && !picked.Contains(m.Card) && TraitCode(m.Card, shared.Trait) == code);
        return partners >= required - picked.Count - 1;
    }

    /// <summary>Pauses a just-added link for its declaration; responses open once it is made.</summary>
    private static PendingChain AskDeclaration(PendingChain chain, ChainLink link, EffectDefinition definition, PlayerId player, IReadOnlyList<int> options) =>
        new(chain.DecisionId, chain.Links, chain.Responder,
            definitionChoice: new PendingDeclaration(chain.DecisionId, player, definition.EffectId, link.Number, definition.Declares!.Value, options));

    private DuelActionResult AnswerDeclaration(PendingChain chain, PendingDeclaration pending, DeclareValue command)
    {
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (!DeclarationValid(pending.Kind, command.Value) || pending.Options.Count != 0 && !pending.Options.Contains(command.Value))
            return Reject(DuelError.InvalidDecisionChoice);
        var links = chain.Links.Select(l => l.Number == pending.LinkNumber ? l with { Declared = command.Value } : l).ToArray();
        // A declaration made as the link resolves returns to resolution; one made on activation opens responses.
        if (chain.NextLink != 0)
            return Accept(State.WithChain(new PendingChain(checked(State.Revision + 1), links, chain.Responder, chain.ConsecutivePasses, chain.NextLink)),
                new DeclarationMade(command.Player, pending.Kind, command.Value, pending.EffectId));
        var next = new PendingChain(checked(State.Revision + 1), links, chain.Responder);
        return Accept(State.WithChain(next), new DeclarationMade(command.Player, pending.Kind, command.Value, pending.EffectId),
            new ChainResponseRequested(next.Responder, next.DecisionId));
    }

    private DuelState OpponentSendsFromDeck(DuelState state, PlayerId controller, CardInstance card, string effectId, List<DuelEvent> events) =>
        MoveChosenDeckCard(state, Other(controller), card.InstanceId, toHand: false, effectId, events);

    private DuelState OpponentAddsFromDeck(DuelState state, PlayerId controller, OpponentAddsFromDeckStep step, string effectId, List<DuelEvent> events)
    {
        var opponent = Other(controller);
        return state.GetPlayer(opponent).Deck.FirstOrDefault(c => MatchesFilter(c, step.Filter)) is { } card
            ? MoveChosenDeckCard(state, opponent, card.InstanceId, toHand: true, effectId, events) : state;
    }

    private static DuelState SendExcavatedToGraveyard(DuelState state, PlayerId controller, string effectId, List<DuelEvent> events)
    {
        foreach (var card in Results(state).Excavated)
        {
            var player = state.GetPlayer(controller);
            if (!player.Deck.Contains(card)) continue;
            state = state.With(player: player.With(deck: player.Deck.Where(c => c != card)));
            var owner = state.GetPlayer(card.Owner);
            state = state.With(player: owner.With(graveyard: owner.Graveyard.Append(card)));
            events.Add(new CardSentFromDeckToGraveyard(controller, card, effectId));
        }
        return state;
    }

    private DuelState OpponentDiscardsMatching(DuelState state, PlayerId controller, OpponentDiscardsMatchingStep step, string effectId, List<DuelEvent> events)
    {
        var opponent = state.GetPlayer(Other(controller));
        var matching = opponent.Hand.Where(c => MatchesFilter(c, step.Filter)).ToArray();
        if (matching.Length != 0)
        {
            foreach (var card in matching) events.Add(new CardDiscarded(opponent.Id, card));
            return state.With(player: opponent.With(hand: opponent.Hand.Except(matching), graveyard: opponent.Graveyard.Concat(matching)));
        }
        // With no match, the activating player may check the opponent's hand (Mind Crush, OCG ruling).
        events.Add(new CardsLookedAt(controller, opponent.Id, opponent.Hand, effectId));
        var own = state.GetPlayer(controller);
        if (!step.RandomOwnDiscardOtherwise || own.Hand.Count == 0) return state;
        var (index, random) = RandomOf(state).NextBelow(own.Hand.Count);
        var discarded = own.Hand[index];
        events.Add(new CardDiscarded(controller, discarded));
        return WithResults(state.With(player: own.With(hand: own.Hand.Where(c => c != discarded), graveyard: own.Graveyard.Append(discarded))), Results(state), random);
    }

    /// <summary>Every filter a step reads, for checking that declared filters belong to declaring effects.</summary>
    internal static IEnumerable<CardFilter> StepFilters(EffectStep step) => step switch
    {
        DestroyAllStep { Cards: { } cards } => [cards],
        TakeExcavatedStep { Filter: { } filter } => [filter],
        StopUnlessMilledStep milled => [milled.Filter],
        StopUnlessHandHoldsStep holds => [holds.Filter],
        OpponentSendsFromDeckStep send => [send.Filter],
        OpponentAddsFromDeckStep add => [add.Filter],
        OpponentDiscardsMatchingStep discard => [discard.Filter],
        ChooseFromOpponentHandStep { Filter: { } filter } => [filter],
        ApplyRuleLockStep { Lock.Cards: { } locked } => [locked],
        _ => []
    };
}
