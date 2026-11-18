using System.Text.Json;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.Import;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.Tests;

public sealed class FoundationLocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void UserIsoCatalogStageAdmitsOnlyKnownLegalSelectedDecks()
    {
        string iso = Environment.GetEnvironmentVariable("TAGFORCE_ISO") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PPSSPP/PSP/GAME/Yu-Gi-Oh! ARC-V Tag Force Special (English v2.1).iso");
        var validated = GameSourceValidator.Open(iso);
        Assert.True(validated.IsValid, JsonSerializer.Serialize(validated.Diagnostics));
        using var source = validated.Source!;
        Assert.Equal(InitialDeckPolicy.SourceBuild, source.Profile.ImageSha256);
        string root = Path.Combine(Path.GetTempPath(), "tagforce-catalog-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new StageCache(new PrivateStorage(root));
            var result = CatalogStage.Run(cache, source, ToolIdentity.DecoderSha256, reuse: true, default);
            Assert.Equal(StageStatus.Succeeded, result.Status);
            Assert.True(source.IsUnchanged());
            Assert.Equal(StageStatus.Reused, CatalogStage.Run(cache, source, ToolIdentity.DecoderSha256, reuse: true, default).Status);
            var game = InstalledGame.FromCatalogDirectory(Path.Combine(root, result.Output!.Directory));
            Assert.Equal(7254, game.Catalog.Catalog.Count);
            // Opened catalogs carry the reviewed overlay while their pinned bytes stay unchanged.
            Assert.Equal(665, game.Catalog.Catalog.Cards.Count(c => c.Effects!.State == Cards.CardEffectState.None));
            NormalMonsterFamily.ValidateCatalog(game.Catalog.Catalog);
            var policy = InitialDeckPolicy.Create();
            var first = InitialDeckPolicy.Example(); var second = InitialDeckPolicy.Example("local-opponent");
            var firstCollection = InitialDeckPolicy.ExampleCollection(); var secondCollection = InitialDeckPolicy.ExampleCollection("local-opponent");
            Assert.True(SelectedDecks.Validate(game, policy, first, firstCollection, second, secondCollection).IsValid);
            var illegal = new DeckDefinition("over-copy", "1", first.OwnerId, first.CatalogId, first.CatalogRevision,
                first.SourceBuild, first.PolicyId, first.PolicyRevision, first.Main, side: [first.Main[0]]);
            var invalid = SelectedDecks.Validate(game, policy, first, firstCollection, illegal, firstCollection);
            Assert.False(invalid.IsValid); Assert.Equal(DeckLegality.Illegal, invalid.Second.Legality);
            var unknown = new DeckDefinition("unknown-regulation", "1", first.OwnerId, first.CatalogId, first.CatalogRevision,
                first.SourceBuild, first.PolicyId, "unreviewed", first.Main);
            Assert.Equal(DeckLegality.Indeterminate, DeckValidator.Validate(unknown, game.Catalog.Catalog, policy, firstCollection).Legality);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
