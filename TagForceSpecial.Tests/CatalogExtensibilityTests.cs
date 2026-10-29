using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

public sealed class CatalogExtensibilityTests
{
    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        return result;
    }

    private static void EnterMain(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(player));
        Ok(engine, new ChangePhase(player, DuelPhase.Main1));
    }

    private static void Next(DuelEngine engine)
    {
        var player = engine.State.ActivePlayer;
        Ok(engine, new ChangePhase(player, DuelPhase.End));
        Ok(engine, new EndTurn(player));
        EnterMain(engine);
    }

    private static void PassBoth(DuelEngine engine)
    {
        for (int i = 0; i < 2; i++)
        {
            var c = engine.State.PendingChain!;
            Ok(engine, new PassChainResponse(c.Responder, c.DecisionId));
        }
    }

    private static void Resolve(DuelEngine engine) =>
        Ok(engine, new ResolveNextChainLink(engine.State.ActivePlayer, engine.State.PendingChain!.DecisionId));

    private static void Pay(DuelEngine engine)
    {
        var p = engine.State.PendingDecision!;
        Ok(engine, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
    }

    [Fact]
    public void ASelectionCanUseTheEndOfACatalogLargerThanThePspIdSpace()
    {
        const int count = 20_000;
        var ids = Enumerable.Range(100_000, count).Select(i => (uint)i).ToArray();
        var catalog = new CardCatalog(new(null, "large-authored-v1"), ids.Select(id =>
            new CardDefinition(id, $"Authored {id}", "", new(1, 1, 4))));
        var flow = new DecisionSequence("whole-catalog", [DecisionStep.Card(DecisionCardSource.Hand, ids)]);
        var deck = Enumerable.Repeat(ids[^1], 40).ToArray();
        var engine = new DuelEngine(catalog, new Dictionary<uint, DuelCardRules>(), deck, deck, decisionSequences: [flow]);
        EnterMain(engine);
        Ok(engine, new BeginDecisionSequence(PlayerId.One, flow.Id));
        Assert.Equal(count, flow.Steps[0].AllowedCardIds.Count);
        var p = engine.State.PendingDecision!;
        Assert.Equal(5, p.CardOptions.Count);
        Assert.All(p.CardOptions, c => Assert.Equal(ids[^1], c.GameCardId));
        Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, p.CardOptions[^1].InstanceId));
        Assert.True(engine.State.ResearchSliceComplete);
    }

    [Theory]
    [InlineData(9999, 7777, 2222)]
    [InlineData(7, 9, 2)]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    [InlineData(0, int.MaxValue, int.MaxValue)]
    public void AuthoredStatsAndFullWidthIdsReachBattleWithoutPackingOrQuantization(int attack, int opposingAttack, int damage)
    {
        const uint attackerId = uint.MaxValue, defenderId = uint.MaxValue - 1;
        var catalog = new CardCatalog(new(null, "custom-combat-v1"),
            [new(attackerId, "Attacker", "", new(attack, 1, 4)),
             new(defenderId, "Defender", "", new(opposingAttack, 1, 4))]);
        var rules = catalog.Cards.ToDictionary(c => c.GameCardId, _ => new DuelCardRules(CardCategory.Monster,
            AllowsNormalSummon: true, AllowsBasicBattle: true));
        var engine = new DuelEngine(catalog, rules, Enumerable.Repeat(defenderId, 40), Enumerable.Repeat(attackerId, 40));
        EnterMain(engine);
        Ok(engine, new NormalSummon(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        Next(engine);
        Ok(engine, new NormalSummon(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Ok(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(engine, new DeclareAttack(PlayerId.Two, 0, 0));
        var dealt = Assert.Single(result.Events.OfType<BattleDamageDealt>());
        Assert.Equal(damage, dealt.Damage);
        Assert.Equal(attack > opposingAttack ? PlayerId.One : PlayerId.Two, dealt.Player);
        Assert.Equal(Math.Max(0, 8000 - damage), dealt.LifePointsAfter);
        if (damage >= 8000)
        {
            Assert.Equal(attack > opposingAttack ? PlayerId.Two : PlayerId.One, result.State.Outcome!.Winner);
            Assert.Empty(result.Events.OfType<MonsterDestroyed>()); // Existing LP-defeat ordering.
        }
    }

    [Fact]
    public void ImportedAndAdditionalCardsUseHighIdCostsAndResponsesInTheSameDuel()
    {
        const uint spell = uint.MaxValue, cost = 70_000, response = 80_000;
        var original = new CardCatalog(new(new string('a', 64), "retained-import-profile"),
            [new(1, "Imported monster", "", new(1000, 1000, 4), new(0, 0, 0, 0, 0, 0, 0), new(1, 1, 4u << 11, 0))]);
        var additions = new CardCatalog(new(null, "additional-v1"),
            [new(spell, "Custom Spell", "", new(0, 0, 0)), new(cost, "Custom monster", "", new(9999, 5111, 4)),
             new(response, "Custom response", "", new(0, 0, 0))]);
        var catalog = original.Merge(additions);
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [1] = new(CardCategory.Monster, AllowsNormalSummon: true),
            [cost] = new(CardCategory.Monster, AllowsNormalSummon: true),
            [spell] = new(CardCategory.Spell), [response] = new(CardCategory.Trap, AllowsSpellTrapSet: true)
        };
        uint[] first = [spell, cost, cost, 1, 1, .. Enumerable.Repeat(cost, 35)];
        uint[] second = [response, cost, cost, 1, 1, .. Enumerable.Repeat(cost, 35)];
        var engine = new DuelEngine(catalog, rules, first, second,
            experimentalActivationContracts: [new("custom-spell", spell, [cost])],
            experimentalChainEffects: [new("custom-spell", spell, ChainOperation.DestroyOpposingFaceUpMonsters)],
            experimentalChainResponses: [new("custom-negate", response, ChainOperation.NegateEffect, ChainSpellSpeed.Two, [cost])]);
        EnterMain(engine);
        Next(engine);
        Ok(engine, new SetSpellTrap(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Ok(engine, new NormalSummon(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == 1).InstanceId, 0));
        Next(engine);
        Ok(engine, new ActivateSpell(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        Pay(engine);
        var chain = engine.State.PendingChain!;
        Assert.Equal(spell, chain.Links[0].Activation.Spell.GameCardId);
        Ok(engine, new ActivateChainResponse(PlayerId.Two, chain.DecisionId, 0));
        Pay(engine);
        PassBoth(engine);
        Resolve(engine);
        Resolve(engine);
        Assert.Equal(new uint[] { cost, spell }, engine.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
        Assert.Equal(new uint[] { cost, response }, engine.State.GetPlayer(PlayerId.Two).Graveyard.Select(c => c.GameCardId));
        Assert.Equal(1u, engine.State.GetPlayer(PlayerId.Two).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(original[1].SourceRow, catalog[1].SourceRow);
        Assert.Equal(original.Source, catalog[1].Source);
        Assert.Equal(additions.Source, catalog[spell].Source);
        Assert.Null(catalog[spell].SourceRow);
    }

    [Fact]
    public void AdditionalTokenIdsRemainCatalogIdsThroughoutResolutionAndCleanup()
    {
        const uint spell = uint.MaxValue, tokenSource = 60_000;
        uint[] tokenIds = [90_001, 90_002, 90_003, 90_004];
        var ids = new uint[] { spell, tokenSource, 1 }.Concat(tokenIds).ToArray();
        var catalog = new CardCatalog(new(null, "custom-tokens-v1"), ids.Select(id =>
            new CardDefinition(id, $"Custom {id}", "", new(0, 0, 1))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(id is spell or tokenSource ? CardCategory.Spell : CardCategory.Monster,
            AllowsSpellTrapSet: id == tokenSource));
        var engine = new DuelEngine(catalog, rules,
            [spell, .. Enumerable.Repeat(1u, 39)], [tokenSource, .. Enumerable.Repeat(1u, 39)],
            experimentalActivationContracts: [new("custom-target", spell, requiresSpellTrapTarget: true)],
            experimentalChainEffects: [new("custom-target", spell, ChainOperation.DestroyBoundSpellTrap),
                new("custom-tokens", tokenSource, ChainOperation.SummonFourTokens, tokenIds)]);
        EnterMain(engine);
        Next(engine);
        Ok(engine, new SetSpellTrap(PlayerId.Two, engine.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 1));
        Next(engine);
        Ok(engine, new ActivateSpell(PlayerId.One, engine.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        var p = engine.State.PendingDecision!;
        var target = Assert.Single(p.TargetOptions);
        Ok(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Ok(engine, new ActivateChainResponse(PlayerId.Two, engine.State.PendingChain!.DecisionId, 1));
        PassBoth(engine);
        Resolve(engine);
        Resolve(engine);
        Assert.Equal(tokenIds, engine.State.GetPlayer(PlayerId.Two).MonsterZones.OfType<FieldMonster>().Select(m => m.Card.GameCardId));
        Assert.Equal(tokenSource, Assert.Single(engine.State.GetPlayer(PlayerId.Two).Graveyard).GameCardId);
        Assert.Equal(spell, Assert.Single(engine.State.GetPlayer(PlayerId.One).Graveyard).GameCardId);
    }
}
