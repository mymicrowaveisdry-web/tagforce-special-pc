using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

/// <summary>Checks over the whole registered pool, in place of per-card tests.</summary>
public sealed class CardPoolTests
{
    /// <summary>Probe stats, except that Ritual Monsters need their recipe's Level for an engine to accept them.</summary>
    private static CardCatalog Catalog()
    {
        var ritualLevels = CardFamilies.All.SelectMany(f => f.SummonEffects).SelectMany(s => s.RitualRecipes).ToDictionary(r => r.TargetCardId, r => r.RequiredLevel);
        return new(new(null, "synthetic-card-pool"), CardPool.Rules.Keys.Concat(CardFamilies.All.SelectMany(f => f.RequiredCardIds)).Distinct().Order()
            .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, ritualLevels.GetValueOrDefault(id, 4u)), new(1, 1, 1, 1, 0, 0, 0))));
    }

    [Fact]
    public void FamiliesAreDistinctAndDisjoint()
    {
        Assert.Equal(CardFamilies.All.Count, CardFamilies.All.Select(f => f.FamilyId).Distinct().Count());
        Assert.Equal(CardFamilies.All.Count, CardFamilies.All.Select(f => f.BatchId).Distinct().Count());
        var ids = CardFamilies.All.SelectMany(f => f.Cards).Select(c => c.CardId).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void EveryFamilyCardHasRulesAndReviewedMetadata()
    {
        foreach (var family in CardFamilies.All)
            foreach (var card in family.Cards)
            {
                Assert.Contains(card.CardId, family.Rules.Keys);
                Assert.Same(family.Reviewed[card.CardId], ReviewedCardMetadata.Reviewed[card.CardId]);
            }
    }

    [Fact]
    public void AllFamiliesLoadIntoOneEngine()
    {
        var deck = Enumerable.Repeat(NormalMonsterFamily.CardIds[0], 40).ToArray();
        var engine = CardPool.CreateEngine(Catalog(), deck, deck, [], [], 1);
        Assert.Null(engine.State.Outcome);
    }

    [Fact]
    public void SeededPoolDuelsHoldEngineInvariantsAndReplay()
    {
        var report = PoolDuels.Run(Catalog(), duels: 5, seed: 1);
        Assert.Empty(report.Failures);
        Assert.True(report.ActivatedCards > 0);
        Assert.Equal(report.ActivatedCards, report.Activated.Count);
        Assert.Equal(report.PlayedCards, report.Played.Count);
        Assert.Empty(report.NeverActivated.Intersect(report.Activated));
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void EveryFamilyMatchesTheOriginalCatalog()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        var catalog = ReviewedCardMetadata.ApplyReviewed(CardCatalogImporter.Read(stream, 3459, 11657));
        foreach (var family in CardFamilies.All) family.ValidateCatalog(catalog);
    }

    /// <summary>
    /// Summon restrictions in the decoded text have matching rules: "Cannot be Normal Summoned" needs no Normal Summon and a Special Summon
    /// policy (none from effects for "Must be" or "by other ways", a proper Summon first for "Must first be"), and a leading "Cannot be
    /// Special Summoned" needs that rule.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalReference")]
    public void EverySupportedMonsterFollowsItsSummonRestrictionText()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var stream = File.OpenRead(Path.Combine(root,
            "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp"));
        var text = CardCatalogImporter.Read(stream, 3459, 11657).Cards.ToDictionary(c => c.GameCardId, c => c.Description.ReplaceLineEndings(" "));
        var policies = CardFamilies.All.SelectMany(f => f.SpecialSummonCards).GroupBy(p => p.CardId).ToDictionary(g => g.Key, g => g.Last());
        List<string> wrong = [];
        int restricted = 0;
        foreach (var (id, rule) in CardPool.Rules.Where(r => r.Value.Category == CardCategory.Monster && !r.Value.AllowsExtraDeck && text.ContainsKey(r.Key)))
        {
            string description = text[id];
            if (description.Contains("Cannot be Normal Summoned", StringComparison.Ordinal))
            {
                restricted++;
                bool first = description.Contains("ust first be Special Summoned", StringComparison.Ordinal);
                if (rule.AllowsNormalSummon || !policies.TryGetValue(id, out var policy) ||
                    (first ? policy.RequiredProperProcedureId is null : policy.EffectSources != SpecialSummonSources.None))
                    wrong.Add($"{id} Nomi");
            }
            if (description.StartsWith("Cannot be Special Summoned", StringComparison.Ordinal) && !rule.CannotBeSpecialSummoned) wrong.Add($"{id} no Special Summon");
        }
        Assert.Empty(wrong);
        Assert.True(restricted >= 6, $"Only {restricted} restricted monsters were checked.");
    }
}
