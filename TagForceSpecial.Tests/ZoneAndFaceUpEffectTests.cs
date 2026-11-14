using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Effects used from the hand or Graveyard, triggers on face-up Spells and Traps, and Quick Effects on face-up cards.</summary>
public sealed class ZoneAndFaceUpEffectTests
{
    private const uint W = 901, ZombieA = 902, FiendA = 903, Storm = 910, Negate = 911, Imperial = 912, HandQuick = 913, OppOnly = 914, FaceUpQuick = 915, Hole = 916, Ping = 917,
        Destroyersaurus = 7108, Jurassic = 6823, Mezuki = 7359, Fork = 10735, Squad = 11147, Poison = 5607;

    private static readonly AuthoredCard QuickContinuous = new CardTable("probe").ContinuousWithEffects(FaceUpQuick, "Quick Continuous", false, true, new EffectOption([new DealDamageStep(100)]));

    private static CardCatalog Catalog() => new(new(null, "synthetic-zone-effects"),
        TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0))).Concat(
        [
            new(W, "Warrior", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(ZombieA, "Zombie", "", new(1000, 1000, 4), new(1, MonsterTypes.Zombie, 1, 1, 0, 0, 0)),
            new(FiendA, "Fiend", "", new(1000, 1000, 4), new(1, MonsterTypes.Fiend, 1, 1, 0, 0, 0)),
            new(HandQuick, "Hand Quick", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)),
            new(OppOnly, "Opponent Only", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0))
        ]).Concat(new uint[] { Storm, Negate, Imperial, FaceUpQuick, Hole, Ping }.Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in new[] { W, ZombieA, FiendA, HandQuick, OppOnly }) rules[id] = monster;
        foreach (uint id in new[] { Storm, FaceUpQuick, Hole, Ping }) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        foreach (uint id in new[] { Negate, Imperial }) rules[id] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, .. QuickContinuous.Definitions,
                new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.SpellTrap)]),
                new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("ping", Ping, EffectKind.NormalSpell, [new DealDamageStep(1)]),
                ResponseProbe.Negate("negate", Negate, ResponseNegation.Activation, ChainSpellSpeed.Three),
                new("imperial", Imperial, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells),
                new("hand-quick", HandQuick, EffectKind.MonsterQuick, [new DealDamageStep(100)], cost: new SendSelfCost(SelfDestination.Discard), origin: EffectOrigin.Hand),
                new("opp-only", OppOnly, EffectKind.MonsterQuick, [new DealDamageStep(100)], [new TurnTimingCondition(TurnOwner.Opponent, MainPhaseOnly: true)], new SendSelfCost(SelfDestination.Graveyard), origin: EffectOrigin.Hand)], shuffleSeed: 4);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id, PlayerId? p = null) => P(e, p ?? Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));
    private static long Chain(DuelEngine e) => e.State.PendingChain!.DecisionId;

    private static void ToChoice(DuelEngine e)
    {
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, Chain(e)));
        Ok(e, new ResolveNextChainLink(Turn(e), Chain(e)));
    }

    [Fact]
    public void AHandEffectDiscardsItselfThenResolves()
    {
        var e = Engine(Deck(Destroyersaurus, W, W, W, W, Jurassic), Deck());
        EnterMain(e);
        Ok(e, new ActivateZoneEffect(PlayerId.One, Hand(e, Destroyersaurus)));
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Destroyersaurus);
        ToChoice(e);
        var choice = e.State.PendingChain!.DefinitionCardChoice!;
        Assert.Equal(Jurassic, Assert.Single(choice.Options).GameCardId);
        Ok(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, choice.Options[0].InstanceId));
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == Jurassic);
    }

    [Fact]
    public void AHandEffectNeedsSomethingToResolveAndAnOpenMainPhase()
    {
        var e = Engine(Deck(Destroyersaurus), Deck());
        Ok(e, new ResolveDrawStep(PlayerId.One));
        Reject(e, new ActivateZoneEffect(PlayerId.One, Hand(e, Destroyersaurus)), DuelError.WrongPhase);
        Phase(e, DuelPhase.Main1);
        Reject(e, new ActivateZoneEffect(PlayerId.One, Hand(e, Destroyersaurus)), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ACardWithoutThatEffectCannotUseItFromTheWrongPlace()
    {
        var e = Engine(Deck(Mezuki), Deck());
        EnterMain(e);
        Reject(e, new ActivateZoneEffect(PlayerId.One, Hand(e, Mezuki)), DuelError.UnsupportedMonsterEffect);
        Reject(e, new ActivateZoneEffect(PlayerId.One, Hand(e, W)), DuelError.UnsupportedMonsterEffect);
        Reject(e, new ActivateZoneEffect(PlayerId.One, 9999), DuelError.CardNotInHand);
    }

    [Fact]
    public void ANegatedHandEffectKeepsItsCostAndDoesNotResolve()
    {
        var e = Engine(Deck(HandQuick), Deck(Negate));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negate), 0)); Next(e);
        Ok(e, new ActivateZoneEffect(PlayerId.One, Hand(e, HandQuick)));
        Ok(e, new ActivateChainResponse(PlayerId.Two, Chain(e), 0));
        Resolve(e);
        Assert.Equal(8000, P(e, PlayerId.Two).LifePoints);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == HandQuick);
    }

    private static DuelEngine BothInGraveyard(uint[] first)
    {
        var e = Engine(first, Deck(Hole, Hole));
        EnterMain(e); Summon(e, Mezuki, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        return e;
    }

    [Fact]
    public void AGraveyardEffectBanishesItselfAndCannotTargetItself()
    {
        var alone = BothInGraveyard(Deck(Mezuki));
        Reject(alone, new ActivateZoneEffect(PlayerId.One, P(alone, PlayerId.One).Graveyard.First(c => c.GameCardId == Mezuki).InstanceId), DuelError.DecisionUnavailable);

        var e = Engine(Deck(Mezuki, ZombieA), Deck(Hole, Hole));
        EnterMain(e); Summon(e, Mezuki, 0); Next(e); Activate(e, Hole); Resolve(e); Next(e);
        Summon(e, ZombieA, 0); Next(e); Activate(e, Hole, 3); Resolve(e); Next(e);
        Ok(e, new ActivateZoneEffect(PlayerId.One, P(e, PlayerId.One).Graveyard.First(c => c.GameCardId == Mezuki).InstanceId));
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == Mezuki);
        Assert.Equal(ZombieA, Assert.Single(e.State.PendingDecision!.FieldTargetOptions!).Card.GameCardId);
    }

    [Fact]
    public void AHandEffectCanSpecialSummonItselfAfterItsCost()
    {
        var e = Engine(Deck(Fork, FiendA), Deck());
        EnterMain(e);
        Ok(e, new ActivateZoneEffect(PlayerId.One, Hand(e, Fork)));
        var cost = e.State.PendingDecision!;
        Assert.Equal(FiendA, Assert.Single(cost.CardOptions).GameCardId);
        Ok(e, new ChooseDecisionCard(PlayerId.One, cost.DecisionId, cost.CardOptions[0].InstanceId));
        ToChoice(e);
        var summon = e.State.PendingChain!.DefinitionSummon!;
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, summon.Options[0].Card.InstanceId, 2, BattlePosition.FaceUpAttack));
        Assert.Equal(Fork, P(e, PlayerId.One).MonsterZones[2]!.Card.GameCardId);
        Assert.DoesNotContain(P(e, PlayerId.One).Hand, c => c.GameCardId == Fork);
    }

    [Fact]
    public void AHandQuickEffectAnswersAChainButAnIgnitionCannot()
    {
        var e = Engine(Deck(Ping), Deck(HandQuick, Destroyersaurus, W, W, W, Jurassic));
        EnterMain(e);
        Activate(e, Ping);
        Reject(e, new ActivateZoneEffectResponse(PlayerId.Two, Chain(e), Hand(e, Destroyersaurus, PlayerId.Two)), DuelError.ActivationTimingNotAllowed);
        Ok(e, new ActivateZoneEffectResponse(PlayerId.Two, Chain(e), Hand(e, HandQuick, PlayerId.Two)));
        Resolve(e);
        Assert.Equal(7900, P(e, PlayerId.One).LifePoints);
        Assert.Equal(7999, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AHandQuickEffectOpensAPhaseChangeWindow()
    {
        var e = Engine(Deck(), Deck(HandQuick));
        EnterMain(e);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.NotNull(e.State.OpenWindow);
        Ok(e, new ActivateZoneEffectResponse(PlayerId.Two, e.State.OpenWindow!.DecisionId, Hand(e, HandQuick, PlayerId.Two)));
        Resolve(e);
        Assert.Equal(7900, P(e, PlayerId.One).LifePoints);
    }

    [Fact]
    public void TurnTimingLimitsAnEffect()
    {
        var own = Engine(Deck(OppOnly), Deck());
        EnterMain(own);
        Reject(own, new ActivateZoneEffect(PlayerId.One, Hand(own, OppOnly)), DuelError.ActivationConditionNotMet);

        var e = Engine(Deck(Ping), Deck(OppOnly));
        EnterMain(e);
        Activate(e, Ping);
        Ok(e, new ActivateZoneEffectResponse(PlayerId.Two, Chain(e), Hand(e, OppOnly, PlayerId.Two)));
    }

    private static DuelEngine SquadActive(uint[] second)
    {
        var e = Engine(Deck(Squad), second);
        EnterMain(e);
        Activate(e, Squad, 3); Resolve(e);
        Summon(e, W, 0);
        return e;
    }

    [Fact]
    public void AFaceUpSpellTriggersOnItsControllersMonsterAndOncePerTurn()
    {
        var e = SquadActive(Deck(Hole, Hole));
        Next(e); Next(e);
        Summon(e, W, 1); Next(e);
        int hand = P(e, PlayerId.One).Hand.Count; // Two monsters are destroyed together: one draw.
        Activate(e, Hole); Resolve(e);
        EventEffectScenario.Drain(e);
        Assert.Equal(hand + 1, P(e, PlayerId.One).Hand.Count);
    }

    [Fact]
    public void AFaceUpSpellDoesNotTriggerForTheOpponentsMonster()
    {
        var e = Engine(Deck(Squad, Hole), Deck(W));
        EnterMain(e);
        Activate(e, Squad, 3); Resolve(e); Next(e);
        Summon(e, W, 0); Next(e);
        Activate(e, Hole); Resolve(e);
        Assert.Null(e.State.EventEffects.PendingTriggers);
    }

    [Fact]
    public void ADestroyedSpellNoLongerTriggers()
    {
        var e = SquadActive(Deck(Storm, Hole));
        Next(e);
        Activate(e, Storm); Resolve(e);
        Next(e); Next(e);
        int hand = P(e, PlayerId.One).Hand.Count;
        Activate(e, Hole); Resolve(e);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(hand, P(e, PlayerId.One).Hand.Count);
    }

    [Fact]
    public void ANegatedSpellStopsItsFaceUpTrigger()
    {
        var e = Engine(Deck(Squad, Imperial), Deck(Hole));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Imperial), 0));
        Activate(e, Squad, 3); Resolve(e);
        Summon(e, W, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e); Next(e);
        int hand = P(e, PlayerId.One).Hand.Count;
        Activate(e, Hole); Resolve(e);
        EventEffectScenario.Drain(e);
        Assert.Equal(hand, P(e, PlayerId.One).Hand.Count);
    }

    private static DuelEngine QuickActive()
    {
        var e = Engine(Deck(FaceUpQuick), Deck(Storm, Storm));
        EnterMain(e);
        Activate(e, FaceUpQuick); Resolve(e);
        return e;
    }

    [Fact]
    public void AFaceUpQuickEffectWorksInTheMainPhaseAndOncePerTurn()
    {
        var e = QuickActive();
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1)); Resolve(e);
        Assert.Equal(7900, P(e, PlayerId.Two).LifePoints);
        Reject(e, new ActivateCardEffect(PlayerId.One, 4, 1), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void AFaceUpQuickEffectAnswersAChainAndAPhaseChange()
    {
        var e = QuickActive();
        Next(e);
        Activate(e, Storm);
        Ok(e, new ActivateFieldEffectResponse(PlayerId.One, Chain(e), 4, 1));
        Resolve(e);
        Assert.Equal(7900, P(e, PlayerId.Two).LifePoints);

        var windowed = QuickActive();
        Next(windowed);
        Ok(windowed, new ChangePhase(PlayerId.Two, DuelPhase.End));
        Ok(windowed, new ActivateFieldEffectResponse(PlayerId.One, windowed.State.OpenWindow!.DecisionId, 4, 1));
        Resolve(windowed);
        Assert.Equal(7900, P(windowed, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AFaceUpIgnitionCannotAnswerAChain()
    {
        var e = Engine(Deck(Squad, 7573), Deck(Storm));
        EnterMain(e);
        Activate(e, 7573, 3); Resolve(e); Next(e);
        Activate(e, Storm);
        Reject(e, new ActivateFieldEffectResponse(PlayerId.One, Chain(e), 3, 1), DuelError.ActivationTimingNotAllowed);
    }

    [Fact]
    public void AModalQuickPlayCanChooseItsOptionAsAChainResponse()
    {
        var e = Engine(Deck(Poison), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Poison), 0));
        Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0, 1)); Resolve(e);
        Assert.Equal(7200, P(e, PlayerId.Two).LifePoints);

        var hand = Engine(Deck(Poison, Ping), Deck());
        EnterMain(hand);
        Activate(hand, Ping, 3);
        Ok(hand, new PassChainResponse(PlayerId.Two, Chain(hand)));
        Ok(hand, new ActivateHandChainResponse(PlayerId.One, Chain(hand), Hand(hand, Poison), 2, 1));
        Resolve(hand);
        Assert.Equal(7199, P(hand, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void TheAdapterOffersHandGraveyardAndFaceUpEffects()
    {
        var e = Engine(Deck(Destroyersaurus, FaceUpQuick, W, W, W, Jurassic), Deck());
        var source = new DuelActionSource(e, Catalog());
        EnterMain(e);
        Activate(e, FaceUpQuick); Resolve(e);
        var actions = source.Observe(PlayerId.One).Actions;
        Assert.Contains(actions, a => a.Kind == ActionKind.CardEffect && a.CardInstanceId == Hand(e, Destroyersaurus));
        Assert.Contains(actions, a => a.Kind == ActionKind.CardEffect && a.Zone == 4);
    }

    [Fact]
    public void DefinitionsValidateOriginsAndSelfCosts()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", W, EffectKind.NormalSpell, [new DealDamageStep(1)], origin: EffectOrigin.Hand));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", W, EffectKind.MonsterIgnition, [new DealDamageStep(1)], cost: new SendSelfCost(SelfDestination.Banish), origin: EffectOrigin.Hand));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", W, EffectKind.MonsterIgnition, [new DealDamageStep(1)], cost: new SendSelfCost(SelfDestination.Discard), origin: EffectOrigin.Graveyard));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", W, EffectKind.MonsterIgnition, [new DealDamageStep(1)], cost: new SendSelfCost(SelfDestination.Discard)));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", W, EffectKind.MonsterIgnition, [new SpecialSummonSelfStep()]));
    }
}
