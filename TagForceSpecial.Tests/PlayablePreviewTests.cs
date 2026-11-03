using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class PlayablePreviewTests
{
    public static IEnumerable<object[]> Monsters() => new object[][]
    {
        [4012u, 1300, 1400, 4u], [4030u, 300, 200, 1u], [4032u, 1700, 1000, 4u],
        [4033u, 1200, 1500, 4u], [4071u, 1200, 800, 3u], [4084u, 1700, 1150, 4u],
        [5644u, 1900, 1600, 4u], [6960u, 2000, 100, 4u]
    };

    [Theory]
    [MemberData(nameof(Monsters))]
    public void EachNormalMonsterSetsRevealsAndLeavesByBattleWithInstanceIdentity(uint id, int atk, int def, uint level)
    {
        var catalog = Catalog();
        Assert.Equal(new CardStatistics(atk, def, level), catalog[id].Statistics);
        var e = new DuelEngine(catalog, PlayablePreview.Rules,
            Enumerable.Repeat(id, 40), Enumerable.Repeat(6960u, 40));
        EnterMain(e);
        var before = e.State;
        var card = before.GetPlayer(PlayerId.One).Hand[0];
        Ok(e, new SetMonster(PlayerId.One, card.InstanceId, 2));
        Assert.Equal(card, e.State.GetPlayer(PlayerId.One).MonsterZones[2]!.Card);
        Reject(e, new ChangeMonsterPosition(PlayerId.One, 2, BattlePosition.FaceUpAttack), DuelError.PositionChangeNotAllowed);
        Reject(e, new NormalSummon(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 1), DuelError.NormalSummonLimitReached);
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(e, new DeclareAttack(PlayerId.Two, 0, 2));
        Assert.Equal(card, Assert.Single(result.Events.OfType<MonsterRevealed>()).Card);
        Assert.Equal(card, Assert.Single(result.Events.OfType<MonsterDestroyed>()).Card);
        Assert.Contains(card, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[2]);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Contains(card, before.GetPlayer(PlayerId.One).Hand);
    }

    [Theory]
    [MemberData(nameof(Monsters))]
    public void EachNormalMonsterSummonsAttacksAndFlipSummonsOnLaterTurn(uint id, int atk, int def, uint level)
    {
        _ = def; _ = level;
        foreach (bool set in new[] { false, true })
        {
            var e = new DuelEngine(Catalog(), PlayablePreview.Rules, Enumerable.Repeat(id, 40), Enumerable.Repeat(4030u, 40));
            EnterMain(e);
            var card = e.State.GetPlayer(PlayerId.One).Hand[0];
            Ok(e, set ? new SetMonster(PlayerId.One, card.InstanceId, 0) : new NormalSummon(PlayerId.One, card.InstanceId, 0));
            Next(e); Next(e);
            if (set)
            {
                var flip = Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
                Assert.Equal(card, Assert.Single(flip.Events.OfType<MonsterFlipSummoned>()).Monster.Card);
            }
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            var result = Ok(e, new DeclareAttack(PlayerId.One, 0));
            Assert.Equal(atk, Assert.Single(result.Events.OfType<BattleDamageDealt>()).Damage);
            Assert.Equal(8000 - atk, e.State.GetPlayer(PlayerId.Two).LifePoints);
            Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.AlreadyAttacked);
        }
    }

    [Fact]
    public void PreviewDeckAdmissionRejectsUnknownIdsTooManyCopiesAndWrongSource()
    {
        var catalog = Catalog();
        Assert.Equal(14, PlayablePreview.Cards.Count);
        Assert.Equal(40, PlayablePreview.ExampleDeck.Count);
        Assert.All(PlayablePreview.ExampleDeck.GroupBy(x => x), g => Assert.InRange(g.Count(), 1, 3));
        PlayablePreview.CreateEngine(catalog, PlayablePreview.ExampleDeck, PlayablePreview.ExampleDeck);
        Assert.Throws<ArgumentException>(() => PlayablePreview.CreateEngine(catalog, Enumerable.Repeat(4012u, 40), PlayablePreview.ExampleDeck));
        Assert.Throws<ArgumentException>(() => PlayablePreview.CreateEngine(catalog, PlayablePreview.ExampleDeck.Skip(1), PlayablePreview.ExampleDeck));
        Assert.Throws<ArgumentException>(() => PlayablePreview.CreateEngine(catalog, PlayablePreview.ExampleDeck.Skip(1).Append(90001u), PlayablePreview.ExampleDeck));
        Assert.Throws<InvalidDataException>(() => PlayablePreview.ValidateCatalog(new(new(new string('f', 64), "unreviewed-build"), catalog.Cards)));
        Assert.Throws<InvalidDataException>(() => PlayablePreview.ValidateCatalog(new(catalog.Source, catalog.Cards.Select(c => c.GameCardId == 4084 ? c with { Effects = CardEffectBinding.Unknown } : c))));
    }

    [Fact]
    public void ManifestsKeepUnreviewedCardsUnsupportedAndPspStatusSeparate()
    {
        var catalog = new CardCatalog(new(null, "authored-inventory-test"),
            Catalog().Cards.Select((c, i) => c with { SourceRow = new(i + 1, c.GameCardId, 0, 0) }));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(CardSupportInventory.Build(catalog, catalog.Count + 1),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var root = json.RootElement;
        Assert.Equal(14, root.GetProperty("playablePreview").GetProperty("cards").GetArrayLength());
        var full = root.GetProperty("pspFidelity10");
        Assert.False(full.GetProperty("ready").GetBoolean());
        Assert.Equal(15, full.GetProperty("cards").GetArrayLength());
        Assert.Equal("unsupported", full.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("gameCardId").GetUInt32() == 90001).GetProperty("status").GetString());
        Assert.All(full.GetProperty("cards").EnumerateArray(), c => Assert.False(c.GetProperty("pspVerified").GetBoolean()));
        Assert.Throws<InvalidDataException>(() => CardSupportInventory.Build(catalog, catalog.Count));
    }

    [Fact]
    public void SupportedOriginalCardDuelReachesRealDefeatAndReplaysDeterministically()
    {
        var first = PreviewDuelScenario.Run(Catalog());
        var replay = PreviewDuelScenario.Run(Catalog());
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(DuelEndReason.LifePointsDepleted, first.Outcome.Reason);
        Assert.Equal(PlayerId.One, first.Outcome.Winner);
        Assert.Equal(0, first.FinalLifePoints[1]);
        Assert.True(first.SpellResolutions > 0);
        Assert.True(first.Battles > 0);
        Assert.True(first.DestroyedMonsters > 0);
    }
}
