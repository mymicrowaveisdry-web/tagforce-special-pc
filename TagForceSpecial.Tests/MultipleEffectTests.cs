using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Cards with more than one effect: chosen options, separate activated effects and effects used while face-up.</summary>
public sealed class MultipleEffectTests
{
    private const uint W = 901, Dual = 902, Shared = 903, ZombieA = 904, Storm = 910, Negate = 911, Imperial = 912, Choice = 913, Gated = 914, Burner = 915, Mummy = 5435, Poison = 5607;

    private static readonly CardTable T = new("probe");
    private static readonly AuthoredCard DualMonster = T.ActivatedEffects(Dual, "Dual", quick: false, sharedLimit: false, null,
        new EffectOption([new DealDamageStep(100)]), new EffectOption([new GainLifePointsStep(200)]));
    private static readonly AuthoredCard SharedMonster = T.ActivatedEffects(Shared, "Shared", quick: false, sharedLimit: true, null,
        new EffectOption([new DealDamageStep(100)]), new EffectOption([new GainLifePointsStep(200)]));
    private static readonly AuthoredCard ChoiceSpell = T.Modal(Choice, "Choice", EffectKind.NormalSpell, "none",
        new EffectOption([new GainLifePointsStep(300)]), new EffectOption([new DestroyAllStep(FieldCardKind.Monster)]));
    private static readonly AuthoredCard BurnerSpell = T.ContinuousWithEffects(Burner, "Burner", trap: false, new EffectOption([new DealDamageStep(100)]));

    private static CardCatalog Catalog() => new(new(null, "synthetic-multiple-effects"),
        TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(0, 0, 1), new(1, 0, 1, 1, 0, 0, 0))).Concat(
            new uint[] { W, Dual, Shared, Storm, Negate, Imperial, Choice, Gated, Burner }.Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0))))
            .Append(new CardDefinition(ZombieA, "Zombie", "", new(1000, 1000, 4), new(1, MonsterTypes.Zombie, 1, 1, 0, 0, 0))));

    private static EffectDefinition[] Extra() =>
    [
        .. DualMonster.Definitions, .. SharedMonster.Definitions, .. ChoiceSpell.Definitions, .. BurnerSpell.Definitions,
        new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.SpellTrap)]),
        ResponseProbe.Negate("negate", Negate, ResponseNegation.Activation, ChainSpellSpeed.Three),
        new("imperial", Imperial, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells),
        new("gated-1", Gated, EffectKind.NormalSpell, [new DealDamageStep(100)]),
        new("gated-2", Gated, EffectKind.NormalSpell, [new AddFromDeckToHandStep(new CardIdFilter([Storm]))]),
        new("snipe", 916, EffectKind.NormalTrap, [new DestroyTargetsStep()], [new RespondsToActivationCondition()], fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap)])
    ];

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in new[] { W, Dual, Shared, ZombieA }) rules[id] = monster;
        foreach (uint id in new[] { Storm, Choice, Gated, Burner }) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        foreach (uint id in new[] { Negate, Imperial, 916u }) rules[id] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        var catalog = new CardCatalog(new(null, "synthetic-multiple-effects"), Catalog().Cards.Append(new CardDefinition(916, "Snipe", "", new(0, 0, 1))));
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, .. Extra()], shuffleSeed: 2);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static int Lp(DuelEngine e, PlayerId p) => e.State.GetPlayer(p).LifePoints;

    [Fact]
    public void EachActivatedEffectIsChosenAndLimitedSeparately()
    {
        var e = Engine(Deck(Dual), Deck());
        EnterMain(e); Summon(e, Dual, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Resolve(e);
        Assert.Equal(7900, Lp(e, PlayerId.Two));
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0, EffectIndex: 1)); Resolve(e);
        Assert.Equal(8200, Lp(e, PlayerId.One));
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0, EffectIndex: 1), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void AnEffectIndexOutOfRangeIsRejected()
    {
        var e = Engine(Deck(Dual), Deck());
        EnterMain(e); Summon(e, Dual, 0);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0, EffectIndex: 2), DuelError.UnsupportedMonsterEffect);
    }

    [Fact]
    public void EffectsCanShareOneUsePerTurnAndResetNextTurn()
    {
        var e = Engine(Deck(Shared), Deck());
        EnterMain(e); Summon(e, Shared, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0, EffectIndex: 1)); Resolve(e);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Next(e); Next(e);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Resolve(e);
        Assert.Equal(7900, Lp(e, PlayerId.Two));
    }

    [Fact]
    public void ModalSpellResolvesOnlyTheChosenOption()
    {
        var e = Engine(Deck(Poison), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Poison), 4, 1)); Resolve(e);
        Assert.Equal(7200, Lp(e, PlayerId.Two));
        Assert.Equal(8000, Lp(e, PlayerId.One));
    }

    [Fact]
    public void ASetModalCardChoosesWhenItIsActivated()
    {
        var e = Engine(Deck(Choice), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Choice), 0));
        Ok(e, new ActivateSetSpell(PlayerId.One, 0, 0)); Resolve(e);
        Assert.Equal(8300, Lp(e, PlayerId.One));
    }

    [Fact]
    public void AnOptionThatCouldNotResolveCannotBeChosen()
    {
        var e = Engine(Deck(Choice), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Choice), 4, 1), DuelError.DecisionUnavailable);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Choice), 4, 0));
    }

    [Fact]
    public void ANegatedOptionDoesNothingAndTheCardLeaves()
    {
        var e = Engine(Deck(Poison), Deck(Negate));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negate), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Poison), 4, 1));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Resolve(e);
        Assert.Equal(8000, Lp(e, PlayerId.Two));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Poison);
    }

    private static DuelEngine BurnerActive(uint[]? second = null)
    {
        var e = Engine(Deck(Burner), second ?? Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burner), 4));
        Resolve(e);
        return e;
    }

    [Fact]
    public void AFaceUpCardUsesItsEffectAndStays()
    {
        var e = BurnerActive();
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1));
        var events = Resolve(e);
        Assert.Equal(7900, Lp(e, PlayerId.Two));
        Assert.DoesNotContain(events, ev => ev is SpellSentToGraveyard);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).SpellTrapFields[4]);
    }

    [Fact]
    public void AFaceUpEffectIsOncePerTurnAndNeedsItsOwnMainPhase()
    {
        var e = BurnerActive();
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1)); Resolve(e);
        Reject(e, new ActivateCardEffect(PlayerId.One, 4, 1), DuelError.EffectAlreadyUsed);
        Next(e); Next(e);
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1)); Resolve(e);
        Assert.Equal(7800, Lp(e, PlayerId.Two));
        Phase(e, DuelPhase.End);
        Reject(e, new ActivateCardEffect(PlayerId.One, 4, 1), DuelError.WrongPhase);
    }

    [Fact]
    public void ASetCardCannotUseItsFaceUpEffectAndTheCardsOwnEntryIsNotAnEffect()
    {
        var e = Engine(Deck(Burner), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Burner), 0));
        Reject(e, new ActivateCardEffect(PlayerId.One, 0, 1), DuelError.UnsupportedSpellEffect);
        var active = BurnerActive();
        Reject(active, new ActivateCardEffect(PlayerId.One, 4, 0), DuelError.UnsupportedSpellEffect);
    }

    [Fact]
    public void AFaceUpEffectCannotBeActivatedFromTheHand()
    {
        var e = Engine(Deck(Burner), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Burner), 4, 1), DuelError.ActivationTimingNotAllowed);
    }

    [Fact]
    public void AFaceUpEffectStillResolvesIfTheCardLeavesInResponse()
    {
        var e = Engine(Deck(Burner), Deck(916u));
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burner), 4)); Resolve(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, 916), 0)); Next(e);
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var pending = e.State.PendingDecision!;
        var target = pending.FieldTargetOptions!.Single(o => o.Player == PlayerId.One && o.Zone == 4);
        Ok(e, new ChooseDecisionFieldTarget(pending.Player, pending.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId));
        Resolve(e);
        Assert.Null(e.State.GetPlayer(PlayerId.One).SpellTrapFields[4]);
        Assert.Equal(7900, Lp(e, PlayerId.Two));
    }

    [Fact]
    public void ANegatedSpellStopsItsFaceUpEffectFromApplying()
    {
        var e = Engine(Deck(Burner, Imperial), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Imperial), 0));
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burner), 4)); Resolve(e);
        Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceEffectsNegated });
        Assert.Equal(8000, Lp(e, PlayerId.Two));
    }

    [Fact]
    public void CallOfTheMummyNeedsNoMonstersToActivateAndToResolve()
    {
        var blocked = Engine(Deck(Mummy, ZombieA), Deck());
        EnterMain(blocked);
        Ok(blocked, new ActivateSpell(PlayerId.One, Hand(blocked, Mummy), 4)); Resolve(blocked);
        Summon(blocked, W, 0);
        Reject(blocked, new ActivateCardEffect(PlayerId.One, 4, 1), DuelError.ActivationConditionNotMet);

        var e = Engine(Deck(Mummy, ZombieA), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Mummy), 4)); Resolve(e);
        Ok(e, new ActivateCardEffect(PlayerId.One, 4, 1));
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, e.State.PendingChain.DecisionId));
        Ok(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId));
        var summon = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal(ZombieA, Assert.Single(summon.Options).Card.GameCardId);
    }

    [Fact]
    public void TheAdapterOffersEveryEffectOfACard()
    {
        var e = Engine(Deck(Dual, Burner), Deck());
        var source = new DuelActionSource(e, Catalog());
        EnterMain(e); Summon(e, Dual, 0);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Burner), 4)); Resolve(e);
        var actions = source.Observe(PlayerId.One).Actions;
        Assert.Equal(2, actions.Count(a => a.Kind == ActionKind.MonsterEffect && a.Zone == 0));
        Assert.Single(actions, a => a.Kind == ActionKind.CardEffect && a.Zone == 4);
    }

    [Fact]
    public void OnlySupportedEffectCombinationsLoad()
    {
        var catalog = Catalog();
        var rules = new Dictionary<uint, DuelCardRules> { [Burner] = new(CardCategory.Spell, AllowsSpellTrapSet: true), [W] = new(CardCategory.Monster, AllowsNormalSummon: true) };
        var deck = Enumerable.Repeat(W, 40).ToArray();
        EffectDefinition normal = new("a", Burner, EffectKind.NormalSpell, [new DealDamageStep(1)]), quick = new("b", Burner, EffectKind.QuickPlaySpell, [new DealDamageStep(1)]);
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [normal, quick]));
        EffectDefinition ignition = new("c", Burner, EffectKind.SpellTrapIgnition, [new DealDamageStep(1)]);
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [normal, ignition]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("d", Burner, EffectKind.NormalSpell, [new DealDamageStep(1)], oncePerTurnKey: "k"));
    }
}
