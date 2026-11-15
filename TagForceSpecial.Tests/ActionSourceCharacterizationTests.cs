using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using Xunit.Abstractions;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Public action ordering, cached observations, rejected handles and concealed-card invariance.</summary>
public sealed class ActionSourceCharacterizationTests(ITestOutputHelper output)
{
    private void Capture(string label, DuelActionSource source, ActionExecution? execution = null) =>
        output.WriteLine("ACTION_TRACE " + JsonSerializer.Serialize(new
        {
            label, execution, contract = DuelActionSource.ContractVersion,
            views = new[] { source.Observe(PlayerId.One), source.Observe(PlayerId.Two) }
        }));

    [Fact]
    public void RevisionCacheAndInvalidHandlesPreserveStateAndRecoverAfterExecution()
    {
        var s = new CountQueryScenario(new("action-probe", CountQueryScenario.Probe, EffectKind.NormalSpell, [new GainLifePointsStep(100)]));
        var source = s.Actions(); var before = s.Engine.State;
        var initial = source.Observe(PlayerId.One);
        Assert.Same(initial, source.Observe(PlayerId.One));
        Assert.Same(before, s.Engine.State);
        Assert.Empty(source.Observe(PlayerId.Two).Actions);
        Assert.Equal(Enumerable.Range(0, initial.Actions.Count), initial.Actions.Select(a => a.Token));
        Capture("initial", source);
        foreach (var (player, revision, token, error) in new[]
        {
            ((PlayerId)99, 0L, 0, DuelError.InvalidPlayer),
            (PlayerId.Two, 0L, 0, DuelError.InvalidDecisionChoice),
            (PlayerId.One, 1L, int.MaxValue, DuelError.StaleDecision),
            (PlayerId.One, 0L, int.MaxValue, DuelError.InvalidDecisionChoice)
        })
        {
            var denied = source.Execute(player, revision, token);
            Assert.Equal(error, denied.Error); Assert.False(denied.Accepted); Assert.Same(before, s.Engine.State);
            Capture(error.ToString(), source, denied);
        }
        var accepted = source.Execute(PlayerId.One, initial.Revision, initial.Actions[0].Token);
        Assert.True(accepted.Accepted); Capture("accepted", source, accepted);
        var fresh = source.Observe(PlayerId.One); Assert.NotSame(initial, fresh); Assert.Same(fresh, source.Observe(PlayerId.One));
        before = s.Engine.State;
        var stale = source.Execute(PlayerId.One, initial.Revision, initial.Actions[0].Token);
        Assert.Equal(DuelError.StaleDecision, stale.Error); Assert.Same(before, s.Engine.State);
        Capture("old-revision", source, stale);
        Assert.Throws<NotSupportedException>(() => ((IList<LegalAction>)fresh.Actions).Clear());
    }

    [Fact]
    public void SecretIdentitiesCannotChangePublicActionsOrDeclarableNames()
    {
        string Run(uint secret)
        {
            var catalog = new CardCatalog(new(null, "synthetic-action-privacy"),
                new[] { "Known monster", "Secret two", "Secret three", "Declaration probe" }
                    .Select((name, i) => new CardDefinition((uint)i + 1, name, "", new(1000, 1000, 4))));
            var rules = Enumerable.Range(1, 4).ToDictionary(i => (uint)i, i => i == 4
                ? new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true)
                : new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true));
            var e = new DuelEngine(catalog, rules, Enumerable.Repeat(secret, 40), [4, .. Enumerable.Repeat(1u, 39)],
                effectDefinitions: [new EffectDefinition("declaration", 4, EffectKind.NormalSpell, [new GainLifePointsStep(100)])
                    .WithDeclaration(DeclarationKind.CardName)]);
            EnterMain(e); Ok(e, new SetMonster(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0)); Next(e);
            var source = new DuelActionSource(e, catalog); var before = source.Observe(PlayerId.Two);
            Assert.Null(before.Opponent.Monsters[0]!.CardId);
            Assert.All(before.Opponent.Hand, c => Assert.Null(c.CardId));
            Ok(e, new ActivateSpell(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == 4).InstanceId, 0));
            var declaration = source.Observe(PlayerId.Two);
            Assert.All(declaration.Actions, a => Assert.DoesNotContain("Secret", a.Label));
            Assert.DoesNotContain(declaration.Actions, a => a.TargetZone is 2 or 3);
            return JsonSerializer.Serialize(new { before, declaration });
        }
        string first = Run(2); Assert.Equal(first, Run(3));
        output.WriteLine("ACTION_TRACE " + first);
    }

    [Theory]
    [InlineData(150, ActionAvailability.Ready)]
    [InlineData(200, ActionAvailability.Unsupported)]
    public void DiscoveryBudgetAppliesBeforeLegalityFiltering(int effectCount, ActionAvailability expected)
    {
        var catalog = new CardCatalog(new(null, "synthetic-action-budget"), [new CardDefinition(1, "Budget monster", "", new(1000, 1000, 4))]);
        var e = new DuelEngine(catalog, new Dictionary<uint, DuelCardRules> { [1] = new(CardCategory.Monster, AllowsNormalSummon: true) },
            Enumerable.Repeat(1u, 40), Enumerable.Repeat(1u, 40),
            effectDefinitions: Enumerable.Range(0, effectCount).Select(i => new EffectDefinition($"budget:{i}", 1,
                EffectKind.MonsterIgnition, [new GainLifePointsStep(1)])));
        var before = e.State; var source = new DuelActionSource(e, catalog); var view = source.Observe(PlayerId.One);
        Assert.Equal(expected, view.Availability); Assert.Same(before, e.State);
        if (expected == ActionAvailability.Unsupported)
        {
            Assert.Empty(view.Actions); Assert.Equal("Legal-action discovery budget exceeded", view.Diagnostic);
        }
        else Assert.Equal(ActionKind.Draw, Assert.Single(view.Actions).Kind);
        Capture($"budget:{effectCount}", source);
    }
}
