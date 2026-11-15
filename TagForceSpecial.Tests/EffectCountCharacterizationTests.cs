using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Current count semantics under legal public commands, independently of the count implementation's structure.</summary>
public sealed class EffectCountCharacterizationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> OccupancyCases()
    {
        foreach (var controller in new[] { PlayerId.One, PlayerId.Two })
            foreach (var (count, expected) in new[]
            {
                (EffectCount.None, 1), (EffectCount.OwnMonsters, 2), (EffectCount.OwnFaceUpMonsters, 1),
                (EffectCount.OpponentMonsters, 2), (EffectCount.AllMonsters, 4),
                (EffectCount.OpponentHand, 4), (EffectCount.OpponentHandAndField, 7),
                (EffectCount.OpponentGraveyard, 2), (EffectCount.OpponentBanished, 2)
            }) yield return [controller, count, expected];
    }

    private static EffectDefinition Gain(EffectCount count, CardFilter? filter = null, int amount = 100) =>
        new("count", Probe, EffectKind.NormalSpell, [new GainLifePointsStep(amount, PerCount: count) { CountFilter = filter }]);

    [Theory]
    [MemberData(nameof(OccupancyCases))]
    public void CountsOccupancyAndInstancesFromEitherController(PlayerId controller, EffectCount count, int expected)
    {
        EffectStep[]? preparation = count switch
        {
            EffectCount.OpponentGraveyard => [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent)],
            EffectCount.OpponentBanished => [new BanishAllStep(TargetSide.Opponent)],
            _ => null
        };
        var s = new CountQueryScenario(Gain(count), controller, preparation);
        s.Populate();
        if (preparation is not null) { s.Cast(Preparation); s.Resolve(); }
        var before = s.Engine.State;
        var random = before.Random;
        s.Cast(); s.Resolve();
        var gained = Assert.Single(s.Events.OfType<LifePointsGained>());
        Assert.Equal(expected * 100, gained.Amount);
        Assert.Equal(controller, gained.Player);
        Assert.Equal(8000 + expected * 100, s.Engine.State.GetPlayer(controller).LifePoints);
        Assert.Equal(8000, before.GetPlayer(controller).LifePoints);
        Assert.Equal(random, s.Engine.State.Random);
    }

    [Theory]
    [InlineData(EffectCount.OwnMonsters)]
    [InlineData(EffectCount.OwnFaceUpMonsters)]
    [InlineData(EffectCount.OpponentMonsters)]
    [InlineData(EffectCount.AllMonsters)]
    [InlineData(EffectCount.OpponentGraveyard)]
    [InlineData(EffectCount.OpponentBanished)]
    public void EmptyCountRejectsBeforeAnUnpayableCostWithoutMutation(EffectCount count)
    {
        var definition = new EffectDefinition("count", Probe, EffectKind.NormalSpell,
            [new GainLifePointsStep(100, PerCount: count)], cost: new PayLifePointsCost(9000));
        var s = new CountQueryScenario(definition); s.EnterMain();
        var player = s.Engine.State.ActivePlayer;
        Reject(s.Engine, new ActivateSpell(player, s.Engine.State.GetPlayer(player).Hand.First(c => c.GameCardId == Probe).InstanceId, 0),
            DuelError.ActivationConditionNotMet);
    }

    [Fact]
    public void NestedFiltersCountDuplicateInstancesAndIgnoreNonmatchingCards()
    {
        CardFilter filter = new AllOfFilter([
            new AnyOfFilter([new CardIdFilter([FirstMonster]), new CardIdFilter([SecondMonster])]),
            new NotFilter(new CardIdFilter([FirstMonster]))]);
        var s = new CountQueryScenario(Gain(EffectCount.AllMonsters, filter));
        s.Populate(); s.Cast(); s.Resolve();
        Assert.Equal(200, Assert.Single(s.Events.OfType<LifePointsGained>()).Amount);
    }

    [Fact]
    public void AFilterWithNoMatchesRejectsEvenWhenTheZoneIsOccupied()
    {
        var s = new CountQueryScenario(Gain(EffectCount.AllMonsters, new NotFilter(new AnyMonsterFilter())));
        s.Populate();
        Reject(s.Engine, new ActivateSpell(PlayerId.One,
            s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Probe).InstanceId, 0), DuelError.ActivationConditionNotMet);
    }

    [Theory]
    [InlineData(EffectCount.OpponentGraveyard, 200)]
    [InlineData(EffectCount.OpponentBanished, 0)]
    public void GeminiNormalTreatmentAppliesOnlyToTheGraveyard(EffectCount count, int expected)
    {
        EffectStep preparation = count == EffectCount.OpponentGraveyard
            ? new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent) : new BanishAllStep(TargetSide.Opponent);
        var s = new CountQueryScenario(Gain(count, new NormalMonsterFilter([])), preparation: [preparation]);
        s.Populate(); s.Cast(Preparation); s.Resolve();
        if (expected == 0)
        {
            Reject(s.Engine, new ActivateSpell(PlayerId.One,
                s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Probe).InstanceId, 0), DuelError.ActivationConditionNotMet);
            Assert.Empty(s.Events.OfType<LifePointsGained>());
        }
        else
        {
            s.Cast(); s.Resolve();
            Assert.Equal(expected, Assert.Single(s.Events.OfType<LifePointsGained>()).Amount);
        }
    }

    [Fact]
    public void ResolutionCountsAgainAfterAnEarlierStepChangesTheField()
    {
        var definition = new EffectDefinition("count", Probe, EffectKind.NormalSpell,
            [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Own), new GainLifePointsStep(100, PerCount: EffectCount.OwnMonsters)]);
        var s = new CountQueryScenario(definition); s.Populate(); s.Cast(); s.Resolve();
        Assert.Empty(s.Events.OfType<LifePointsGained>());
        Assert.Equal(2, s.Events.OfType<MonsterDestroyed>().Count());
    }

    [Fact]
    public void FilterReadsTheFaceDownChangeEarlierInTheSameResolution()
    {
        var definition = new EffectDefinition("count", Probe, EffectKind.NormalSpell,
            [new FlipTargetsFaceDownStep(), new GainLifePointsStep(100, PerCount: EffectCount.OwnMonsters) { CountFilter = new FaceUpAttackFilter() }],
            fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp)]);
        var s = new CountQueryScenario(definition, output: output);
        s.Populate(); s.Cast(); s.Resolve();
        Assert.Empty(s.Events.OfType<LifePointsGained>());
        Assert.Equal(BattlePosition.FaceDownDefense, s.Engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        Assert.Null(s.Engine.State.PendingDecision);
        Assert.Null(s.Engine.State.PendingChain);
    }

    [Fact]
    public void DeclaredCountUsesTheDeclaredNameAtResolution()
    {
        var definition = new EffectDefinition("count", Probe, EffectKind.NormalSpell,
            [new GainLifePointsStep(100, PerCount: EffectCount.AllMonsters) { CountFilter = new DeclaredFilter() }])
            .WithDeclaration(DeclarationKind.MonsterCardName);
        var s = new CountQueryScenario(definition, output: output);
        s.Populate(); s.Cast(); s.Resolve((int)FirstMonster);
        Assert.Equal(200, Assert.Single(s.Events.OfType<LifePointsGained>()).Amount);
    }

    [Fact]
    public void ScaledGainSaturatesWithoutOverflowAndDamageEndsTheDuel()
    {
        var gain = new CountQueryScenario(Gain(EffectCount.AllMonsters, amount: int.MaxValue));
        gain.Populate(); gain.Cast(); gain.Resolve();
        Assert.Equal(int.MaxValue, gain.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(int.MaxValue - 8000, Assert.Single(gain.Events.OfType<LifePointsGained>()).Amount);

        var damage = new CountQueryScenario(new("count", Probe, EffectKind.NormalSpell,
            [new DealDamageStep(int.MaxValue, EffectCount.AllMonsters)]));
        damage.Populate(); damage.Cast(); damage.Resolve();
        Assert.Equal(0, damage.Engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.Equal(PlayerId.One, damage.Engine.State.Outcome!.Winner);
        Assert.Equal(8000, Assert.Single(damage.Events.OfType<EffectDamageDealt>()).Damage);
    }

    [Theory]
    [InlineData(EffectCount.DieRoll)]
    [InlineData(EffectCount.Heads)]
    [InlineData(EffectCount.MilledLevel)]
    public void RandomCountsAreDeferredUntilTheResultIsProduced(EffectCount count)
    {
        EffectStep first = count switch
        {
            EffectCount.DieRoll => new RollDieStep(), EffectCount.Heads => new TossCoinsStep(3), _ => new MillTopOfDeckStep(1)
        };
        var s = new CountQueryScenario(new("count", Probe, EffectKind.NormalSpell,
            [first, new GainLifePointsStep(100, PerCount: count)]), output: output, random: count != EffectCount.MilledLevel);
        s.EnterMain(); Assert.Empty(s.Events.OfType<DieRolled>()); Assert.Empty(s.Events.OfType<CoinsTossed>());
        s.Cast(); s.Resolve();
        int expected = count switch
        {
            EffectCount.DieRoll => Assert.Single(s.Events.OfType<DieRolled>()).Result,
            EffectCount.Heads => Assert.Single(s.Events.OfType<CoinsTossed>()).Heads.Count(h => h), _ => 4
        };
        Assert.Equal(8000 + expected * 100, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(expected == 0 ? 0 : 1, s.Events.OfType<LifePointsGained>().Count());
    }

    [Theory]
    [InlineData(EffectCount.DieRoll)]
    [InlineData(EffectCount.Heads)]
    [InlineData(EffectCount.MilledLevel)]
    public void AResultWithoutAProducerIsNotAValidPublicDefinition(EffectCount count) =>
        Assert.Throws<ArgumentException>(() => Gain(count));
}
