using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Tools;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Per-card wiring of all 665 reviewed Normal Monsters against the private original cardinfo.</summary>
public sealed class NormalMonsterFamilyLocalReferenceTests
{
    private const uint Donor = 4012, Filler = 4030;

    private static string CardInfoPath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        return Path.Combine(root, "data/extracted/e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707/files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp");
    }

    private static CardCatalog Imported()
    {
        using var stream = File.OpenRead(CardInfoPath());
        return CardCatalogImporter.Read(stream, 3459, 11657);
    }

    private static CardCatalog Original() => ReviewedCardMetadata.ApplyReviewed(Imported());

    [Fact]
    [Trait("Category", "LocalReference")]
    public void FrozenConverterStillProducesThePinnedCacheBytes()
    {
        var imported = Imported();
        Assert.Equal(8, imported.Cards.Count(c => c.Effects!.State == CardEffectState.None));
        string package = Path.Combine(Path.GetTempPath(), "tagforce-frozen-converter-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Same identity as the PC importer (SetupStore).
            CardCatalogPackage.Write(package, imported,
                new("tfsp-original", "1", "en", $"cardinfo-sha256:{imported.Source.ContainerSha256}"));
            foreach (var pinned in GameBuildProfile.English21.CacheFiles.Where(f => f.Path.StartsWith("catalog/", StringComparison.Ordinal)))
            {
                string file = Path.Combine(package, pinned.Path["catalog/".Length..]);
                Assert.Equal(pinned.Length, new FileInfo(file).Length);
                Assert.Equal(pinned.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            }
        }
        finally { if (Directory.Exists(package)) Directory.Delete(package, recursive: true); }
    }

    [Fact]
    [Trait("Category", "LocalReference")]
    public void OriginalCatalogMatchesTheReviewedFamilyAndTokenLists()
    {
        var catalog = Original();
        NormalMonsterFamily.ValidateCatalog(catalog);
        Assert.Equal(NormalMonsterFamily.CardIds.Order(), catalog.Cards.Where(c => c.Codes!.GetterKind == 0).Select(c => c.GameCardId));
        Assert.Equal(TfspTokens.Ids.Order(), catalog.Cards.Where(c => c.Codes!.GetterKind == 10).Select(c => c.GameCardId));
        Assert.All(TfspTokens.Ids, id => Assert.Contains("ummon", catalog[id].Description));
        Assert.All(TfspNormalMonsters.ArchfiendMembers, id => Assert.Contains("\"Archfiend\" card", catalog[id].Description));
    }

    /// <summary>
    /// Every member is summoned by its own procedure (Level 1–4 directly; 5–6 one Tribute; 7–8 two)
    /// and attacks directly for its real printed ATK.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalReference")]
    public void EveryFamilyCardSummonsByItsLevelAndDealsItsPrintedAttack()
    {
        var catalog = Original();
        foreach (uint id in NormalMonsterFamily.CardIds)
        {
            var stats = catalog[id].Statistics;
            int tributes = stats.Level <= 4 ? 0 : stats.Level <= 6 ? 1 : 2;
            uint[] deck = [.. Enumerable.Repeat(Donor, tributes), id, id, .. Enumerable.Repeat(Filler, 40)];
            var e = new DuelEngine(catalog, NormalMonsterFamily.Rules, deck.Take(40), Enumerable.Repeat(Filler, 40));
            EnterMain(e);
            for (int zone = 0; zone < tributes; zone++)
            {
                if (zone > 0) { Next(e); Next(e); }
                Ok(e, new NormalSummon(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Donor).InstanceId, zone));
            }
            Next(e); Next(e);
            var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
            int target = tributes;
            if (tributes == 0)
                Ok(e, new NormalSummon(PlayerId.One, card.InstanceId, target));
            else
            {
                var bound = Enumerable.Range(0, tributes).Select(z =>
                {
                    var m = e.State.GetPlayer(PlayerId.One).MonsterZones[z]!;
                    return new MonsterBinding(PlayerId.One, z, m.Card, m.PlacementId);
                }).ToArray();
                Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, card.InstanceId, target, bound));
                Assert.Equal(tributes, e.State.GetPlayer(PlayerId.One).Graveyard.Count);
            }
            Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Battle));
            Ok(e, new DeclareAttack(PlayerId.One, target));
            Assert.True(8000 - stats.Attack!.Value == e.State.GetPlayer(PlayerId.Two).LifePoints,
                $"{id}: expected {8000 - stats.Attack.Value} LP after the attack.");
        }
    }
}
