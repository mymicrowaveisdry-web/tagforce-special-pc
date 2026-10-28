using System.Text.Json;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.ChainScenario;
using static TagForceSpecial.Tests.DuelChainTests;

namespace TagForceSpecial.Tests;

/// <summary>Repeated retained native success projections; not general priority/negation equivalence.</summary>
[Trait("Category", "LocalReference")]
public sealed class ChainReferenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void VortexPaidAndResolvedStatesMatchRetainedOriginal(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chain-slice.json")));
        var fixture = doc.RootElement;
        Assert.False(fixture.GetProperty("nativeMatchClaim").GetBoolean());
        var pair = fixture.GetProperty("costPairs")[repeat];
        JsonElement Snapshot(string stage) => fixture.GetProperty("snapshots").GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine(5217);
        var initial = engine.State;
        Projection(Snapshot("before"), engine.State);
        var originalOpponent = engine.State.GetPlayer(PlayerId.Two);
        Commit(engine);
        Projection(Snapshot("paid"), engine.State);
        Assert.Equal(Snapshot("paid").GetProperty("players")[1].GetProperty("monsterCardIds").EnumerateArray().Select(c => c.GetUInt32()),
            engine.State.GetPlayer(PlayerId.Two).MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        PassBoth(engine); // PC protocol, native UI AI passes are not recovered priority rules.
        Resolve(engine);
        Projection(Snapshot("resolved"), engine.State);
        var opponent = engine.State.GetPlayer(PlayerId.Two);
        Assert.All(opponent.MonsterZones, Assert.Null);
        Assert.Equal(new uint[] { 4028 }, opponent.Graveyard.Select(c => c.GameCardId));
        Assert.Equal(originalOpponent.Hand, opponent.Hand);
        Assert.Equal(originalOpponent.Deck, opponent.Deck);
        Assert.Equal(originalOpponent.SpellTrapFields, opponent.SpellTrapFields);
        Assert.Equal(originalOpponent.LifePoints, opponent.LifePoints);
        Assert.Equal(initial.GetPlayer(PlayerId.One).LifePoints, engine.State.GetPlayer(PlayerId.One).LifePoints);
        // Original opponent's prior grave [6109], hand/deck and absolute LP are excluded.
        var nativeGrave = Snapshot("resolved").GetProperty("players")[1].GetProperty("graveyardCardIds");
        Assert.Equal(4028u, nativeGrave[nativeGrave.GetArrayLength() - 1].GetUInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ScapegoatTokensPrecedeTyphoonDestructionAndSurviveCleanup(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chain-slice.json")));
        var fixture = doc.RootElement;
        var pair = fixture.GetProperty("targetPairs")[repeat];
        JsonElement Snapshot(string stage) => fixture.GetProperty("snapshots").GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine(4909);
        var initial = engine.State;
        Projection(Snapshot("before"), initial);
        Commit(engine);
        var c = engine.State.PendingChain!;
        Ok(engine, new ActivateChainResponse(PlayerId.Two, c.DecisionId, 1));
        Projection(Snapshot("committed"), engine.State);
        PassBoth(engine);
        Resolve(engine);
        Compare("tokensResolved");
        Resolve(engine);
        Compare("resolved");
        Assert.True(engine.State.ResearchSliceComplete);
        Assert.Null(engine.State.Outcome);
        Assert.Equal(initial.NormalSummonUsed, engine.State.NormalSummonUsed);
        Assert.Equal(initial.GetPlayer(PlayerId.One).LifePoints, engine.State.GetPlayer(PlayerId.One).LifePoints);

        void Compare(string stage)
        {
            var expected = Snapshot(stage);
            Projection(expected, engine.State);
            var p = engine.State.GetPlayer(PlayerId.Two);
            var native = expected.GetProperty("players")[1];
            Assert.Equal(native.GetProperty("monsterCardIds").EnumerateArray().Select(c => c.GetUInt32()),
                p.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
            Assert.Equal(native.GetProperty("spellTrapCardIds").EnumerateArray().Select(c => c.GetUInt32()),
                p.SpellTrapZones.Select(m => m?.GameCardId ?? 0));
            Assert.Equal(native.GetProperty("graveyardCardIds").EnumerateArray().Skip(1).Select(c => c.GetUInt32()),
                p.Graveyard.Select(c => c.GameCardId));
            var before = initial.GetPlayer(PlayerId.Two);
            Assert.Equal(before.Hand, p.Hand);
            Assert.Equal(before.Deck, p.Deck);
            Assert.Equal(before.LifePoints, p.LifePoints);
        }
    }
}
