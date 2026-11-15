using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;

namespace TagForceSpecial.Tests;

/// <summary>Effect damage, gain conversions, redirection and simultaneous terminal results.</summary>
public sealed class LifePointOperationCharacterizationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("damage-gain", 8600, 8000)]
    [InlineData("gain-damage", 7500, 8000)]
    [InlineData("gain-cycle", 7500, 8000)]
    [InlineData("damage-cycle", 8500, 8000)]
    [InlineData("redirect", 8000, 7700)]
    [InlineData("both-redirect", 8000, 7700)]
    public void ConversionAndRedirectRulesPreserveRecipientsAndTerminateCycles(string mode, int own, int opponent)
    {
        EffectStep Rule(DamageRuleKind kind) => new ApplyDamageRuleStep(new(kind, DamageKinds.Effect));
        EffectStep[] steps = mode switch
        {
            "damage-gain" => [Rule(DamageRuleKind.BecomesGain), new TakeDamageStep(500), new GainLifePointsStep(100)],
            "gain-damage" => [Rule(DamageRuleKind.GainBecomesDamage), new GainLifePointsStep(500)],
            "gain-cycle" => [Rule(DamageRuleKind.GainBecomesDamage), Rule(DamageRuleKind.BecomesGain), new GainLifePointsStep(500)],
            "damage-cycle" => [Rule(DamageRuleKind.GainBecomesDamage), Rule(DamageRuleKind.BecomesGain), new TakeDamageStep(500)],
            "redirect" => [Rule(DamageRuleKind.RedirectToOpponent), new TakeDamageStep(300)],
            _ => [Rule(DamageRuleKind.RedirectToOpponent), new DamageBothPlayersStep(ToOpponent: 100, ToSelf: 200)]
        };
        var s = new CountQueryScenario(new("life-point-operations", Probe, EffectKind.NormalSpell, steps), output: new HealthOutput(output));
        s.EnterMain(); var before = s.Engine.State; s.Cast(); s.Resolve();
        Assert.Equal(own, s.Engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(opponent, s.Engine.State.GetPlayer(PlayerId.Two).LifePoints);
        Assert.All(before.Players, p => Assert.Equal(8000, p.LifePoints));
        Assert.Null(s.Engine.State.Outcome);
        if (mode == "both-redirect")
        {
            var damage = s.Events.OfType<EffectDamageDealt>().ToArray();
            Assert.Equal(new[] { 8000, 7900 }, damage.Select(d => d.LifePointsBefore));
            Assert.Equal(new[] { 7900, 7700 }, damage.Select(d => d.LifePointsAfter));
            Assert.All(damage, d => Assert.Equal(PlayerId.Two, d.Player));
        }
        if (mode is "gain-cycle" or "damage-cycle")
            Assert.Single(s.Events, e => e is EffectDamageDealt or LifePointsGained);
    }

    [Fact]
    public void SimultaneousLethalDamageEmitsOpponentFirstAndEndsInOneDraw()
    {
        var s = new CountQueryScenario(new("life-point-operations", Probe, EffectKind.NormalSpell,
            [new DamageBothPlayersStep(ToOpponent: 8000, ToSelf: 8000), new GainLifePointsStep(100)]), output: new HealthOutput(output));
        s.EnterMain(); s.Cast(); s.Resolve();
        Assert.Equal(new[] { PlayerId.Two, PlayerId.One }, s.Events.OfType<EffectDamageDealt>().Select(d => d.Player));
        Assert.True(s.Engine.State.Outcome!.IsDraw);
        Assert.Equal(DuelEndReason.BothLifePointsDepleted, Assert.Single(s.Events.OfType<DuelEnded>()).Outcome.Reason);
        Assert.All(s.Engine.State.Players, p => Assert.Equal(0, p.LifePoints));
        Assert.Empty(s.Events.OfType<LifePointsGained>());
    }

    private sealed class HealthOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "HEALTH_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
