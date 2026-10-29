using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>
/// Retained original final-state projections only. No native window/replay equivalence claim.
/// Monster-battle printed stats substitute observed post-Dice values; Dice is not reconstructed.
/// Hidden hands, deck order, earlier graveyard contents and internal step timing are excluded.
/// </summary>
[Trait("Category", "LocalReference")]
public sealed class BattleFlowReferenceTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void StagedCombatMatchesBothRetainedDirectAndMonsterOutcomeObservations(bool monsterBattle, int repeat)
    {
        string filename = monsterBattle ? "duel-boundaries.json" : "duel-battle.json";
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename)));
        var root = fixture.RootElement;
        JsonElement before, after;
        if (monsterBattle)
        {
            var scenario = root.GetProperty("scenarios").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "monster-battle");
            var pair = scenario.GetProperty("captures")[repeat];
            before = root.GetProperty("snapshots").GetProperty(pair[0].GetString()!).GetProperty("projection");
            after = root.GetProperty("snapshots").GetProperty(pair[1].GetString()!).GetProperty("projection");
        }
        else
        {
            before = root.GetProperty("snapshots").GetProperty($"m3-attack-before-{repeat + 1}").GetProperty("projection");
            after = root.GetProperty("snapshots").GetProperty($"m3-attack-after-{repeat + 1}").GetProperty("projection");
        }
        uint[] ids = [7716, 9979, 6109, 1_000_000];
        var catalog = new CardCatalog(new(null, "retained-battle-stat-substitute"), ids.Select(id => new CardDefinition(id,
            $"Observed/probe {id}", "", id switch { 7716 => new(1200, 400, 2), 9979 => new(800, 2000, 4),
                6109 => new(1000, 700, 1), _ => new CardStatistics(1000, 1000, 2) })));
        var rules = ids.ToDictionary(id => id, _ => new DuelCardRules(CardCategory.Monster, true, true));
        uint[] one = [7716, 9979, .. Enumerable.Repeat(1_000_000u, 38)];
        uint[] two = [6109, .. Enumerable.Repeat(1_000_000u, 39)];
        var engine = new DuelEngine(catalog, rules, one, two, experimentalBattleFlow: true);
        int lastTurn = monsterBattle ? 4 : 3;
        for (int turn = 1; turn <= lastTurn; turn++)
        {
            EnterMain(engine); var actor = engine.State.ActivePlayer;
            uint? id = turn == 1 ? 7716u : monsterBattle && turn == 3 ? 9979u : monsterBattle && turn == 4 ? 6109u : null;
            if (id is { } cardId)
            {
                int card = engine.State.GetPlayer(actor).Hand.First(c => c.GameCardId == cardId).InstanceId;
                Ok(engine, new NormalSummon(actor, card, cardId == 9979 ? 3 : 2));
            }
            if (turn == lastTurn) break;
            Ok(engine, new ChangePhase(actor, DuelPhase.End)); Ok(engine, new EndTurn(actor));
        }
        Ok(engine, new ChangePhase(engine.State.ActivePlayer, DuelPhase.Battle)); Ready(engine);
        var baseline = engine.State;
        Assert.Equal(before.GetProperty("activePlayer").GetInt32(), (int)baseline.ActivePlayer);
        Assert.Equal(before.GetProperty("turnNumber").GetInt32(), baseline.TurnNumber);
        Ok(engine, new DeclareAttack(engine.State.ActivePlayer, 2, monsterBattle ? 3 : null));
        FinishAttack(engine);
        Assert.Equal(DuelPhase.Battle, engine.State.Phase);
        Assert.Null(engine.State.Outcome);
        for (int i = 0; i < 2; i++)
        {
            var player = engine.State.GetPlayer((PlayerId)i); var old = baseline.GetPlayer((PlayerId)i);
            var expectedBefore = before.GetProperty("players")[i]; var expectedAfter = after.GetProperty("players")[i];
            Assert.Equal(expectedAfter.GetProperty("lifePoints").GetInt32() - expectedBefore.GetProperty("lifePoints").GetInt32(), player.LifePoints - old.LifePoints);
            Assert.Equal(expectedAfter.GetProperty("monsterCardIds").EnumerateArray().Select(v => v.GetUInt32()),
                player.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
            if (monsterBattle)
                Assert.Equal(expectedAfter.GetProperty("graveyardCardIds").EnumerateArray().Skip(expectedBefore.GetProperty("graveyardCardIds").GetArrayLength()).Select(v => v.GetUInt32()),
                    player.Graveyard.Skip(old.Graveyard.Count).Select(c => c.GameCardId));
            else Assert.Equal(old.Graveyard, player.Graveyard);
        }
    }
}
