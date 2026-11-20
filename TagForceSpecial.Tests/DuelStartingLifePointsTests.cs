using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>Duel setup options used by Free Duel: starting Life Points (rule change) and the player who goes first.</summary>
public sealed class DuelStartingLifePointsTests
{
    private static CardCatalog Catalog()
    {
        var ritualLevels = CardFamilies.All.SelectMany(f => f.SummonEffects).SelectMany(s => s.RitualRecipes).ToDictionary(r => r.TargetCardId, r => r.RequiredLevel);
        return new(new(TfspCardMetadata.CardInfoSha256, "synthetic-confirmed-pool"),
            CardPool.Rules.Keys.Concat(CardFamilies.All.SelectMany(f => f.RequiredCardIds)).Distinct().Order()
                .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, ritualLevels.GetValueOrDefault(id, 4u)), new(1, 1, 1, 1, 0, 0, 0))));
    }

    private static DuelEngine Engine(PlayerId first = PlayerId.One, int? lifePoints = null) => lifePoints is int lp
        ? ConfirmedCardPool.CreateEngine(Catalog(), InitialOpponentDecks.Create(0), InitialOpponentDecks.Create(0, "local-opponent"), 7, first, lp)
        : ConfirmedCardPool.CreateEngine(Catalog(), InitialOpponentDecks.Create(0), InitialOpponentDecks.Create(0, "local-opponent"), 7, first);

    [Fact]
    public void BothPlayersStartWith8000ByDefault()
    {
        var state = Engine().State;
        Assert.Equal((8000, 8000), (state.GetPlayer(PlayerId.One).LifePoints, state.GetPlayer(PlayerId.Two).LifePoints));
    }

    [Fact]
    public void RuleChangeStartsBothPlayersAt4000()
    {
        var state = Engine(lifePoints: 4000).State;
        Assert.Equal((4000, 4000), (state.GetPlayer(PlayerId.One).LifePoints, state.GetPlayer(PlayerId.Two).LifePoints));
    }

    [Fact]
    public void TheChosenPlayerGoesFirst() => Assert.Equal(PlayerId.Two, Engine(PlayerId.Two).State.ActivePlayer);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void StartingLifePointsOutsideTheRangeAreRejected(int lifePoints) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine(lifePoints: lifePoints));
}
