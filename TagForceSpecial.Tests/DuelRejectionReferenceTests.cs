using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>
/// Compares a C# rejection with the original game's observed UI constraint and
/// selected unchanged state. It does not assert an equivalent PSP error code.
/// </summary>
[Trait("Category", "LocalReference")]
public sealed class DuelRejectionReferenceTests
{
    [Fact]
    public void UsedNormalSummonAllowanceRejectsTheActionHiddenByOriginalUi()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "duel-rejections.json")));
        var fixture = document.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        var scenario = fixture.GetProperty("scenario");
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("originalErrorCode").ValueKind);
        Assert.Equal("NormalSummonLimitReached", scenario.GetProperty("expectedCSharpError").GetString());
        uint attemptedId = fixture.GetProperty("attemptedCardId").GetUInt32();
        uint summonedId = fixture.GetProperty("summonedCardId").GetUInt32();
        int zone = fixture.GetProperty("summonedZone").GetInt32();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-rejection-reference-projection"),
        [
            Card(1, 4, 1),
            Card(attemptedId, fixture.GetProperty("attemptedLevel").GetUInt32(), 2),
            Card(summonedId, fixture.GetProperty("summonedLevel").GetUInt32(), 3)
        ]);
        var rules = catalog.Cards.ToDictionary(card => card.GameCardId, _ => new DuelCardRules(CardCategory.Monster, true));
        uint[] firstDeck = [attemptedId, summonedId, .. Enumerable.Repeat(1u, 38)];
        var engine = new DuelEngine(catalog, rules, firstDeck, Enumerable.Repeat(1u, 40));
        Execute(engine, new ResolveDrawStep(PlayerId.One));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        var summoned = engine.State.GetPlayer(PlayerId.One).Hand.Single(card => card.GameCardId == summonedId);
        Execute(engine, new NormalSummon(PlayerId.One, summoned.InstanceId, zone));
        AssertProjection(scenario.GetProperty("before"), engine.State);
        var before = engine.State;
        string beforeJson = JsonSerializer.Serialize(before);
        var attempted = before.GetPlayer(PlayerId.One).Hand.Single(card => card.GameCardId == attemptedId);
        Assert.Null(before.GetPlayer(PlayerId.One).MonsterZones[0]); // Isolate the allowance constraint from occupancy.
        var result = engine.Execute(new NormalSummon(PlayerId.One, attempted.InstanceId, 0));
        Assert.False(result.Succeeded);
        Assert.Equal(DuelError.NormalSummonLimitReached, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(engine.State));
        AssertProjection(scenario.GetProperty("after"), engine.State);
    }

    [Fact]
    public void OccupiedZoneRejectsPlacementBlockedByOriginalDestinationPicker()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "duel-rejections.json")));
        var fixture = document.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        var scenario = fixture.GetProperty("occupiedScenario");
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("originalErrorCode").ValueKind);
        Assert.Equal("MonsterZoneOccupied", scenario.GetProperty("expectedCSharpError").GetString());
        uint attemptedId = fixture.GetProperty("attemptedCardId").GetUInt32();
        uint summonedId = fixture.GetProperty("summonedCardId").GetUInt32();
        int summonedZone = fixture.GetProperty("summonedZone").GetInt32();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-occupied-zone-reference-projection"),
        [
            Card(1, 4, 1), Card(2, 0, 2),
            Card(attemptedId, fixture.GetProperty("attemptedLevel").GetUInt32(), 3),
            Card(summonedId, fixture.GetProperty("summonedLevel").GetUInt32(), 4)
        ]);
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [1] = new(CardCategory.Monster, true),
            [2] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
            [attemptedId] = new(CardCategory.Monster, true),
            [summonedId] = new(CardCategory.Monster, true)
        };
        uint[] firstDeck = [attemptedId, summonedId, .. Enumerable.Repeat(1u, 38)];
        uint[] secondDeck = [2, 2, .. Enumerable.Repeat(1u, 38)];
        var engine = new DuelEngine(catalog, rules, firstDeck, secondDeck);
        Execute(engine, new ResolveDrawStep(PlayerId.One));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        var summoned = engine.State.GetPlayer(PlayerId.One).Hand.Single(card => card.GameCardId == summonedId);
        Execute(engine, new NormalSummon(PlayerId.One, summoned.InstanceId, summonedZone));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Execute(engine, new EndTurn(PlayerId.One));
        Execute(engine, new ResolveDrawStep(PlayerId.Two));
        Execute(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main1));
        var backRow = engine.State.GetPlayer(PlayerId.Two).Hand.Where(card => card.GameCardId == 2).ToArray();
        for (int index = 0; index < backRow.Length; index++)
            Execute(engine, new SetSpellTrap(PlayerId.Two, backRow[index].InstanceId, index));
        Execute(engine, new ChangePhase(PlayerId.Two, DuelPhase.End));
        Execute(engine, new EndTurn(PlayerId.Two));
        Execute(engine, new ResolveDrawStep(PlayerId.One));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main1));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Assert.False(engine.State.NormalSummonUsed); // Isolate occupancy from the per-turn allowance.
        AssertProjection(scenario.GetProperty("before"), engine.State);
        var before = engine.State;
        string beforeJson = JsonSerializer.Serialize(before);
        var attempted = before.GetPlayer(PlayerId.One).Hand.Single(card => card.GameCardId == attemptedId);
        var result = engine.Execute(new NormalSummon(PlayerId.One, attempted.InstanceId,
            scenario.GetProperty("attemptedZone").GetInt32()));
        Assert.False(result.Succeeded);
        Assert.Equal(DuelError.MonsterZoneOccupied, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(engine.State));
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
        for (int index = 0; index < 2; index++)
        {
            var player = state.GetPlayer((PlayerId)index);
            Assert.Equal(players[index].GetProperty("lifePoints").GetInt32(), player.LifePoints);
            Assert.Equal(players[index].GetProperty("handCount").GetInt32(), player.Hand.Count);
            Assert.Equal(players[index].GetProperty("deckCount").GetInt32(), player.Deck.Count);
            Assert.Equal(players[index].GetProperty("monsterCardIds").EnumerateArray().Select(card => card.GetUInt32()),
                player.MonsterZones.Select(monster => monster?.Card.GameCardId ?? 0));
        }
    }
}
