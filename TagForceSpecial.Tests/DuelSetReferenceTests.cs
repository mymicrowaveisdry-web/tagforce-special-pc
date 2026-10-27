using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>Compares scoped set transitions with repeated original-game captures.
/// UI constraints do not expose native error codes. Turn-seven back-row scenes
/// compare slot IDs and deltas, excluding earlier effects and unrelated state.</summary>
[Trait("Category", "LocalReference")]
public sealed class DuelSetReferenceTests
{
    [Theory]
    [InlineData("monster-set")]
    [InlineData("monster-set-shares-allowance-ui")]
    [InlineData("spell-set-after-monster-set")]
    [InlineData("occupied-monster-set-ui")]
    [InlineData("spell-set-preserves-allowance-ui")]
    [InlineData("occupied-backrow-set-ui")]
    [InlineData("opponent-monster-spell-trap-set-sequence")]
    public void SetBehaviorMatchesRepeatedOriginalProjection(string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "duel-sets.json")));
        var fixture = document.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        var scenario = fixture.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
        Assert.Equal(2, scenario.GetProperty("repeatCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("originalErrorCode").ValueKind);
        bool backrowOnly = scenario.GetProperty("comparisonScope").GetString() == "backrow-and-deltas";
        var engine = Engine(fixture, backrowOnly);
        EnterMain(engine);
        switch (id)
        {
            case "monster-set-shares-allowance-ui":
            case "spell-set-after-monster-set":
                SetMonster(engine, 7716, 2);
                break;
            case "occupied-monster-set-ui":
            case "opponent-monster-spell-trap-set-sequence":
                PrepareTurnThreeMain2(engine);
                if (id == "opponent-monster-spell-trap-set-sequence")
                {
                    SetMonster(engine, 6151, 3);
                    Finish(engine);
                    Execute(engine, new ResolveDrawStep(PlayerId.Two));
                }
                break;
            case "occupied-backrow-set-ui":
                SetSpellTrap(engine, 7747, 2);
                break;
        }
        AssertProjection(scenario.GetProperty("before"), engine.State, backrowOnly);
        var before = engine.State;
        switch (id)
        {
            case "monster-set":
                Assert.False(before.NormalSummonUsed);
                var result = SetMonster(engine, 7716, 2);
                Assert.IsType<MonsterSet>(Assert.Single(result.Events));
                Assert.Equal(BattlePosition.FaceDownDefense, engine.State.GetPlayer(PlayerId.One).MonsterZones[2]!.Position);
                Assert.True(engine.State.NormalSummonUsed);
                break;
            case "monster-set-shares-allowance-ui":
                Assert.True(before.NormalSummonUsed);
                int attempted = Instance(engine, 9979);
                Reject(engine, new NormalSummon(PlayerId.One, attempted, 0), DuelError.NormalSummonLimitReached);
                Reject(engine, new SetMonster(PlayerId.One, attempted, 0), DuelError.NormalSummonLimitReached);
                break;
            case "spell-set-after-monster-set":
                Assert.True(before.NormalSummonUsed);
                Assert.IsType<SpellTrapSet>(Assert.Single(SetSpellTrap(engine, 7747, 2).Events));
                Assert.True(engine.State.NormalSummonUsed);
                break;
            case "occupied-monster-set-ui":
                Assert.False(before.NormalSummonUsed); // Isolate occupancy from the used-allowance rejection.
                Reject(engine, new SetMonster(PlayerId.One, Instance(engine, 6151), 2), DuelError.MonsterZoneOccupied);
                Assert.False(engine.State.NormalSummonUsed);
                break;
            case "spell-set-preserves-allowance-ui":
                Assert.False(before.NormalSummonUsed);
                Assert.IsType<SpellTrapSet>(Assert.Single(SetSpellTrap(engine, 7747, 2).Events));
                Assert.False(engine.State.NormalSummonUsed);
                Assert.Contains(engine.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == 7716);
                // Original Krebons menus 058/064 still offer both Summon and Set.
                break;
            case "occupied-backrow-set-ui":
                Assert.False(before.NormalSummonUsed);
                Reject(engine, new SetSpellTrap(PlayerId.One, Instance(engine, 5217), 2), DuelError.SpellTrapZoneOccupied);
                break;
            case "opponent-monster-spell-trap-set-sequence":
                Execute(engine, new ChangePhase(PlayerId.Two, DuelPhase.Main1));
                Assert.IsType<MonsterSet>(Assert.Single(SetMonster(engine, 6109, 2).Events));
                Assert.True(engine.State.NormalSummonUsed);
                Assert.IsType<SpellTrapSet>(Assert.Single(SetSpellTrap(engine, 4818, 1).Events));
                Assert.IsType<SpellTrapSet>(Assert.Single(SetSpellTrap(engine, 4664, 4).Events));
                Assert.Equal(BattlePosition.FaceDownDefense, engine.State.GetPlayer(PlayerId.Two).MonsterZones[2]!.Position);
                Finish(engine);
                break;
            default:
                throw new ArgumentException("Unknown set reference case.");
        }
        AssertProjection(scenario.GetProperty("after"), engine.State, backrowOnly);
        AssertActionDeltas(scenario, before, engine.State);
        Assert.Null(engine.State.Outcome);
    }

    private static DuelEngine Engine(JsonElement fixture, bool backrowOnly)
    {
        var cards = fixture.GetProperty("cards").EnumerateArray().Select(row =>
        {
            uint id = row.GetProperty("cardId").GetUInt32();
            uint level = row.GetProperty("level").GetUInt32();
            return new CardDefinition(id, "Synthetic setting-only reference input", "", new(0, 0, level),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, level << 11, 0));
        }).Append(new CardDefinition(1, "Synthetic filler", "", new(0, 0, 4),
            new(0, 0, 0, 0, 0, 0, 0), new(1, 1, 4u << 11, 0))).ToArray();
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-setting-reference-projection"), cards);
        uint[] spells = [7747, 5217, 4842, 4909, 4815, 4818];
        var rules = cards.ToDictionary(c => c.GameCardId, c => spells.Contains(c.GameCardId)
            ? new DuelCardRules(CardCategory.Spell, AllowsSpellTrapSet: true)
            : c.GameCardId == 4664 ? new DuelCardRules(CardCategory.Trap, AllowsSpellTrapSet: true)
            : new DuelCardRules(CardCategory.Monster, AllowsNormalSummon: true));
        uint[] first = backrowOnly
            ? [7747, 7716, 5217, .. Enumerable.Repeat(1u, 37)]
            : [9979, 7716, 9979, 6151, 7747, 7716, 9723, 5217, .. Enumerable.Repeat(1u, 32)];
        uint[] second = [4842, 4909, 4815, 4818, 4028, 4664, 6109, .. Enumerable.Repeat(1u, 33)];
        return new(catalog, rules, first, second);
    }

    private static void PrepareTurnThreeMain2(DuelEngine engine)
    {
        Execute(engine, new NormalSummon(PlayerId.One, Instance(engine, 7716), 2));
        Finish(engine);
        EnterMain(engine);
        SetSpellTrap(engine, 4909, 2);
        SetSpellTrap(engine, 4815, 3);
        Finish(engine);
        EnterMain(engine);
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Execute(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
    }

    private static int Instance(DuelEngine engine, uint id) => engine.State.GetPlayer(engine.State.ActivePlayer)
        .Hand.First(c => c.GameCardId == id).InstanceId;
    private static DuelActionResult SetMonster(DuelEngine engine, uint id, int zone) => Execute(engine,
        new SetMonster(engine.State.ActivePlayer, Instance(engine, id), zone));
    private static DuelActionResult SetSpellTrap(DuelEngine engine, uint id, int zone) => Execute(engine,
        new SetSpellTrap(engine.State.ActivePlayer, Instance(engine, id), zone));
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
        Execute(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Execute(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[0].InstanceId));
        Execute(engine, new EndTurn(actor));
    }
    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        string serialized = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Equal(serialized, JsonSerializer.Serialize(engine.State));
        Assert.Empty(result.Events);
    }
    private static void AssertProjection(JsonElement expected, DuelState state, bool backrowOnly)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)state.ActivePlayer);
        Assert.Equal(expected.GetProperty("phase").GetString(), state.Phase.ToString());
        if (!backrowOnly) Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), state.TurnNumber);
        for (int i = 0; i < 2; i++)
        {
            var native = expected.GetProperty("players")[i];
            var managed = state.GetPlayer((PlayerId)i);
            if (backrowOnly)
            {
                // Native turn-seven LP and unrelated cards come from earlier original effects.
                if (i == 0) Assert.Equal(Ids(native, "spellTrapCardIds"), managed.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
                continue;
            }
            Assert.Equal(native.GetProperty("lifePoints").GetInt32(), managed.LifePoints);
            Assert.Equal(native.GetProperty("handCount").GetInt32(), managed.Hand.Count);
            Assert.Equal(native.GetProperty("deckCount").GetInt32(), managed.Deck.Count);
            Assert.Equal(Ids(native, "handCardIds"), managed.Hand.Select(c => c.GameCardId));
            Assert.Equal(Ids(native, "monsterCardIds"), managed.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
            Assert.Equal(Ids(native, "spellTrapCardIds"), managed.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
            Assert.Equal(Ids(native, "graveyardCardIds"), managed.Graveyard.Select(c => c.GameCardId));
        }
    }
    private static IEnumerable<uint> Ids(JsonElement player, string field) => player.GetProperty(field).EnumerateArray().Select(v => v.GetUInt32());
    private static void AssertActionDeltas(JsonElement scenario, DuelState before, DuelState after)
    {
        for (int i = 0; i < 2; i++)
        {
            var first = scenario.GetProperty("before").GetProperty("players")[i];
            var last = scenario.GetProperty("after").GetProperty("players")[i];
            var p = before.GetPlayer((PlayerId)i);
            var q = after.GetPlayer((PlayerId)i);
            Assert.Equal(last.GetProperty("handCount").GetInt32() - first.GetProperty("handCount").GetInt32(), q.Hand.Count - p.Hand.Count);
            Assert.Equal(last.GetProperty("deckCount").GetInt32() - first.GetProperty("deckCount").GetInt32(), q.Deck.Count - p.Deck.Count);
            Assert.Equal(last.GetProperty("lifePoints").GetInt32() - first.GetProperty("lifePoints").GetInt32(), q.LifePoints - p.LifePoints);
        }
    }
}
