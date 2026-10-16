using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>
/// Compare bounded state projections with recorded PPSSPP observations. Synthetic
/// decks reproduce the observed counts and summon card; hidden state and deck
/// identities/order are deliberately outside this comparison.
/// </summary>
public sealed class DuelReferenceScenarioTests
{
    [Theory]
    [InlineData("normal-summon")]
    [InlineData("end-turn")]
    [InlineData("draw")]
    public void CapturedOriginalGameTransitionMatchesObservableProjection(string scenarioId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "duel-primitives.json")));
        var fixture = document.RootElement;
        var scenario = fixture.GetProperty("scenarios").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == scenarioId);
        uint sampleId = fixture.GetProperty("summonedCardId").GetUInt32();
        uint level = fixture.GetProperty("summonedLevel").GetUInt32();
        int zone = fixture.GetProperty("summonedZone").GetInt32();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-reference-projection"),
        [
            Card(1, 4, 1),
            Card(sampleId, level, 2)
        ]);
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [1] = new(CardCategory.Monster, true),
            [sampleId] = new(CardCategory.Monster, true)
        };
        var firstDeck = Enumerable.Repeat(1u, 40).ToArray();
        firstDeck[1] = sampleId;
        var engine = new DuelEngine(catalog, rules, firstDeck, Enumerable.Repeat(1u, 40));
        Execute(engine, new ResolveDrawStep(PlayerId.One));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        int instance = engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == sampleId).InstanceId;
        if (scenarioId is "end-turn" or "draw")
        {
            Execute(engine, new NormalSummon(PlayerId.One, instance, zone));
            Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        }
        if (scenarioId == "draw") Execute(engine, new EndTurn(PlayerId.One));

        AssertProjection(scenario.GetProperty("before"), engine.State);
        DuelCommand command = scenarioId switch
        {
            "normal-summon" => new NormalSummon(PlayerId.One, instance, zone),
            "end-turn" => new EndTurn(PlayerId.One),
            "draw" => new ResolveDrawStep(PlayerId.Two),
            _ => throw new ArgumentException("Unknown reference scenario.")
        };
        Execute(engine, command);
        AssertProjection(scenario.GetProperty("after"), engine.State);
    }

    private static CardDefinition Card(uint id, uint level, int row) => new(id,
        "Synthetic reference projection", "", new(0, 0, level), new(0, 0, 0, 0, 0, 0, 0),
        new(row, id, level << 11, 0));

    private static void Execute(DuelEngine engine, DuelCommand command)
    {
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
    }

    private static void AssertProjection(JsonElement expected, DuelState state)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)state.ActivePlayer);
        Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), state.TurnNumber);
        Assert.Equal(expected.GetProperty("phase").GetString(), state.Phase.ToString());
        var players = expected.GetProperty("players");
        for (int i = 0; i < 2; i++)
        {
            var player = state.GetPlayer((PlayerId)i);
            Assert.Equal(players[i].GetProperty("lifePoints").GetInt32(), player.LifePoints);
            Assert.Equal(players[i].GetProperty("handCount").GetInt32(), player.Hand.Count);
            Assert.Equal(players[i].GetProperty("deckCount").GetInt32(), player.Deck.Count);
            Assert.Equal(players[i].GetProperty("monsterCardIds").EnumerateArray().Select(c => c.GetUInt32()),
                player.MonsterZones.Select(monster => monster?.Card.GameCardId ?? 0));
        }
    }
}
