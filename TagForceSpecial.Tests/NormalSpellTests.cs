using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic command contracts using independently authored printed values, not PSP observations.</summary>
public sealed class NormalSpellTests
{
    internal static CardCatalog Catalog()
    {
        // Independent authored fixture values; production definitions now come exclusively from Cards packages.
        (uint Id, string Name, int Attack, int Defense, uint Level)[] rows =
        [
            (4012, "Feral Imp", 1300, 1400, 4), (4030, "Skull Servant", 300, 200, 1),
            (4032, "Battle Ox", 1700, 1000, 4), (4033, "Beaver Warrior", 1200, 1500, 4),
            (4071, "Silver Fang", 1200, 800, 3), (4084, "Axe Raider", 1700, 1150, 4),
            (5644, "Luster Dragon", 1900, 1600, 4), (6960, "Gene-Warped Warwolf", 2000, 100, 4),
            (4345, "Red Medicine", 0, 0, 0), (4348, "Dian Keto the Cure Master", 0, 0, 0),
            (4349, "Sparks", 0, 0, 0), (4350, "Hinotama", 0, 0, 0),
            (4352, "Ookazi", 0, 0, 0), (4840, "Blue Medicine", 0, 0, 0)
        ];
        return new(new(null, "authored-preview-test-inputs"), rows.Select((c, i) => new CardDefinition(c.Id, c.Name,
            "Synthetic test description", new(c.Attack, c.Defense, c.Level), SourceRow: new(i + 1, c.Id, 0, 0),
            Kind: TfspCardMetadata.Reviewed[c.Id].Kind, Effects: TfspCardMetadata.Reviewed[c.Id].Effects))
            .Append(new(90001, "Synthetic response", "", new(0, 0, 0))));
    }

    internal static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, $"{command}: {result.Error}");
        return result;
    }

    internal static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, result.State);
        Assert.Same(before, engine.State);
        Assert.Empty(result.Events);
    }

    internal static void EnterMain(DuelEngine engine)
    {
        Ok(engine, new ResolveDrawStep(engine.State.ActivePlayer));
        Ok(engine, new ChangePhase(engine.State.ActivePlayer, DuelPhase.Main1));
    }

    internal static void Next(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        if (engine.State.Phase == DuelPhase.Battle) Ok(engine, new ChangePhase(actor, DuelPhase.Main2));
        Ok(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
        Ok(engine, new EndTurn(actor));
        EnterMain(engine);
    }

    internal static List<DuelEvent> Resolve(DuelEngine engine)
    {
        List<DuelEvent> events = [];
        while (engine.State.PendingChain is { } chain && engine.State.Outcome is null)
        {
            DuelCommand command = chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(engine.State.ActivePlayer, chain.DecisionId);
            events.AddRange(Ok(engine, command).Events);
        }
        return events;
    }

    private static DuelEngine Engine(uint card, ChainOperation? response = null)
    {
        var rules = PlayablePreview.Rules.ToDictionary();
        rules[90001] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        var deck = new uint[] { card, 4012, 4030, 4032, 4033 }.Concat(Enumerable.Repeat(card, 35));
        return new(Catalog(), rules, deck, new uint[] { 90001, 4012, 4030, 4032, 4033 }.Concat(Enumerable.Repeat(4012u, 35)),
            effectDefinitions: PlayablePreview.Definitions,
            experimentalChainResponses: response is { } operation ? [new("synthetic-response", 90001, operation, ChainSpellSpeed.Three)] : []);
    }

    public static IEnumerable<object[]> Spells() => new object[][]
    {
        [4345u, 500, true], [4348u, 1000, true], [4349u, 200, false],
        [4350u, 500, false], [4352u, 800, false], [4840u, 400, true]
    };

    [Theory]
    [MemberData(nameof(Spells))]
    public void EachSpellFromHandAndSetHasCorrectAmountIdentityAndCleanup(uint id, int amount, bool recovery)
    {
        foreach (bool set in new[] { false, true })
        foreach (bool main2 in new[] { false, true })
        {
            var e = Engine(id); EnterMain(e);
            if (main2) { Next(e); Next(e); Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle)); Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main2)); }
            var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
            if (set) Ok(e, new SetSpellTrap(PlayerId.One, card.InstanceId, 2));
            var before = e.State;
            var result = Ok(e, set ? new ActivateSetSpell(PlayerId.One, 2) : new ActivateSpell(PlayerId.One, card.InstanceId, 2));
            Assert.Equal(new[] { typeof(SpellActivated), typeof(ActivationCommitted), typeof(ChainLinkAdded), typeof(ChainResponseRequested) }, result.Events.Select(x => x.GetType()));
            Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
            Assert.Equal(card, e.State.PendingChain!.Links[0].Activation.Spell);
            Assert.Equal(ChainSpellSpeed.One, e.State.PendingChain.Links[0].SpellSpeed);
            Assert.Null(e.State.PendingChain.Links[0].Activation.Target);
            var events = Resolve(e);
            Assert.Equal(8000 + (recovery ? amount : 0), e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(8000 - (recovery ? 0 : amount), e.State.GetPlayer(PlayerId.Two).LifePoints);
            Assert.Equal(card, Assert.Single(events.OfType<SpellSentToGraveyard>()).Card);
            Assert.Contains(card, e.State.GetPlayer(PlayerId.One).Graveyard);
            Assert.Null(e.State.GetPlayer(PlayerId.One).SpellTrapFields[2]);
            Assert.Null(e.State.PendingChain);
            Assert.False(e.State.ResearchSliceComplete);
            Assert.Equal(8000, before.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(8000, before.GetPlayer(PlayerId.Two).LifePoints);
            Assert.True(set ? before.GetPlayer(PlayerId.One).SpellTrapFields[2] is { IsFaceUp: false }
                : before.GetPlayer(PlayerId.One).Hand.Contains(card));
            Assert.True(events.FindIndex(x => x is LifePointsGained or EffectDamageDealt) < events.FindIndex(x => x is ChainLinkResolved));
            Assert.True(events.FindIndex(x => x is ChainLinkResolved) < events.FindIndex(x => x is SpellSentToGraveyard));
            Assert.IsType<ChainCompleted>(events[^1]);
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        }
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void EachSpellHonorsBothNegationKinds(uint id, int amount, bool recovery)
    {
        _ = amount; _ = recovery;
        foreach (var operation in new[] { ChainOperation.NegateActivation, ChainOperation.NegateEffect })
        {
            var e = Engine(id, operation); EnterMain(e); Next(e);
            Ok(e, new SetSpellTrap(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
            Next(e);
            Ok(e, new ActivateSpell(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id).InstanceId, 0));
            Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
            var events = Resolve(e);
            Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
            Assert.Empty(events.OfType<EffectDamageDealt>());
            Assert.Empty(events.OfType<LifePointsGained>());
            Assert.Single(events.OfType<ChainLinkSkipped>());
            Assert.Single(events.OfType<SpellSentToGraveyard>());
            Assert.False(e.State.ResearchSliceComplete);
        }
    }

    [Fact]
    public void SourceDestructionDoesNotNegateAndDoesNotDoubleSendAnySpell()
    {
        foreach (var row in Spells())
        {
            uint id = (uint)row[0]; int amount = (int)row[1]; bool recovery = (bool)row[2];
            var e = Engine(id, ChainOperation.DestroyBoundSpellTrap); EnterMain(e); Next(e);
            Ok(e, new SetSpellTrap(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
            Next(e);
            var spell = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
            Ok(e, new ActivateSpell(PlayerId.One, spell.InstanceId, 0));
            Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
            var decision = e.State.PendingDecision!;
            Ok(e, new ChooseDecisionTarget(PlayerId.Two, decision.DecisionId, PlayerId.One, 0, spell.InstanceId));
            var events = Resolve(e);
            Assert.Single(events.OfType<SpellTrapDestroyed>());
            Assert.Empty(events.OfType<SpellSentToGraveyard>());
            Assert.Single(e.State.GetPlayer(PlayerId.One).Graveyard, c => c == spell);
            Assert.Equal(8000 + (recovery ? amount : 0), e.State.GetPlayer(PlayerId.One).LifePoints);
            Assert.Equal(8000 - (recovery ? 0 : amount), e.State.GetPlayer(PlayerId.Two).LifePoints);
        }
    }

    [Fact]
    public void InvalidPhaseActorSourceZoneAndStaleResponsesRejectAtomically()
    {
        var e = Engine(4352); var card = e.State.GetPlayer(PlayerId.One).Hand[0];
        Reject(e, new ActivateSpell(PlayerId.One, card.InstanceId, 0), DuelError.WrongPhase);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.WrongPhase);
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.Two, card.InstanceId, 0), DuelError.NotActivePlayer);
        Reject(e, new ActivateSpell(PlayerId.One, card.InstanceId, 5), DuelError.InvalidSpellTrapZone);
        Reject(e, new ActivateSetSpell(PlayerId.One, -1), DuelError.InvalidSpellTrapZone);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.UnsupportedSpellEffect);
        Reject(e, new ActivateSpell(PlayerId.One, -1, 0), DuelError.CardNotInHand);
        Reject(e, new ActivateSpell(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[1].InstanceId, 0), DuelError.UnsupportedSpellEffect);
        Ok(e, new SetSpellTrap(PlayerId.One, card.InstanceId, 0));
        var second = e.State.GetPlayer(PlayerId.One).Hand[0];
        Reject(e, new ActivateSpell(PlayerId.One, second.InstanceId, 0), DuelError.UnsupportedSpellEffect);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Reject(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId - 1), DuelError.StaleChainDecision);
        Reject(e, new PassChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId), DuelError.NotChainResponder);
        Reject(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId), DuelError.ChainPending);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.ChainPending);
    }

    [Fact]
    public void RepeatedSpellInstancesAndOccupiedSlotsStayDistinct()
    {
        var e = Engine(4352); EnterMain(e);
        var card = e.State.GetPlayer(PlayerId.One).Hand[0];
        Ok(e, new SetSpellTrap(PlayerId.One, card.InstanceId, 0));
        Next(e); Next(e);
        var next = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == 4352);
        Reject(e, new ActivateSpell(PlayerId.One, next.InstanceId, 0), DuelError.SpellTrapZoneOccupied);
        Ok(e, new ActivateSpell(PlayerId.One, next.InstanceId, 1)); Resolve(e);
        Assert.Equal(card, e.State.GetPlayer(PlayerId.One).SpellTrapFields[0]!.Card);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Assert.Equal(6400, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(new[] { next, card }, e.State.GetPlayer(PlayerId.One).Graveyard);
    }

    [Fact]
    public void LethalEffectDamageClampsLifePointsAndStopsImmediately()
    {
        var e = Engine(4352); EnterMain(e);
        // Legal turns/draws supply ten distinct copies; no state hydration or LP patch.
        for (int i = 0; i < 10; i++)
        {
            var spell = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == 4352);
            Ok(e, new ActivateSpell(PlayerId.One, spell.InstanceId, 0));
            var events = Resolve(e);
            if (i < 9) { Next(e); Next(e); continue; }
            Assert.Equal(new DuelOutcome(PlayerId.One, PlayerId.Two, DuelEndReason.LifePointsDepleted), e.State.Outcome);
            Assert.Equal(0, e.State.GetPlayer(PlayerId.Two).LifePoints);
            Assert.IsType<DuelEnded>(events[^1]);
            Assert.Empty(events.OfType<ChainCompleted>());
            Assert.Empty(events.OfType<SpellSentToGraveyard>());
            Reject(e, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DuelFinished);
        }
    }

    [Fact]
    public void DefinitionsFailClosedForBadStepsKindsCategoriesAndDuplicateBindings()
    {
        EffectDefinition Burn(string id, uint card, int amount = 800) => new(id, card, EffectKind.NormalSpell, [new DealDamageStep(amount)]);
        Assert.Throws<ArgumentException>(() => Burn("x", 4352, 0));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 4352, EffectKind.NormalSpell, [new GainLifePointsStep(-1)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 4352, EffectKind.NormalSpell, []));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 4352, EffectKind.NormalSpell, [null!]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 4352, EffectKind.NormalSpell,
            Enumerable.Repeat<EffectStep>(new DealDamageStep(1), 33)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectDefinition("x", 4352, (EffectKind)99, [new DealDamageStep(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Burn("x", 0));
        var p = Burn("x", 4352);
        DuelEngine Build(IEnumerable<EffectDefinition> definitions, IReadOnlyDictionary<uint, DuelCardRules>? rules = null) =>
            new(Catalog(), rules ?? PlayablePreview.Rules, Enumerable.Repeat(4012u, 40), Enumerable.Repeat(4012u, 40), effectDefinitions: definitions);
        Assert.Throws<ArgumentException>(() => Build([p, p]));
        Assert.Throws<ArgumentException>(() => Build([p, Burn("x", 4350, 500)]));
        Assert.Throws<ArgumentException>(() => Build([Burn("x", 4012, 500)]));
        // A Trap kind needs Trap card rules (4352 is a Spell); a shuffle step needs a seeded engine.
        Assert.Throws<ArgumentException>(() => Build([new("t", 4352, EffectKind.NormalTrap, [new DealDamageStep(800)])]));
        Assert.Throws<ArgumentException>(() => Build([new("s", 4352, EffectKind.NormalSpell, [new ShuffleDeckStep()])]));
        var bad = PlayablePreview.Rules.ToDictionary(); bad[4352] = new(CardCategory.Spell);
        Assert.Throws<ArgumentException>(() => Build([p], bad));
    }

    [Fact]
    public void MultiStepDefinitionsResolveInOrderAndStopAtTerminalDamage()
    {
        // Synthetic two-step Normal Spell on the 4352 card slot: gain then burn; a second burn never runs after defeat.
        var twoStep = new EffectDefinition("synthetic-two-step", 4352, EffectKind.NormalSpell,
            [new GainLifePointsStep(300), new DealDamageStep(8000), new DealDamageStep(100)]);
        var e = new DuelEngine(Catalog(), PlayablePreview.Rules, new uint[] { 4352 }.Concat(Enumerable.Repeat(4012u, 39)),
            Enumerable.Repeat(4012u, 40), effectDefinitions: [twoStep]);
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == 4352).InstanceId, 0));
        var events = Resolve(e);
        Assert.Equal(300, Assert.Single(events.OfType<LifePointsGained>()).Amount);
        Assert.Equal(8000, Assert.Single(events.OfType<EffectDamageDealt>()).Damage);
        Assert.IsType<DuelEnded>(events[^1]);
        Assert.Equal(8300, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(0, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.DoesNotContain(events, ev => ev is ChainCompleted or SpellSentToGraveyard);
    }
}

