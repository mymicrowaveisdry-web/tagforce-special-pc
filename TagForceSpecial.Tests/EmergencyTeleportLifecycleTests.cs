using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>
/// Scoped PSP End Phase comparisons for the Emergency Teleport definition; guards/events/identity are synthetic
/// managed contracts.
/// </summary>
public sealed class EmergencyTeleportLifecycleTests
{
    private static DuelEngine Engine(PlayerId starter = PlayerId.One, bool allowBattle = false)
    {
        uint[] ids = [1, 9979, 7716, 6151, 7747, 9723, 5217];
        var catalog = new CardCatalog(new(new string('0', 64), "synthetic-lifecycle-reference-input"),
            ids.Select(id => new CardDefinition(id, $"Synthetic {id}", "", new(allowBattle && id == 9979 ? 2500 : 1200, 400, 2),
                new(0, 0, 0, 0, 0, 0, 0), new((int)id, id, 2u << 11, 0))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is 7747 or 5217 ? CardCategory.Spell : CardCategory.Monster,
            AllowsNormalSummon: id is not (7747 or 5217), AllowsBasicBattle: allowBattle && id is 7716 or 9979,
            AllowsSpellTrapSet: id is 7747 or 5217));
        uint[] deck = [9979, 7716, 9979, 6151, 7747, 7716, 9723, 5217, 1, 7716, .. Enumerable.Repeat(1u, 30)];
        return new(catalog, rules, deck, deck, starter, effectDefinitions: [EmergencyTeleportTests.Definition], shuffleSeed: 7);
    }

    private static DuelActionResult Ok(DuelEngine engine, DuelCommand command)
    {
        long revision = engine.State.Revision;
        var result = engine.Execute(command);
        Assert.True(result.Succeeded, result.Error.ToString());
        Assert.Equal(revision + 1, result.State.Revision);
        return result;
    }

    private static void Reject(DuelEngine engine, DuelCommand command, DuelError error)
    {
        var before = engine.State;
        var serialized = JsonSerializer.Serialize(before);
        var result = engine.Execute(command);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.Error);
        Assert.Same(before, engine.State);
        Assert.Same(before, result.State);
        Assert.Equal(serialized, JsonSerializer.Serialize(result.State));
        Assert.Empty(result.Events);
    }

    private static void EnterMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ResolveDrawStep(actor));
        Ok(engine, new ChangePhase(actor, DuelPhase.Main1));
    }

    private static void NextMain(DuelEngine engine)
    {
        var actor = engine.State.ActivePlayer;
        Ok(engine, new ChangePhase(actor, DuelPhase.End));
        while (engine.State.GetPlayer(actor).Hand.Count > 6)
            Ok(engine, new DiscardForHandLimit(actor, engine.State.GetPlayer(actor).Hand[^1].InstanceId));
        Ok(engine, new EndTurn(actor));
        EnterMain(engine);
    }

    /// <summary>Activation through resolution, summoning the first Deck copy (the PSP-observed branch).</summary>
    private static DuelActionResult Resolve(DuelEngine engine, int zone = 1)
    {
        var actor = engine.State.ActivePlayer;
        var spell = engine.State.GetPlayer(actor).Hand.Single(c => c.GameCardId == ChainCardFamily.EmergencyTeleport);
        Ok(engine, new ActivateSpell(actor, spell.InstanceId, 2));
        var choice = EmergencyTeleportTests.ReachChoice(engine);
        var deckCopy = choice.Options.First(o => o.Source == DecisionCardSource.Deck).Card;
        return Ok(engine, new ChooseDefinitionSummon(actor, choice.DecisionId, deckCopy.InstanceId, zone, BattlePosition.FaceUpAttack));
    }

    private static void AssertProjection(JsonElement expected, DuelState actual)
    {
        Assert.Equal(expected.GetProperty("activePlayer").GetInt32(), (int)actual.ActivePlayer);
        Assert.Equal(expected.GetProperty("turnNumber").GetInt32(), actual.TurnNumber);
        Assert.Equal(expected.GetProperty("phase").GetString(), actual.Phase.ToString());
        AssertPlayerProjection(expected, actual);
    }

    private static void AssertPlayerProjection(JsonElement expected, DuelState actual)
    {
        var p = expected.GetProperty("players")[0];
        var player = actual.GetPlayer(PlayerId.One);
        uint[] Ids(string property) => p.GetProperty(property).EnumerateArray().Select(x => x.GetUInt32()).ToArray();
        Assert.Equal(p.GetProperty("handCount").GetInt32(), player.Hand.Count);
        Assert.Equal(p.GetProperty("deckCount").GetInt32(), player.Deck.Count);
        Assert.Equal(Ids("handCardIds"), player.Hand.Select(c => c.GameCardId));
        Assert.Equal(Ids("monsterCardIds"), player.MonsterZones.Select(m => m?.Card.GameCardId ?? 0));
        Assert.Equal(Ids("spellTrapCardIds"), player.SpellTrapZones.Select(c => c?.GameCardId ?? 0));
        Assert.Equal(Ids("graveyardCardIds"), player.Graveyard.Select(c => c.GameCardId));
        Assert.Equal(Ids("banishedCardIds"), player.Banished.Select(c => c.GameCardId));
    }

    private static void ReachReferenceMain(DuelEngine engine, bool syntheticDefender = false)
    {
        EnterMain(engine);
        while (engine.State.TurnNumber < 7)
        {
            if (engine.State.ActivePlayer == PlayerId.One && engine.State.TurnNumber is 1 or 3)
            {
                uint id = engine.State.TurnNumber == 1 ? 7716u : 6151u;
                var card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
                Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, engine.State.TurnNumber == 1 ? 2 : 3));
            }
            if (syntheticDefender && engine.State.TurnNumber == 2)
            {
                var card = engine.State.GetPlayer(PlayerId.Two).Hand.First(c => c.GameCardId == 9979);
                Ok(engine, new NormalSummon(PlayerId.Two, card.InstanceId, 2));
            }
            NextMain(engine);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Category", "LocalReference")]
    public void EndPhaseMatchesRepeatedOriginalDeckBranch(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "emergency-teleport-lifecycle.json")));
        var fixture = doc.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        Assert.False(fixture.GetProperty("nativeMatchClaim").GetBoolean());
        var pair = fixture.GetProperty("captures")[repeat];
        JsonElement Projection(string stage) => fixture.GetProperty("snapshots")
            .GetProperty(pair.GetProperty(stage).GetString()!).GetProperty("projection");
        var engine = Engine();
        ReachReferenceMain(engine);
        Resolve(engine);
        var before = engine.State;
        AssertProjection(Projection("before"), before);
        var player = before.GetPlayer(PlayerId.One);
        var target = player.MonsterZones[1]!.Card;
        var earlierCopy = player.MonsterZones[2];
        var handCopy = player.Hand.Single(c => c.GameCardId == 7716);
        var result = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        AssertProjection(Projection("after"), result.State);
        Assert.Collection(result.Events, e => Assert.IsType<PhaseChanged>(e), e =>
        {
            var banished = Assert.IsType<MonsterBanished>(e);
            Assert.Equal(target, banished.Card);
            Assert.Equal(1, banished.Zone);
        });
        var after = result.State.GetPlayer(PlayerId.One);
        Assert.Same(earlierCopy, after.MonsterZones[2]);
        Assert.Contains(handCopy, after.Hand);
        Assert.Equal(target, Assert.Single(after.Banished));
        Assert.Empty(result.State.PendingEndPhaseBanishments);
        Assert.False(result.State.ResearchSliceComplete);
        Assert.Null(result.State.Outcome);
        Assert.Equal(before.NormalSummonUsed, result.State.NormalSummonUsed);
        Assert.Same(before.GetPlayer(PlayerId.Two), result.State.GetPlayer(PlayerId.Two));
        Assert.Equal(player.LifePoints, after.LifePoints);
        Assert.Equal(player.Deck, after.Deck);
        Assert.Equal(player.Graveyard, after.Graveyard);
        Assert.NotNull(player.MonsterZones[1]);
        Assert.Empty(player.Banished);
        Assert.Single(before.PendingEndPhaseBanishments);
        Assert.Throws<NotSupportedException>(() => ((IList<CardInstance>)after.Banished).Clear());
    }

    [Theory]
    [InlineData(PlayerId.One, 0)]
    [InlineData(PlayerId.Two, 4)]
    public void SyntheticLifecycleBindsChosenInstanceAndPreservesAllowance(PlayerId actor, int zone)
    {
        var engine = Engine(actor);
        EnterMain(engine);
        var before = engine.State;
        var resolution = Resolve(engine, zone);
        Assert.Collection(resolution.Events, e => Assert.IsType<MonsterSpecialSummoned>(e),
            e => Assert.IsType<EndPhaseBanishmentScheduled>(e), e => Assert.IsType<DeckShuffled>(e),
            e => Assert.IsType<ChainLinkResolved>(e), e => Assert.IsType<SpellSentToGraveyard>(e), e => Assert.IsType<ChainCompleted>(e));
        var delayed = Assert.Single(engine.State.PendingEndPhaseBanishments);
        Assert.Equal(actor, delayed.Player);
        Assert.Equal(engine.State.GetPlayer(actor).MonsterZones[zone]!.Card, delayed.Monster);
        Assert.Equal(1, delayed.DueTurn);
        Assert.Equal(engine.State.GetPlayer(actor).MonsterZones[zone]!.PlacementId, delayed.PlacementId);
        Assert.False(engine.State.NormalSummonUsed);
        Assert.Empty(before.PendingEndPhaseBanishments);
        Assert.Throws<NotSupportedException>(() => ((IList<PendingEndPhaseBanishment>)engine.State.PendingEndPhaseBanishments).Clear());
        var result = Ok(engine, new ChangePhase(actor, DuelPhase.End));
        Assert.Equal(delayed.Monster, Assert.Single(result.State.GetPlayer(actor).Banished));
        Assert.Equal(delayed.Spell, Assert.Single(result.State.GetPlayer(actor).Graveyard));
        Assert.Null(result.State.GetPlayer(actor).MonsterZones[zone]);
        Assert.False(result.State.NormalSummonUsed);
    }

    [Fact]
    public void OrdinaryCommandsContinueWhileABanishmentIsPending()
    {
        var engine = Engine();
        EnterMain(engine);
        Resolve(engine);
        var player = engine.State.GetPlayer(PlayerId.One);
        Reject(engine, new ChangePhase(PlayerId.Two, DuelPhase.End), DuelError.NotActivePlayer);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle), DuelError.FirstTurnBattleNotAllowed);
        Reject(engine, new DeclareAttack(PlayerId.One, 1), DuelError.WrongPhase);
        Reject(engine, new ChangeMonsterPosition(PlayerId.One, 1, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
        Ok(engine, new NormalSummon(PlayerId.One, player.Hand.First(c => c.GameCardId == 9979).InstanceId, 0));
        Assert.Single(engine.State.PendingEndPhaseBanishments);
        var end = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Single(end.Events.OfType<MonsterBanished>());
        Assert.NotNull(engine.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Ok(engine, new EndTurn(PlayerId.One));
    }

    [Fact]
    public void PendingChoicesCannotEnterEndPhaseOrScheduleBanishmentEarly()
    {
        var engine = Engine();
        EnterMain(engine);
        var spell = engine.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == 7747);
        Ok(engine, new ActivateSpell(PlayerId.One, spell.InstanceId, 2));
        Assert.Empty(engine.State.PendingEndPhaseBanishments);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.ChainPending);
        var choice = EmergencyTeleportTests.ReachChoice(engine);
        var card = choice.Options[0].Card;
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DecisionPending);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, card.InstanceId, 1, BattlePosition.FaceDownDefense), DuelError.InvalidBattlePosition);
        Reject(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, card.InstanceId, 5, BattlePosition.FaceUpAttack), DuelError.InvalidMonsterZone);
        Assert.Empty(engine.State.PendingEndPhaseBanishments);
        Assert.Empty(engine.State.GetPlayer(PlayerId.One).Banished);
        Ok(engine, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Assert.Single(engine.State.PendingEndPhaseBanishments);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
    }
    [Fact]
    public void UsedNormalSummonAllowanceAndOtherMonsterSurviveLifecycle()
    {
        var engine = Engine();
        EnterMain(engine);
        var card = engine.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == 7716);
        Ok(engine, new NormalSummon(PlayerId.One, card.InstanceId, 2));
        Resolve(engine);
        Assert.True(engine.State.NormalSummonUsed);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.True(engine.State.NormalSummonUsed);
        Assert.Equal(card, engine.State.GetPlayer(PlayerId.One).MonsterZones[2]!.Card);
        Assert.NotEqual(card.InstanceId, Assert.Single(engine.State.GetPlayer(PlayerId.One).Banished).InstanceId);
    }

    [Fact]
    public void LifecycleDoesNotBypassBasicBattleOptIn()
    {
        var engine = Engine();
        EnterMain(engine);
        NextMain(engine);
        NextMain(engine);
        Resolve(engine);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Reject(engine, new DeclareAttack(PlayerId.One, 1), DuelError.UnsupportedBattleCard);
        Reject(engine, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.InvalidPhaseTransition);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        Assert.Equal(7716u, Assert.Single(engine.State.GetPlayer(PlayerId.One).Banished).GameCardId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Category", "LocalReference")]
    public void DestroyedSummonedInstanceStaysInGraveyardThroughEndPhase(int repeat)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "emergency-teleport-destroyed.json")));
        var fixture = doc.RootElement;
        Assert.Equal(2, fixture.GetProperty("repeatCount").GetInt32());
        Assert.False(fixture.GetProperty("nativeMatchClaim").GetBoolean());
        var pair = fixture.GetProperty("captures")[repeat];
        var snapshots = fixture.GetProperty("snapshots");
        JsonElement Projection(string name) => snapshots.GetProperty(name).GetProperty("projection");
        var engine = Engine(allowBattle: true);
        // Controlled synthetic 2500-ATK defender; native defender identity/summon is excluded.
        ReachReferenceMain(engine, syntheticDefender: true);
        Resolve(engine);
        var resolved = engine.State;
        var target = resolved.GetPlayer(PlayerId.One).MonsterZones[1]!.Card;
        var fieldCopy = resolved.GetPlayer(PlayerId.One).MonsterZones[2];
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Battle));
        Ok(engine, new DeclareAttack(PlayerId.One, 1, 2));
        int nativeDamage = Projection("001-battle-select").GetProperty("players")[0].GetProperty("lifePoints").GetInt32()
            - Projection("007-destroyed").GetProperty("players")[0].GetProperty("lifePoints").GetInt32();
        Assert.Equal(nativeDamage, resolved.GetPlayer(PlayerId.One).LifePoints - engine.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(target, engine.State.GetPlayer(PlayerId.One).Graveyard[^1]);
        Assert.Single(engine.State.PendingEndPhaseBanishments);
        Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        var beforeEnd = engine.State;
        AssertProjection(Projection(pair.GetProperty("before").GetString()!), beforeEnd);
        var result = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End));
        // Original after boundary is next Draw, before drawing. Compare card zones only.
        AssertPlayerProjection(Projection(pair.GetProperty("after").GetString()!), result.State);
        Assert.Collection(result.Events, e => Assert.IsType<PhaseChanged>(e), e =>
            Assert.Equal(target, Assert.IsType<EndPhaseBanishmentExpired>(e).Card));
        Assert.Empty(result.State.GetPlayer(PlayerId.One).Banished);
        Assert.Equal(beforeEnd.GetPlayer(PlayerId.One).Graveyard, result.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Same(fieldCopy, result.State.GetPlayer(PlayerId.One).MonsterZones[2]);
        Assert.Same(beforeEnd.GetPlayer(PlayerId.Two), result.State.GetPlayer(PlayerId.Two));
        Assert.Equal(beforeEnd.GetPlayer(PlayerId.One).LifePoints, result.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Empty(result.State.PendingEndPhaseBanishments);
        Assert.False(result.State.ResearchSliceComplete);
        Assert.Null(result.State.Outcome);
        Assert.NotNull(resolved.GetPlayer(PlayerId.One).MonsterZones[1]);
    }

    [Fact]
    public void LifecycleReplayProducesIdenticalSnapshotsAndEvents()
    {
        (string State, string Events) Run()
        {
            var engine = Engine();
            EnterMain(engine);
            Resolve(engine);
            var events = Ok(engine, new ChangePhase(PlayerId.One, DuelPhase.End)).Events;
            return (JsonSerializer.Serialize(engine.State), JsonSerializer.Serialize(events.Select(e => (e.GetType().Name, JsonSerializer.Serialize(e, e.GetType())))));
        }
        Assert.Equal(Run(), Run());
    }
}
