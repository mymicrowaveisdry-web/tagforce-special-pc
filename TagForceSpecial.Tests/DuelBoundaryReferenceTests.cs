using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>Compares only the declared projections with repeated PSP captures.
/// Deck/LP boundaries use controlled PSP RAM inputs; C# reaches them through commands.
/// Combat uses observed post-Dice stats, without claiming effect equivalence.</summary>
[Trait("Category", "LocalReference")]
public sealed class DuelBoundaryReferenceTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "duel-boundaries.json")));

    private static JsonElement Scenario(JsonElement fixture, string id)
    {
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        var scenario = fixture.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
        Assert.Equal(2, scenario.GetProperty("repeatCount").GetInt32());
        return scenario;
    }

    private static CardDefinition Card(uint id, int attack, int defense = 0) => new(id,
        "Synthetic current-stat reference input", "", new(attack, defense, 4),
        new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 4u << 11, 0));

    private static DuelEngine Engine(params CardDefinition[] cards)
    {
        var all = new[] { Card(1, 0) }.Concat(cards).ToArray();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-bounded-reference"), all);
        var rules = all.ToDictionary(c => c.GameCardId, _ => new DuelCardRules(CardCategory.Monster, true, AllowsBasicBattle: true));
        var first = Enumerable.Repeat(1u, 40).ToArray();
        var second = Enumerable.Repeat(1u, 40).ToArray();
        if (cards.Any(c => c.GameCardId == 7716)) first[0] = 7716;
        if (cards.Any(c => c.GameCardId == 9979)) first[1] = 9979;
        if (cards.Any(c => c.GameCardId == 6109)) second[0] = 6109;
        return new(catalog, rules, first, second);
    }

    private static DuelActionResult Execute(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        return result;
    }

    private static void EnterMain(DuelEngine engine)
    {
        Execute(engine, new ResolveDrawStep(engine.State.ActivePlayer));
        Execute(engine, new ChangePhase(engine.State.ActivePlayer, DuelPhase.Main1));
    }

    private static void Finish(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        if (engine.State.Phase == DuelPhase.Battle)
            Execute(engine, new ChangePhase(actor, DuelPhase.Main2));
        Execute(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Execute(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[0].InstanceId));
        Execute(engine, new EndTurn(actor));
    }

    [Theory]
    [InlineData("last-draw", 1)]
    [InlineData("empty-draw", 0)]
    public void RequiredDrawMatchesControlledOriginalBoundary(string id, int count)
    {
        using var document = Fixture();
        var scenario = Scenario(document.RootElement, id);
        var engine = Engine();
        // Legal synthetic turns, never direct state hydration or a production testing hook.
        for (int guard = 0; guard < 80 && !(engine.State.ActivePlayer == PlayerId.Two &&
                 engine.State.GetPlayer(PlayerId.Two).Deck.Count == count); guard++)
        {
            EnterMain(engine);
            Finish(engine);
        }
        var before = scenario.GetProperty("before");
        var after = scenario.GetProperty("after");
        Assert.Equal(PlayerId.Two, engine.State.ActivePlayer);
        Assert.Equal(before.GetProperty("phase").GetString(), engine.State.Phase.ToString());
        Assert.Equal(before.GetProperty("players")[1].GetProperty("deckCount").GetInt32(), engine.State.GetPlayer(PlayerId.Two).Deck.Count);
        Assert.Null(engine.State.Outcome);
        int handBefore = engine.State.GetPlayer(PlayerId.Two).Hand.Count;
        var result = Execute(engine, new ResolveDrawStep(PlayerId.Two));
        Assert.Equal(after.GetProperty("phase").GetString(), engine.State.Phase.ToString());
        Assert.Equal(after.GetProperty("players")[1].GetProperty("deckCount").GetInt32(), engine.State.GetPlayer(PlayerId.Two).Deck.Count);
        int nativeHandDelta = after.GetProperty("players")[1].GetProperty("handCount").GetInt32() - before.GetProperty("players")[1].GetProperty("handCount").GetInt32();
        Assert.Equal(nativeHandDelta, engine.State.GetPlayer(PlayerId.Two).Hand.Count - handBefore);
        if (count == 1)
        {
            Assert.IsType<CardDrawn>(result.Events[0]);
            Assert.Null(engine.State.Outcome);
            Assert.Equal(0, after.GetProperty("endLatchRaw").GetInt32());
        }
        else
        {
            Assert.IsType<DuelEnded>(Assert.Single(result.Events));
            Assert.Equal(1, after.GetProperty("endLatchRaw").GetInt32());
            Assert.Equal(new DuelOutcome(PlayerId.One, PlayerId.Two, DuelEndReason.UnableToDraw), engine.State.Outcome);
            Assert.Equal(DuelError.DuelFinished, engine.Execute(new ResolveDrawStep(PlayerId.Two)).Error);
        }
    }

    [Fact]
    public void LethalDirectAttackMatchesControlledOriginalDefeat()
    {
        using var document = Fixture();
        var scenario = Scenario(document.RootElement, "lp-defeat");
        var engine = Engine(Card(7716, 1200, 400));
        EnterMain(engine);
        int card = engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == 7716).InstanceId;
        Execute(engine, new NormalSummon(PlayerId.One, card, 2));
        Finish(engine);
        for (int hit = 0; hit < 7; hit++)
        {
            EnterMain(engine); // Opponent takes no action.
            Finish(engine);
            EnterMain(engine);
            Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            if (hit == 6)
                Assert.Equal(scenario.GetProperty("before").GetProperty("players")[1].GetProperty("lifePoints").GetInt32(), engine.State.GetPlayer(PlayerId.Two).LifePoints);
            var result = Execute(engine, new DeclareAttack(PlayerId.One, 2));
            if (hit < 6) Finish(engine);
            else
            {
                Assert.Equal(scenario.GetProperty("after").GetProperty("players")[1].GetProperty("lifePoints").GetInt32(), engine.State.GetPlayer(PlayerId.Two).LifePoints);
                Assert.Equal(new DuelOutcome(PlayerId.One, PlayerId.Two, DuelEndReason.LifePointsDepleted), engine.State.Outcome);
                Assert.Contains(result.Events, e => e is BattleDamageDealt { Damage: 1200, LifePointsBefore: 800, LifePointsAfter: 0 });
                Assert.IsType<DuelEnded>(result.Events.Last());
            }
        }
    }

    [Fact]
    public void ObservedCurrentCombatStatsMatchOriginalDamageAndDestruction()
    {
        using var document = Fixture();
        var fixture = document.RootElement;
        var scenario = Scenario(fixture, "monster-battle");
        var input = fixture.GetProperty("battleInput");
        var engine = Engine(Card(7716, 1200, 400),
            Card(9979, input.GetProperty("defenderAttack").GetInt32(), input.GetProperty("defenderDefense").GetInt32()),
            Card(6109, input.GetProperty("attackerAttack").GetInt32(), input.GetProperty("attackerDefense").GetInt32()));
        EnterMain(engine);
        Summon(engine, 7716, 2);
        Finish(engine);
        EnterMain(engine); Finish(engine);
        EnterMain(engine); Summon(engine, 9979, 3); Finish(engine);
        EnterMain(engine); Summon(engine, 6109, 2);
        Execute(engine, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        AssertCombat(scenario.GetProperty("before"), engine.State);
        var result = Execute(engine, new DeclareAttack(PlayerId.Two, 2, 3));
        AssertCombat(scenario.GetProperty("after"), engine.State);
        Assert.Contains(result.Events, e => e is BattleDamageDealt { Player: PlayerId.One, Damage: 200 });
        Assert.Contains(result.Events, e => e is MonsterDestroyed { Player: PlayerId.One, Zone: 3 });
        Assert.Null(engine.State.Outcome);
    }

    private static void Summon(DuelEngine engine, uint id, int zone) => Execute(engine,
        new NormalSummon(engine.State.ActivePlayer, engine.State.GetPlayer(engine.State.ActivePlayer).Hand.Single(c => c.GameCardId == id).InstanceId, zone));

    private static void AssertCombat(JsonElement expected, DuelState state)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)state.ActivePlayer);
        Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), state.TurnNumber);
        Assert.Equal(expected.GetProperty("phase").GetString(), state.Phase.ToString());
        for (int i = 0; i < 2; i++)
        {
            var player = state.GetPlayer((PlayerId)i);
            Assert.Equal(expected.GetProperty("players")[i].GetProperty("lifePoints").GetInt32(), player.LifePoints);
            Assert.Equal(expected.GetProperty("players")[i].GetProperty("monsterCardIds").EnumerateArray().Select(v => v.GetUInt32()),
                player.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        }
        // Opponent's resolved Dice is deliberately outside this combat projection.
        Assert.Equal(expected.GetProperty("players")[0].GetProperty("graveyardCardIds").EnumerateArray().Select(v => v.GetUInt32()),
            state.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId));
    }
}
