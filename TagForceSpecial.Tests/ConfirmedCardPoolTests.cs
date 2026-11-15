using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;

namespace TagForceSpecial.Tests;

/// <summary>The confirmed card waves as the PC deck pool: regulation, original copy limits, preview decks and engine loading.</summary>
public sealed class ConfirmedCardPoolTests
{
    /// <summary>Probe stats for every pool card, stamped with the reviewed source build the pool policy requires.</summary>
    private static CardCatalog Catalog()
    {
        var ritualLevels = CardFamilies.All.SelectMany(f => f.SummonEffects).SelectMany(s => s.RitualRecipes).ToDictionary(r => r.TargetCardId, r => r.RequiredLevel);
        return new(new(TfspCardMetadata.CardInfoSha256, "synthetic-confirmed-pool"),
            CardPool.Rules.Keys.Concat(CardFamilies.All.SelectMany(f => f.RequiredCardIds)).Distinct().Order()
                .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, ritualLevels.GetValueOrDefault(id, 4u)), new(1, 1, 1, 1, 0, 0, 0))));
    }

    private static DeckDefinition PoolDeck(IEnumerable<uint> main, IEnumerable<uint>? extra = null)
    {
        var preview = InitialOpponentDecks.Create(0);
        return new(preview.DeckId, "1", preview.OwnerId, preview.CatalogId, preview.CatalogRevision, preview.SourceBuild,
            ConfirmedCardPool.PolicyId, ConfirmedCardPool.PolicyRevision, main, extra);
    }

    private static DeckValidation Validate(DeckDefinition deck, CardCatalog catalog) =>
        DeckValidator.Validate(deck, catalog, ConfirmedCardPool.PolicyFor(deck), ConfirmedCardPool.CollectionFor(deck));

    [Fact]
    public void BuiltInRestrictionTableMatchesTheDecodedSeed()
    {
        var entries = BuiltInRestrictions.Entries;
        Assert.Equal(152, entries.Count);
        Assert.Equal(152, entries.Select(e => e.CardId).Distinct().Count());
        Assert.Equal([63, 68, 21], new[] { 0, 1, 2 }.Select(n => entries.Count(e => e.Copies == n)));
        Assert.True(entries.Zip(entries.Skip(1)).All(p => p.First.Copies <= p.Second.Copies));
        Assert.Equal(entries[0].Copies, BuiltInRestrictions.CopiesAllowed(entries[0].CardId));
        Assert.All(PlayablePreview.Cards, c => Assert.Equal(3, BuiltInRestrictions.CopiesAllowed(c.CardId)));
    }

    [Fact]
    public void PolicyCoversEveryConfirmedCardWithItsPlacementAndOriginalLimit()
    {
        Assert.Equal(CardPool.Rules.Where(e => !e.Value.IsToken).Select(e => e.Key).Order(), ConfirmedCardPool.CardIds);
        var policy = ConfirmedCardPool.Policy;
        Assert.Equal(ConfirmedCardPool.CardIds.Count, policy.Cards.Count);
        Assert.All(ConfirmedCardPool.CardIds, id =>
        {
            var rule = policy.Cards[id];
            var home = CardPool.Rules[id].AllowsExtraDeck ? DeckPlacements.Extra : DeckPlacements.Main;
            Assert.Equal(home | DeckPlacements.Side, rule.Placements);
            Assert.Equal(BuiltInRestrictions.CopiesAllowed(id), rule.BanlistLimit);
            Assert.Equal($"card:{id}", rule.CopyGroup);
        });
        Assert.All(PlayablePreview.Cards, c => Assert.True(ConfirmedCardPool.Contains(c.CardId)));
    }

    [Fact]
    public void PreviewDecksMoveToThePoolAndUnknownRegulationsDoNot()
    {
        var catalog = Catalog();
        var preview = InitialOpponentDecks.Create(0);
        Assert.True(Validate(preview, catalog).CanPlay);
        var adopted = ConfirmedCardPool.AdoptPreview(preview);
        Assert.Equal(ConfirmedCardPool.PolicyId, adopted.PolicyId);
        Assert.Equal(preview.Main, adopted.Main);
        Assert.True(Validate(adopted, catalog).CanPlay);
        var unknown = new DeckDefinition(preview.DeckId, "1", preview.OwnerId, preview.CatalogId, preview.CatalogRevision,
            preview.SourceBuild, preview.PolicyId, "unreviewed", preview.Main);
        Assert.Same(unknown, ConfirmedCardPool.AdoptPreview(unknown));
        Assert.Equal(DeckLegality.Indeterminate, Validate(unknown, catalog).Legality);
    }

    [Fact]
    public void RestrictedCardsFollowTheBuiltInSeed()
    {
        uint restricted = ConfirmedCardPool.CardIds.First(id => BuiltInRestrictions.CopiesAllowed(id) < 3 && !ConfirmedCardPool.IsExtraDeck(id));
        int limit = BuiltInRestrictions.CopiesAllowed(restricted);
        var main = InitialOpponentDecks.Create(0).Main;
        Assert.Equal(limit != 0, Validate(PoolDeck(main.Concat(Enumerable.Repeat(restricted, limit))), Catalog()).CanPlay);
        var over = Validate(PoolDeck(main.Concat(Enumerable.Repeat(restricted, limit + 1))), Catalog());
        Assert.False(over.CanPlay);
        Assert.Contains(over.Diagnostics, d => d.Code == "BanlistLimitExceeded" && d.CardId == restricted);
    }

    [Fact]
    public void ExtraDeckMonstersStayInTheExtraDeckAndReachTheEngine()
    {
        var catalog = Catalog();
        uint extra = ConfirmedCardPool.CardIds.First(id => ConfirmedCardPool.IsExtraDeck(id) && BuiltInRestrictions.CopiesAllowed(id) == 3);
        var main = InitialOpponentDecks.Create(0).Main;
        var deck = PoolDeck(main, [extra]);
        Assert.True(Validate(deck, catalog).CanPlay);
        Assert.Contains(Validate(PoolDeck(main.Append(extra)), catalog).Diagnostics, d => d.Code == "WrongSection" && d.CardId == extra);
        var engine = ConfirmedCardPool.CreateEngine(catalog, deck, InitialOpponentDecks.Create(0, "local-opponent"), 7);
        Assert.Contains(engine.State.GetPlayer(PlayerId.One).ExtraDeck, c => c.GameCardId == extra);
        Assert.Throws<ArgumentException>(() => ConfirmedCardPool.CreateEngine(catalog, PoolDeck(main.Take(39).Append(90001u)), deck, 7));
    }
}
