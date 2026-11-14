using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;

namespace TagForceSpecial.Tests;

/// <summary>Continuous stat reevaluation, source removal and protective-counter transitions.</summary>
public sealed class LastingOperationCharacterizationTests(ITestOutputHelper output)
{
    private static int Attack(DuelEngine e, PlayerId player, int zone) =>
        e.State.EventEffects.Statistics.Single(s => s.Monster.Player == player && s.Monster.Zone == zone).Attack!.Value;

    [Theory]
    [InlineData("flat", 1100, 1100)]
    [InlineData("filtered", 1100, 1000)]
    [InlineData("scaled", 1200, 1200)]
    public void LastingStatsRecomputeAfterAFlipAndEndWhenTheSourceLeaves(string mode, int ownAttack, int opponentAttack)
    {
        var modifier = new LastingStatModifier(TargetSide.Either, 100, 200,
            filter: mode == "filtered" ? new MonsterTypeFilter([1]) : null,
            perCount: mode == "scaled" ? LastingCount.OwnFaceUpMonsters : LastingCount.None);
        var s = new CountQueryScenario(new("lasting", Probe, EffectKind.ContinuousSpell, [], lastingModifiers: [modifier]),
            preparation: [new DestroyAllStep(FieldCardKind.SpellTrap, TargetSide.Own)], output: new LastingOutput(output), basicBattle: true);
        s.Populate(); s.Cast(); s.Resolve();
        Assert.Equal(1100, Attack(s.Engine, PlayerId.One, 0));
        var previous = s.Engine.State;
        s.Execute(new ChangeMonsterPosition(PlayerId.One, 1, BattlePosition.FaceUpAttack));
        Assert.Equal(ownAttack, Attack(s.Engine, PlayerId.One, 0));
        Assert.Equal(opponentAttack, Attack(s.Engine, PlayerId.Two, 0));
        Assert.Equal(1100, previous.EventEffects.Statistics.Single(t => t.Monster.Player == PlayerId.One && t.Monster.Zone == 0).Attack);
        int source = s.Engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Preparation).InstanceId;
        s.Execute(new ActivateSpell(PlayerId.One, source, 2)); s.Resolve();
        Assert.Equal(1000, Attack(s.Engine, PlayerId.One, 0));
        Assert.Equal(1000, Attack(s.Engine, PlayerId.Two, 0));
    }

    [Fact]
    public void AProtectiveCounterIsConsumedWithoutDestroyingItsMonster()
    {
        var s = new CountQueryScenario(new("lasting", Probe, EffectKind.NormalSpell,
            [new PlaceCounterStep(2, 5, protective: true)], fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp)]),
            preparation: [new DestroyAllStep(FieldCardKind.Monster, TargetSide.Own)], output: new LastingOutput(output));
        s.Populate(); s.Cast(); s.Resolve();
        Assert.Equal(2, s.Engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.ProtectionCounters);
        s.Cast(Preparation); s.Resolve();
        Assert.Equal(1, s.Engine.State.GetPlayer(PlayerId.One).MonsterZones[0]!.ProtectionCounters);
        Assert.Null(s.Engine.State.GetPlayer(PlayerId.One).MonsterZones[1]);
        Assert.Single(s.Events.OfType<DestructionReplaced>());
    }

    private sealed class LastingOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "LASTING_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
