using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Token lifecycle tests.</summary>
public sealed class TokenLifecycleTests
{
    private const uint Attacker = 1, High = 2, Filler = 3, Spell = 800, Token = 900, Sheep = 901;

    private static CardCatalog Catalog() => new(new(null, "synthetic-token-lifecycle"),
    [
        new(Attacker, "Attacker", "", new(1500, 1000, 4)), new(High, "High", "", new(2000, 1000, 5)),
        new(Filler, "Filler", "", new(1000, 1000, 4)), new(Spell, "Token Spell", "", new(0, 0, 0)),
        new(Token, "Token", "", new(0, 0, 1)), new(Sheep, "Sheep", "", new(0, 0, 1))
    ]);

    private static Dictionary<uint, DuelCardRules> Rules() => new()
    {
        [Attacker] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true),
        [High] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true),
        [Filler] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true),
        [Spell] = new(CardCategory.Spell, AllowsSpellTrapSet: true),
        [Token] = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsTributeForSummon: true, IsToken: true),
        // Like the Sheep Token: "cannot be Tributed for a Tribute Summon".
        [Sheep] = new(CardCategory.Monster, AllowsBasicBattle: true, IsToken: true)
    };

    private static DuelEngine Engine(uint[] tokens, IEnumerable<uint>? first = null) =>
        new(Catalog(), Rules(), first ?? [Spell, Spell, High, Filler, Filler, .. Enumerable.Repeat(Filler, 35)],
            Enumerable.Repeat(Attacker, 40),
            effectDefinitions: [new("synthetic-tokens", Spell, EffectKind.NormalSpell, [new SummonTokensStep(tokens, BattlePosition.FaceUpDefense)])]);

    private static List<DuelEvent> Activate(DuelEngine e, int zone)
    {
        var spell = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Spell);
        Ok(e, new ActivateSpell(PlayerId.One, spell.InstanceId, zone));
        return Resolve(e);
    }

    private static MonsterBinding Bound(DuelEngine e, PlayerId p, int zone)
    {
        var m = e.State.GetPlayer(p).MonsterZones[zone]!;
        return new(p, zone, m.Card, m.PlacementId);
    }

    private static IEnumerable<CardInstance> AllZones(DuelState s) => s.Players.SelectMany(p => p.Deck.Concat(p.Hand)
        .Concat(p.Graveyard).Concat(p.Banished).Concat(p.ExtraDeck).Concat(p.FaceUpExtraDeck));

    [Fact]
    public void TokensTakeTheLowestFreeZonesWithFreshMonotonicInstances()
    {
        var e = Engine([Token, Sheep]);
        EnterMain(e);
        int first = e.State.NextInstanceId;
        Assert.Equal(81, first); // 80 deck cards were allocated 1..80
        var events = Activate(e, 0);
        var summoned = events.OfType<TokenSpecialSummoned>().ToArray();
        Assert.Equal([0, 1], summoned.Select(t => t.Zone));
        Assert.Equal([first, first + 1], summoned.Select(t => t.Monster.Card.InstanceId));
        Assert.Equal([Token, Sheep], summoned.Select(t => t.Monster.Card.GameCardId));
        Assert.All(summoned, t => Assert.Equal(BattlePosition.FaceUpDefense, t.Monster.Position));
        Assert.Equal(first + 2, e.State.NextInstanceId);
        Assert.DoesNotContain(AllZones(e.State), c => c.InstanceId >= first);
    }

    [Fact]
    public void TokenTributedForASummonCeasesToExistAndTheNoTributeTokenIsRefused()
    {
        var e = Engine([Token, Sheep]);
        EnterMain(e);
        Activate(e, 0);
        Next(e); Next(e);
        var high = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == High);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, high.InstanceId, 2, [Bound(e, PlayerId.One, 1)]), DuelError.TributeNotAllowed);
        var token = Bound(e, PlayerId.One, 0);
        var result = Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, high.InstanceId, 0, [token]));
        var tributed = Assert.Single(result.Events.OfType<MonsterTributed>());
        Assert.Equal(FieldCardDestination.Removed, tributed.FieldDestination);
        Assert.Equal(token.Card, Assert.Single(result.Events.OfType<TokenRemoved>()).Card);
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId is Token or Sheep);
        Assert.DoesNotContain(AllZones(e.State), c => c == token.Card);
        Assert.Equal(High, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void TokenDestroyedByBattleIsRemovedNotSentToTheGraveyardAndIdsAreNeverReused()
    {
        var e = Engine([Token, Sheep]);
        EnterMain(e);
        var firstTokens = Activate(e, 0).OfType<TokenSpecialSummoned>().Select(t => t.Monster.Card.InstanceId).ToArray();
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, e.State.GetPlayer(PlayerId.Two).Hand[0].InstanceId, 0));
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var battle = Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        var destroyed = Assert.Single(battle.Events.OfType<MonsterDestroyed>());
        Assert.Equal(FieldCardDestination.Removed, destroyed.FieldDestination);
        Assert.Single(battle.Events.OfType<TokenRemoved>());
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Equal([Spell], e.State.GetPlayer(PlayerId.One).Graveyard.Select(c => c.GameCardId)); // only the resolved Spell
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.One).LifePoints); // Defense Position: no battle damage
        Next(e);
        var again = Activate(e, 1).OfType<TokenSpecialSummoned>().ToArray();
        Assert.Equal([0, 2], again.Select(t => t.Zone)); // zone 0 freed, zone 1 still holds the Sheep
        Assert.All(again, t => Assert.True(t.Monster.Card.InstanceId > firstTokens.Max()));
    }

    [Fact]
    public void SummoningIsAllOrNothingWhenTooFewZonesAreFree()
    {
        var e = Engine([Token, Token, Sheep]);
        EnterMain(e);
        Activate(e, 0); // zones 0..2
        int next = e.State.NextInstanceId;
        var before = e.State.GetPlayer(PlayerId.One).MonsterZones.ToArray();
        var events = Activate(e, 1); // needs 3, only 2 free
        Assert.Equal(ChainSkipReason.SummonUnavailable, Assert.Single(events.OfType<ChainLinkSkipped>()).Reason);
        Assert.Empty(events.OfType<TokenSpecialSummoned>());
        Assert.Equal(before, e.State.GetPlayer(PlayerId.One).MonsterZones);
        Assert.Equal(next, e.State.NextInstanceId);
        Assert.Contains(events, ev => ev is ChainCompleted);
    }

    [Fact]
    public void TokenRulesDecksAndStepsFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new SummonTokensStep([], BattlePosition.FaceUpDefense));
        Assert.Throws<ArgumentException>(() => new SummonTokensStep(Enumerable.Repeat(Token, 6), BattlePosition.FaceUpDefense));
        Assert.Throws<ArgumentException>(() => new SummonTokensStep([0], BattlePosition.FaceUpDefense));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummonTokensStep([Token], BattlePosition.FaceDownDefense));
        // A Token cannot start in a Deck.
        Assert.Throws<ArgumentException>(() => Engine([Token], [Token, .. Enumerable.Repeat(Filler, 39)]));
        // A step may only name cards with Token rules.
        Assert.Throws<ArgumentException>(() => Engine([Filler]));
        // Token rules cannot opt into Normal or Tribute Summons or the Extra Deck.
        foreach (var bad in new DuelCardRules[]
        {
            new(CardCategory.Monster, AllowsNormalSummon: true, IsToken: true),
            new(CardCategory.Monster, AllowsNormalSummon: true, AllowsTributeSummon: true, IsToken: true),
            new(CardCategory.Monster, AllowsExtraDeck: true, IsToken: true),
            new(CardCategory.Spell, IsToken: true)
        })
        {
            var rules = Rules(); rules[Token] = bad;
            Assert.Throws<ArgumentException>(() => new DuelEngine(Catalog(), rules, Enumerable.Repeat(Filler, 40), Enumerable.Repeat(Filler, 40)));
        }
    }
}
