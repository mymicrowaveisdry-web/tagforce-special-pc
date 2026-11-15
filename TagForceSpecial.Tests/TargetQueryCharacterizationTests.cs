using TagForceSpecial.Core;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.CountQueryScenario;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Ordered public target choices, including position, side, zone and source exclusions.</summary>
public sealed class TargetQueryCharacterizationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> FieldCases()
    {
        foreach (var controller in new[] { PlayerId.One, PlayerId.Two })
            foreach (var (kind, side, filter, expected) in new[]
            {
                (FieldCardKind.Any, TargetSide.Either, FieldCardFilter.Any, "O:M:0,O:M:1,T:M:0,T:M:1,T:S:1"),
                (FieldCardKind.Any, TargetSide.Own, FieldCardFilter.Any, "O:M:0,O:M:1"),
                (FieldCardKind.Any, TargetSide.Opponent, FieldCardFilter.Any, "T:M:0,T:M:1,T:S:1"),
                (FieldCardKind.Any, TargetSide.Either, FieldCardFilter.FaceUp, "O:M:0,T:M:0"),
                (FieldCardKind.Any, TargetSide.Either, FieldCardFilter.FaceDown, "O:M:1,T:M:1,T:S:1"),
                (FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.Any, "O:M:0,O:M:1,T:M:0,T:M:1"),
                (FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.AttackPosition, "O:M:0,T:M:0"),
                (FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUpAttack, "O:M:0,T:M:0"),
                (FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.DefensePosition, "O:M:1,T:M:1"),
                (FieldCardKind.SpellTrap, TargetSide.Either, FieldCardFilter.Any, "T:S:1"),
                (FieldCardKind.Spell, TargetSide.Either, FieldCardFilter.FaceDown, "T:S:1"),
                (FieldCardKind.PendulumScale, TargetSide.Either, FieldCardFilter.Any, ""),
                (FieldCardKind.SpellTrap, TargetSide.Either, FieldCardFilter.FaceUp, "")
            }) yield return [controller, kind, side, filter, expected];
    }

    [Theory]
    [MemberData(nameof(FieldCases))]
    public void FieldChoicesPreserveSideZonePositionOrderAndExcludeTheSource(PlayerId controller,
        FieldCardKind kind, TargetSide side, FieldCardFilter filter, string expected)
    {
        var definition = new EffectDefinition("target", Probe, EffectKind.NormalSpell, [new DestroyTargetsStep()],
            fieldTargets: [new FieldTarget(kind, side, filter)]);
        var s = new CountQueryScenario(definition, controller);
        s.Populate();
        if (expected.Length == 0)
        {
            Reject(s.Engine, new ActivateSpell(controller,
                s.Engine.State.GetPlayer(controller).Hand.First(c => c.GameCardId == Probe).InstanceId, 0), DuelError.DecisionUnavailable);
            return;
        }
        s.Cast();
        var options = s.Engine.State.PendingDecision!.FieldTargetOptions!;
        string Key(FieldCardTarget t) => $"{(t.Player == controller ? "O" : "T")}:{(t.Location == FieldLocation.MonsterZone ? "M" : "S")}:{t.Zone}";
        Assert.Equal(expected, string.Join(',', options.Select(Key)));
        Assert.DoesNotContain(options, t => t.Card.GameCardId == Probe);
        var first = options[0];
        s.Resolve();
        Assert.False(first.IsStillPresent(s.Engine.State));
    }

    [Theory]
    [InlineData(PlayerId.One, CardZone.Graveyard)]
    [InlineData(PlayerId.Two, CardZone.Graveyard)]
    [InlineData(PlayerId.One, CardZone.Banished)]
    [InlineData(PlayerId.Two, CardZone.Banished)]
    public void ZoneChoicesKeepControllerFirstAndDuplicateInstances(PlayerId controller, CardZone zone)
    {
        var definition = new EffectDefinition("target", Probe, EffectKind.NormalSpell, [new AddTargetsToHandStep()],
            fieldTargets: [new ZoneTarget(zone, TargetSide.Either, new AnyMonsterFilter())]);
        EffectStep preparation = zone == CardZone.Graveyard
            ? new DestroyAllStep(FieldCardKind.Monster) : new BanishAllStep();
        var s = new CountQueryScenario(definition, controller, [preparation], new TargetOutput(output));
        s.Populate(); s.Cast(Preparation); s.Resolve(); s.Cast();
        var options = s.Engine.State.PendingDecision!.FieldTargetOptions!;
        var opponent = controller == PlayerId.One ? PlayerId.Two : PlayerId.One;
        Assert.Equal(new[] { controller, controller, opponent, opponent }, options.Select(t => t.Player));
        uint own = controller == PlayerId.One ? FirstMonster : SecondMonster;
        uint theirs = controller == PlayerId.One ? SecondMonster : FirstMonster;
        Assert.Equal(new[] { own, own, theirs, theirs }, options.Select(t => t.Card.GameCardId));
        Assert.Equal(4, options.Select(t => t.Card.InstanceId).Distinct().Count());
        Assert.All(options, t => Assert.Equal(zone == CardZone.Graveyard ? FieldLocation.Graveyard : FieldLocation.Banished, t.Location));
        var chosen = options[0];
        s.Resolve();
        Assert.Contains(chosen.Card, s.Engine.State.GetPlayer(controller).Hand);
    }

    private sealed class TargetOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        public void WriteLine(string message) => output.WriteLine(message.Replace("COUNT_TRACE ", "TARGET_TRACE ", StringComparison.Ordinal));
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
