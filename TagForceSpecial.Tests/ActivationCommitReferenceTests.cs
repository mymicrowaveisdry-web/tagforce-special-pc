using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>Original commitment projections only. No native chain-resolution comparison is claimed.</summary>
[Trait("Category", "LocalReference")]
public sealed class ActivationCommitReferenceTests
{
    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        return result;
    }

    private static DuelEngine Engine(uint spell)
    {
        uint[] ids = [1, 9979, 7716, 6151, 7747, 9723, 5217, 4909, 4818, 4815, 4664];
        var spells = new HashSet<uint> { 7747, 5217, 4909, 4818, 4815 };
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-activation-projection-inputs"),
            ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(1000, 1000, 2),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 2u << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(spells.Contains(id) ? CardCategory.Spell :
            id == 4664 ? CardCategory.Trap : CardCategory.Monster,
            AllowsNormalSummon: !spells.Contains(id) && id != 4664, AllowsSpellTrapSet: spells.Contains(id) || id == 4664));
        uint[] p1 = [9979, 7716, 9979, 6151, 7747, 7716, 9723, spell, 1, 7716, .. Enumerable.Repeat(1u, 30)];
        uint[] p2 = [4818, 4909, 4815, 4664, 1, .. Enumerable.Repeat(1u, 35)];
        var engine = new DuelEngine(catalog, rules, p1, p2, experimentalActivationContracts:
            [ActivationResearch.LightningVortex(ids), ActivationResearch.MysticalSpaceTyphoon()]);
        EnterMain(engine);
        while (engine.State.TurnNumber < 7)
        {
            var actor = engine.State.ActivePlayer;
            if (actor == PlayerId.One && engine.State.TurnNumber is 1 or 3)
            {
                uint cardId = engine.State.TurnNumber == 1 ? 7716u : 6151u;
                var card = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == cardId);
                Ok(engine, new NormalSummon(actor, card.InstanceId, engine.State.TurnNumber == 1 ? 2 : 3));
            }
            if (actor == PlayerId.Two && engine.State.TurnNumber == 2)
                for (int zone = 1; zone <= 4; zone++)
                {
                    uint id = new uint[] { 4818, 4909, 4815, 4664 }[zone - 1];
                    Ok(engine, new SetSpellTrap(actor, engine.State.GetPlayer(actor).Hand.Single(c => c.GameCardId == id).InstanceId, zone));
                }
            Ok(engine, new ChangePhase(actor, DuelPhase.End));
            while (engine.State.GetPlayer(actor).Hand.Count > 6)
                Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
            Ok(engine, new EndTurn(actor));
            EnterMain(engine);
        }
        var source = engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == spell);
        Ok(engine, new ActivateSpell(PlayerId.One, source.InstanceId, 2));
        return engine;
    }

    private static void EnterMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
    }

    private static void Projection(JsonElement expected, DuelState state)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)state.ActivePlayer);
        Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), state.TurnNumber);
        Assert.Equal(expected.GetProperty("phase").GetString(), state.Phase.ToString());
        var e = expected.GetProperty("players")[0];
        var p = state.GetPlayer(PlayerId.One);
        uint[] Ids(string property) => e.GetProperty(property).EnumerateArray().Select(c => c.GetUInt32()).ToArray();
        Assert.Equal(e.GetProperty("handCount").GetInt32(), p.Hand.Count);
        Assert.Equal(e.GetProperty("deckCount").GetInt32(), p.Deck.Count);
        Assert.Equal(Ids("handCardIds"), p.Hand.Select(c => c.GameCardId));
        Assert.Equal(Ids("monsterCardIds"), p.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        Assert.Equal(Ids("spellTrapCardIds"), p.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
        Assert.Equal(Ids("graveyardCardIds"), p.Graveyard.Select(c => c.GameCardId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CostPaymentMatchesOriginalPreResponseBoundary(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "activation-contracts.json")));
        var fixture = doc.RootElement;
        Assert.False(fixture.GetProperty("nativeMatchClaim").GetBoolean());
        var pair = fixture.GetProperty("costPairs")[repeat];
        JsonElement Snapshot(string stage) => fixture.GetProperty("snapshots").GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine(5217);
        var before = engine.State;
        Projection(Snapshot("before"), before);
        var p = before.PendingDecision!;
        var cost = p.CardOptions.First(c => c.GameCardId == 9979);
        Ok(engine, new ChooseDecisionCard(PlayerId.One, p.DecisionId, cost.InstanceId));
        Projection(Snapshot("paid"), engine.State);
        Assert.Equal(cost, engine.State.CommittedActivation!.DiscardedCost);
        Assert.Null(engine.State.CommittedActivation.Target); // Vortex's discard is not an effect target.
        Assert.Same(before.GetPlayer(PlayerId.Two), engine.State.GetPlayer(PlayerId.Two));
        Assert.Equal(before.GetPlayer(PlayerId.One).LifePoints, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.Empty(before.GetPlayer(PlayerId.One).Graveyard);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TargetBindingMatchesOriginalSelectedScapegoat(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "activation-contracts.json")));
        var fixture = doc.RootElement;
        var pair = fixture.GetProperty("targetPairs")[repeat];
        JsonElement Snapshot(string stage) => fixture.GetProperty("snapshots").GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine(4909);
        var before = engine.State;
        Projection(Snapshot("before"), before);
        var selection = fixture.GetProperty("selectedTarget");
        var p = before.PendingDecision!;
        var target = p.TargetOptions.Single(t => (int)t.Player == selection.GetProperty("player").GetInt32() && t.Zone == selection.GetProperty("zone").GetInt32());
        Assert.Equal(selection.GetProperty("cardId").GetUInt32(), target.Card.GameCardId);
        Ok(engine, new ChooseDecisionTarget(PlayerId.One, p.DecisionId, target.Player, target.Zone, target.Card.InstanceId));
        Projection(Snapshot("committed"), engine.State);
        Assert.Equal(target, engine.State.CommittedActivation!.Target);
        Assert.Null(engine.State.CommittedActivation.DiscardedCost);
        Assert.Same(before.GetPlayer(PlayerId.Two), engine.State.GetPlayer(PlayerId.Two));
        Assert.Equal(before.GetPlayer(PlayerId.One).LifePoints, engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.True(engine.State.ResearchSliceComplete);
        // Native Scapegoat response/face-up change and its later tokens are excluded:
        // this managed contract stops before implementing any response or resolution.
    }
}
