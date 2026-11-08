using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Monster triggers wired through the shared definition steps and the staged battle flow.</summary>
public sealed class MonsterTriggerFamilyTests
{
    private const uint Sangan = 4054, Witch = 4580, Zolga = 5363, Firefly = 5966, Tomato = 4930,
        Attacker = 901, DarkLow = 902, DarkHigh = 903, LightLow = 904, Variable = 905, High = 906, Filler = 907,
        DarkHole = 4342, Negator = 910, BottomlessHole = 5400, Hyena = 6247, Germ = 4921, Momonga = 4922, Evacuation = 5914;
    private static readonly uint Restricted = TfspSummonRestrictions.DeckSummonBlockedIds[0];

    private static CardNumericCodes Attribute(uint code) => new(code, 0, 1, 1, 0, 0, 0);

    private static CardCatalog Catalog() => new(new(null, "synthetic-monster-trigger"),
        MonsterTriggerFamily.Cards.Select(c => c.CardId).Except([Tomato]).Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4), Attribute(2)))
            .Concat([
                new(Tomato, "Probe tomato", "", new(1400, 1100, 4), Attribute(2)),
                new(Attacker, "Attacker", "", new(2000, 1000, 4), Attribute(5)),
                new(DarkLow, "Dark low", "", new(1500, 2000, 4), Attribute(2)),
                new(DarkHigh, "Dark high", "", new(1600, 500, 4), Attribute(2)),
                new(LightLow, "Light low", "", new(100, 100, 4), Attribute(1)),
                new(Variable, "Variable", "", new(null, null, 4), Attribute(2)),
                new(High, "High", "", new(2500, 2000, 6), Attribute(5)),
                new(Filler, "Filler", "", new(1700, 1800, 4), Attribute(5)),
                new(DarkHole, "Hole", "", new(0, 0, 1)),
                new(Negator, "Negator", "", new(0, 0, 1)),
                new(BottomlessHole, "Bottomless", "", new(0, 0, 1)),
                new(Evacuation, "Evacuation", "", new(0, 0, 1)),
                new(Restricted, "Restricted", "", new(1000, 1000, 4), Attribute(2))]));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second, IEnumerable<EffectDefinition>? extra = null,
        IEnumerable<TriggerEffectProfile>? triggers = null)
    {
        var rules = MonsterTriggerFamily.Rules.ToDictionary();
        foreach (uint id in new[] { Attacker, DarkLow, DarkHigh, LightLow, Variable, Filler, Restricted })
            rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[High] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true);
        rules[DarkHole] = RemovalFamily.Rules[DarkHole];
        rules[Negator] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        rules[BottomlessHole] = SummonTrapFamily.Rules[BottomlessHole];
        rules[Evacuation] = RemovalFamily.Rules[Evacuation];
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: [.. MonsterTriggerFamily.Triggers, .. triggers ?? []], experimentalBattleFlow: true,
            effectDefinitions: [.. RemovalFamily.Definitions.Where(d => d.CardId is DarkHole or Evacuation), .. extra ?? []], shuffleSeed: 1);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    /// <summary>The monster and four fillers form the opening hand; <paramref name="rest"/> stays in the Deck.</summary>
    private static uint[] Stacked(uint monster, params uint[] rest) => Deck([monster, Filler, Filler, Filler, Filler, .. rest]);
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Play(DuelEngine e, uint id, int zone = 0) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));

    /// <summary>Player One summons <paramref name="monster"/>; Player Two summons Attacker and attacks it.</summary>
    private static DuelEngine Attacked(uint monster, uint[]? first = null)
    {
        var e = Engine(first ?? Deck(monster), Deck(Attacker));
        EnterMain(e); Play(e, monster); Next(e);
        Play(e, Attacker); Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        return e;
    }

    /// <summary>Advances the attack until a trigger choice appears or the attack finishes.</summary>
    private static List<DuelEvent> ToTrigger(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.EventEffects.PendingTriggers is null && e.State.Battle?.Attack is not null && e.State.Outcome is null)
        {
            S.Close(e); events.AddRange(S.Advance(e).Events);
        }
        return events;
    }

    private static IEnumerable<uint> Options(DuelEngine e) => e.State.PendingChain!.DefinitionCardChoice!.Options.Select(c => c.GameCardId).Distinct();

    /// <summary>Activates the pending triggers and resolves their chain up to any card or Summon choice.</summary>
    private static List<DuelEvent> Activate(DuelEngine e)
    {
        var batch = e.State.EventEffects.PendingTriggers!;
        List<DuelEvent> events = [.. Ok(e, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray())).Events];
        while (e.State.PendingChain is { DefinitionSummon: null, DefinitionCardChoice: null } chain && e.State.Outcome is null)
            events.AddRange(Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId)).Events);
        return events;
    }

    [Fact]
    public void SanganSearchesOnlyMonstersWithinTheAttackLimit()
    {
        var e = Attacked(Sangan, Stacked(Sangan, DarkLow, DarkHigh, Variable, LightLow));
        ToTrigger(e); Activate(e);
        Assert.Equal([DarkLow, LightLow], Options(e).Order());
        int deck = e.State.GetPlayer(PlayerId.One).Deck.Count;
        var choice = e.State.PendingChain!.DefinitionCardChoice!;
        var events = Ok(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, choice.Options[0].InstanceId)).Events;
        Assert.Contains(events, ev => ev is CardSearched);
        Assert.Contains(events, ev => ev is DeckShuffled);
        Assert.Equal(deck - 1, e.State.GetPlayer(PlayerId.One).Deck.Count);
    }

    [Fact]
    public void WitchSearchesByDefense()
    {
        var e = Attacked(Witch, Stacked(Witch, DarkLow, DarkHigh, Variable));
        ToTrigger(e); Activate(e);
        Assert.Contains(DarkHigh, Options(e));
        Assert.DoesNotContain(DarkLow, Options(e));
        Assert.DoesNotContain(Variable, Options(e));
    }

    [Fact]
    public void SanganTriggersWhenDestroyedByAnEffect()
    {
        var e = Engine(Stacked(Sangan, DarkLow), Deck(DarkHole));
        EnterMain(e); Play(e, Sangan); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, DarkHole), 0));
        Resolve(e);
        Assert.Equal(PlayerId.Two, e.State.ActivePlayer);
        Assert.Equal(PlayerId.One, e.State.EventEffects.PendingTriggers!.Options.Single().Player);
    }

    [Fact]
    public void SanganDoesNotTriggerFromTheHand()
    {
        var e = Engine(Deck(Filler, Filler, Filler, Filler, Sangan), Deck());
        EnterMain(e);
        for (int i = 0; i < 4; i++) Next(e);
        Phase(e, DuelPhase.End);
        Ok(e, new DiscardForHandLimit(PlayerId.One, Hand(e, Sangan)));
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Sangan);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Null(e.State.PendingChain);
    }

    [Fact]
    public void AMandatorySearchWithNoMatchStillResolvesWithoutAShuffle()
    {
        var e = Attacked(Sangan);
        ToTrigger(e);
        var events = Activate(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.NoMatchingCard });
        Assert.DoesNotContain(events, ev => ev is DeckShuffled);
    }

    [Fact]
    public void AFloaterSummonsOnlyItsAttributeAndOnlyInAttackPosition()
    {
        var e = Attacked(Tomato, Stacked(Tomato, DarkLow, DarkHigh, LightLow));
        ToTrigger(e); Activate(e);
        var summon = e.State.PendingChain!.DefinitionSummon!;
        Assert.Equal([DarkLow], summon.Options.Select(o => o.Card.GameCardId).Distinct());
        Assert.Equal([BattlePosition.FaceUpAttack], summon.PositionOptions);
        int card = summon.Options[0].Card.InstanceId;
        Reject(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, card, 0, BattlePosition.FaceUpDefense), DuelError.InvalidBattlePosition);
        var events = Ok(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, card, 0, BattlePosition.FaceUpAttack)).Events;
        Assert.Contains(events, ev => ev is MonsterSpecialSummoned { Source: DecisionCardSource.Deck });
        Assert.Contains(events, ev => ev is DeckShuffled);
        Assert.Equal(DarkLow, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void AFloaterMayBeDeclined()
    {
        var e = Attacked(Tomato, Stacked(Tomato, DarkLow));
        ToTrigger(e);
        var batch = e.State.EventEffects.PendingTriggers!;
        Assert.False(batch.Mandatory);
        Ok(e, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, []));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Null(e.State.PendingChain);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void AFloaterNeedsATargetAndABattleDestruction()
    {
        var none = Attacked(Tomato);
        Assert.Contains(ToTrigger(none), ev => ev is TriggerUnavailable);
        Assert.Null(none.State.EventEffects.PendingTriggers);

        var e = Engine(Stacked(Tomato, DarkLow), Deck(DarkHole));
        EnterMain(e); Play(e, Tomato); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, DarkHole), 0));
        Resolve(e);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Tomato);
        Assert.Null(e.State.EventEffects.PendingTriggers);
    }

    private static TributeSummon TributeFor(DuelEngine e, int zone = 0)
    {
        var monster = e.State.GetPlayer(Turn(e)).MonsterZones[zone]!;
        return new(Turn(e), e.State.Revision, Hand(e, High), zone, [new(Turn(e), zone, monster.Card, monster.PlacementId)], false, null);
    }

    [Fact]
    public void ZolgaGainsLifePointsWhenTributedForASummon()
    {
        var e = Engine(Deck(Zolga, High), Deck());
        EnterMain(e); Play(e, Zolga); Next(e); Next(e);
        Ok(e, TributeFor(e));
        Assert.Contains(Activate(e), ev => ev is LifePointsGained { Amount: 2000 });
        Assert.Equal(10000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void ZolgaDoesNothingWhenDestroyedOrWhenTheSummonIsNegated()
    {
        var destroyed = Engine(Deck(Zolga), Deck(DarkHole));
        EnterMain(destroyed); Play(destroyed, Zolga); Next(destroyed);
        Ok(destroyed, new ActivateSpell(PlayerId.Two, Hand(destroyed, DarkHole), 0));
        Resolve(destroyed);
        Assert.Null(destroyed.State.EventEffects.PendingTriggers);

        var e = Engine(Deck(Zolga, High), Deck(Negator), [ResponseProbe.NegateSummon("negate", Negator)]);
        EnterMain(e); Play(e, Zolga); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negator), 0)); Next(e);
        Ok(e, TributeFor(e));
        var pending = e.State.PendingSummon!;
        Ok(e, new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0));
        Assert.Contains(Resolve(e), ev => ev is SummonNegated);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void AtomicFireflyBurnsThePlayerWhoDestroyedIt()
    {
        var e = Attacked(Firefly);
        ToTrigger(e);
        Assert.Contains(Activate(e), ev => ev is EffectDamageDealt { Damage: 1000 });
        Assert.Equal(7000, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ATriggerLinkCanBeNegated()
    {
        var e = Engine(Stacked(Sangan, DarkLow), Deck(DarkHole, Negator),
            [ResponseProbe.Negate("negate", Negator, ResponseNegation.Effect, respondsTo: CardCategory.Monster)]);
        EnterMain(e); Play(e, Sangan); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negator), 0)); Next(e); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, DarkHole), 1));
        Resolve(e);
        var batch = e.State.EventEffects.PendingTriggers!;
        Ok(e, new ChooseTriggerOrder(PlayerId.One, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.EffectNegated });
        Assert.DoesNotContain(events, ev => ev is CardSearched);
    }

    [Fact]
    public void ASummonTrapCannotAnswerAFloaterInTheDamageStep()
    {
        var e = Engine(Stacked(Tomato, DarkLow), Deck(Attacker, BottomlessHole), SummonTrapFamily.Definitions.Where(d => d.CardId == BottomlessHole));
        EnterMain(e); Play(e, Tomato); Next(e);
        Play(e, Attacker); Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, BottomlessHole), 0)); Next(e); Next(e);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        ToTrigger(e); Activate(e);
        var summon = e.State.PendingChain!.DefinitionSummon!;
        Ok(e, new ChooseDefinitionSummon(PlayerId.One, summon.DecisionId, summon.Options[0].Card.InstanceId, 0, BattlePosition.FaceUpAttack));
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Null(e.State.OpenWindow);
    }

    [Fact]
    public void TheActionAdapterOffersOrderAndDeclineAndThePolicyActivates()
    {
        var e = Attacked(Tomato, Stacked(Tomato, DarkLow));
        ToTrigger(e);
        var source = new DuelActionSource(e, Catalog());
        var view = source.Observe(PlayerId.One);
        Assert.Equal(ActionAvailability.Ready, view.Availability);
        Assert.Equal("TriggerOrder", view.Decision!.Kind);
        Assert.Equal([ActionKind.DeclineTriggers, ActionKind.OrderTriggers], view.Actions.Select(a => a.Kind));
        Assert.Equal(ActionAvailability.Waiting, source.Observe(PlayerId.Two).Availability);
        var choice = new TagForceSpecial.AI.DeterministicPolicy(7).Choose(view)!;
        Assert.Equal(ActionKind.OrderTriggers, choice.Kind);
        Assert.True(source.Execute(PlayerId.One, choice.Revision, choice.Token).Accepted);
        Assert.NotNull(e.State.PendingChain);
    }

    private static PendingDefinitionSummon SummonChoice(DuelEngine e) => e.State.PendingChain!.DefinitionSummon!;
    private static DuelActionResult Pick(DuelEngine e, int option, int zone, BattlePosition position)
    {
        var summon = SummonChoice(e);
        return Ok(e, new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[option].Card.InstanceId, zone, position));
    }

    [Fact]
    public void AnAnyNumberSummonPlacesEveryChoiceTogetherAndShufflesOnce()
    {
        var e = Attacked(Hyena, Stacked(Hyena, Hyena, Hyena));
        ToTrigger(e); Activate(e);
        Assert.False(SummonChoice(e).CanFinish);
        Reject(e, new FinishDefinitionSummon(PlayerId.One, SummonChoice(e).DecisionId), DuelError.InvalidDecisionChoice);
        Assert.Empty(Pick(e, 0, 0, BattlePosition.FaceUpAttack).Events.OfType<MonsterSpecialSummoned>());
        Assert.True(SummonChoice(e).CanFinish);
        Assert.DoesNotContain(0, SummonChoice(e).MonsterZoneOptions);
        Assert.Single(SummonChoice(e).Options);
        var events = Pick(e, 0, 1, BattlePosition.FaceUpDefense).Events;
        Assert.Equal(2, events.OfType<MonsterSpecialSummoned>().Count());
        Assert.Single(events.OfType<DeckShuffled>());
        Assert.Null(e.State.PendingChain);
    }

    [Fact]
    public void AnAnyNumberSummonCanStopAfterItsMinimum()
    {
        var e = Attacked(Hyena, Stacked(Hyena, Hyena, Hyena));
        ToTrigger(e); Activate(e);
        Pick(e, 0, 0, BattlePosition.FaceUpAttack);
        var events = Ok(e, new FinishDefinitionSummon(PlayerId.One, SummonChoice(e).DecisionId)).Events;
        Assert.Single(events.OfType<MonsterSpecialSummoned>());
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Deck, c => c.GameCardId == Hyena);
    }

    [Fact]
    public void AnOptionalAnyNumberSummonFollowsItsFirstStepAndMayBeSkipped()
    {
        var e = Attacked(Germ, Stacked(Germ, Germ));
        ToTrigger(e); Activate(e);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.True(SummonChoice(e).CanFinish);
        Assert.Equal([BattlePosition.FaceUpAttack], SummonChoice(e).PositionOptions);
        var events = Ok(e, new FinishDefinitionSummon(PlayerId.One, SummonChoice(e).DecisionId)).Events;
        Assert.Empty(events.OfType<MonsterSpecialSummoned>());
        Assert.Empty(events.OfType<DeckShuffled>());
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void AFixedFaceDownSummonIsPlacedFaceDown()
    {
        var e = Attacked(Momonga, Stacked(Momonga, Momonga));
        ToTrigger(e);
        Assert.Contains(Activate(e), ev => ev is LifePointsGained { Amount: 1000 });
        Assert.Equal([BattlePosition.FaceDownDefense], SummonChoice(e).PositionOptions);
        Pick(e, 0, 0, BattlePosition.FaceDownDefense);
        Assert.Equal(BattlePosition.FaceDownDefense, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
    }

    [Fact]
    public void TheAdapterOffersFinishAndThePolicyKeepsSummoning()
    {
        var e = Attacked(Germ, Stacked(Germ, Germ));
        ToTrigger(e); Activate(e);
        var source = new DuelActionSource(e, Catalog());
        var view = source.Observe(PlayerId.One);
        Assert.Contains(view.Actions, a => a.Kind == ActionKind.FinishChoice);
        Assert.Equal(ActionKind.ChooseCard, new TagForceSpecial.AI.DeterministicPolicy(7).Choose(view)!.Kind);
    }

    [Fact]
    public void SummonRestrictedMonstersCanBeSearchedButNotSummonedFromTheDeck()
    {
        var floater = Attacked(Tomato, Stacked(Tomato, Restricted, DarkLow));
        ToTrigger(floater); Activate(floater);
        Assert.Equal([DarkLow], SummonChoice(floater).Options.Select(o => o.Card.GameCardId));

        var none = Attacked(Tomato, Stacked(Tomato, Restricted));
        Assert.Contains(ToTrigger(none), ev => ev is TriggerUnavailable);

        var search = Attacked(Sangan, Stacked(Sangan, Restricted));
        ToTrigger(search); Activate(search);
        Assert.Contains(Restricted, Options(search));
    }

    private static TriggerEffectProfile DestroyOnSummon(int lifePointCost = 0, IEnumerable<uint>? discard = null, bool faceDown = true) =>
        new(new EffectDefinition($"destroy-on-summon-{lifePointCost}", LightLow, EffectKind.MonsterTrigger, [new DestroyTargetsStep()], fieldTargets: [new TriggerTargetedMonster()]),
            MonsterEventKind.NormalSummoned, EffectRelation.Self, TriggerRequirement.Mandatory, lifePointCost: lifePointCost, discardCardIds: discard,
            target: new(EffectRelation.Opponent, requireFaceUpAtResolution: !faceDown, allowFaceDown: faceDown));

    /// <summary>Player One sets Filler; Player Two summons the trigger's source.</summary>
    private static DuelEngine SummonedOverSet(TriggerEffectProfile trigger, uint[]? first = null)
    {
        var e = Engine(first ?? Deck(Filler), Deck(LightLow), triggers: [trigger]);
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, Filler), 0)); Next(e);
        Play(e, LightLow);
        return e;
    }

    private static void Order(DuelEngine e)
    {
        var batch = e.State.EventEffects.PendingTriggers!;
        Ok(e, new ChooseTriggerOrder(batch.Player, batch.DecisionId, batch.Options.Select(t => t.Id).ToArray()));
    }

    [Fact]
    public void ADefinitionTriggerDestroysItsChosenTargetAndHidesItInTheAdapter()
    {
        var e = SummonedOverSet(DestroyOnSummon());
        Order(e);
        var source = new DuelActionSource(e, Catalog());
        var view = source.Observe(PlayerId.Two);
        Assert.Equal("TriggerCommitment", view.Decision!.Kind);
        Assert.Empty(view.Decision.Cards);
        var action = Assert.Single(view.Actions);
        Assert.Equal(ActionKind.ChooseTarget, action.Kind);
        Assert.Null(action.CardInstanceId);
        Assert.True(source.Execute(PlayerId.Two, view.Revision, action.Token).Accepted);
        Assert.Contains(Resolve(e), ev => ev is MonsterDestroyed { Player: PlayerId.One, Zone: 0 });
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void ADefinitionTriggerPaysItsCostsBeforeTargeting()
    {
        var e = SummonedOverSet(DestroyOnSummon(lifePointCost: 500, discard: [Filler]));
        S.DrainEffects(e);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Graveyard, c => c.GameCardId == Filler);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void ADefinitionTriggerNeedsALegalTarget()
    {
        var e = SummonedOverSet(DestroyOnSummon(faceDown: false));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Null(e.State.PendingChain);
    }

    [Fact]
    public void ADefinitionTriggerDoesNothingWhenItsTargetLeaves()
    {
        var e = Engine(Deck(Filler, Evacuation), Deck(LightLow), triggers: [DestroyOnSummon()]);
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, Filler), 0));
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Evacuation), 0)); Next(e);
        Play(e, LightLow);
        Order(e);
        var target = e.State.EventEffects.PendingCommitment!;
        var chosen = target.TargetOptions.Single();
        Ok(e, new ChooseTriggerMonsterTarget(PlayerId.Two, target.DecisionId, chosen.Player, chosen.Zone, chosen.Card.InstanceId));
        Ok(e, new ActivateChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId, 0));
        var decision = e.State.PendingDecision!;
        var option = decision.FieldTargetOptions!.Single(o => o.Player == PlayerId.One && o.Zone == 0);
        Ok(e, new ChooseDecisionFieldTarget(PlayerId.One, decision.DecisionId, option.Player, option.Location, option.Zone, option.Card.InstanceId));
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.TargetUnavailable });
        Assert.DoesNotContain(events, ev => ev is MonsterDestroyed);
    }

    [Fact]
    public void RegistrationRejectsInvalidMonsterTriggers()
    {
        var search = new EffectDefinition("search", Sangan, EffectKind.MonsterTrigger, [new AddFromDeckToHandStep(new MonsterTraitFilter(MonsterStat.Attack, 1500))]);
        var profile = new TriggerEffectProfile(search, MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf);
        var rules = MonsterTriggerFamily.Rules;
        uint[] deck = [.. Enumerable.Repeat(Sangan, 40)];
        var catalog = new CardCatalog(new(null, "synthetic"), MonsterTriggerFamily.Cards.Select(c => new CardDefinition(c.CardId, "x", "", new(1000, 1000, 4))));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, experimentalTriggerEffects: [profile]));
        Assert.Throws<ArgumentException>(() => new DuelEngine(catalog, rules, deck, deck, effectDefinitions: [search], shuffleSeed: 1));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("cost", Sangan, EffectKind.MonsterTrigger, [new DrawCardsStep(1)], cost: new DiscardCost()));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(new EffectDefinition("spell", Sangan, EffectKind.NormalSpell, [new DrawCardsStep(1)]),
            MonsterEventKind.Destroyed, EffectRelation.Self, TriggerRequirement.Mandatory));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(search, MonsterEventKind.NormalSummoned, EffectRelation.Self,
            TriggerRequirement.Mandatory, cause: TriggerEventCause.ByBattle));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(search, MonsterEventKind.TributedForSummon, EffectRelation.Self, TriggerRequirement.Mandatory));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonsterTraitFilter(MonsterStat.Attack, 100, 200));
        // A chosen-target definition and a target policy go together.
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(search, MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Mandatory,
            TriggerSourceLocation.GraveyardSelf, target: new(EffectRelation.Opponent)));
        Assert.Throws<ArgumentException>(() => new TriggerEffectProfile(new EffectDefinition("t", Sangan, EffectKind.MonsterTrigger, [new DestroyTargetsStep()],
            fieldTargets: [new TriggerTargetedMonster()]), MonsterEventKind.SentToGraveyard, EffectRelation.Self, TriggerRequirement.Mandatory, TriggerSourceLocation.GraveyardSelf));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("t", Sangan, EffectKind.NormalTrap, [new DestroyTargetsStep()], fieldTargets: [new TriggerTargetedMonster()]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialSummonStep(SpecialSummonSources.Deck, new AnyMonsterFilter(), minCount: 0));
    }
}
