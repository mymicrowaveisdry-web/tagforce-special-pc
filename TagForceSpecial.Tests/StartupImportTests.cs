using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Recipes;

namespace TagForceSpecial.Tests;

public sealed class StartupImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-startup-import-" + Guid.NewGuid().ToString("N"));
    private readonly CardCatalog cards = new(new(null, "startup-recipe-fixture"), [new CardDefinition(1, "Fixture", "", new(1000, 1000, 4))]);
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private (PrivateStorage Source, string File) Source()
    {
        var source = new PrivateStorage(Path.Combine(root, "source"));
        byte[] pixels = [1, 2, 3, 4];
        string hash = Convert.ToHexStringLower(SHA256.HashData(pixels));
        Directory.CreateDirectory(source.PathFor("recipes/blobs"));
        File.WriteAllBytes(source.PathFor("recipes/blobs/" + hash + ".rgba"), pixels);
        var document = new RecipeDocument(1, GameBuildProfile.English21.BuildId, GameBuildProfile.English21.ImageSha256,
            Digest, Digest, Digest, [new(RecipeListKind.User, 7, "Imported source recipe", new DateTime(2020, 1, 1), [1], [], [], Digest, 0x08af0000)],
            [new("fixture", hash, 1, 1)], new([], [], [], 36, 29, 8, 174, 38, 155, 52));
        source.Write("recipes/viewer.json", document);
        return (source, source.PathFor("recipes/viewer.json"));
    }

    private StageResult Import(string library, PrivateStorage destination, bool reuse = true, CancellationToken cancellation = default) =>
        RecipeStage.Run(new StageCache(destination), library, GameBuildProfile.English21, cards, reuse, null, cancellation);

    [Fact]
    public void NormalLaunchAlwaysOpensImporterWhileExplicitPresentationModesRetainTheirEntryPoint()
    {
        Assert.True(HostOptions.Parse([]).OpenImporterOnLaunch);
        Assert.True(HostOptions.Parse(["--data-root", root]).OpenImporterOnLaunch);
        Assert.False(HostOptions.Parse(["--presentation-smoke"]).OpenImporterOnLaunch);
        Assert.False(HostOptions.Parse(["--asset-diagnostics"]).OpenImporterOnLaunch);
        Assert.False(HostOptions.Parse(["--capture", Path.Combine(root, "capture.png")]).OpenImporterOnLaunch);
    }

    [Fact]
    public void ImportedRecipesSurviveRemovalOfTheSourceLibraryAndOpenInTheGameCatalog()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        var result = Import(source.File, destination);
        Assert.Equal(StageStatus.Succeeded, result.Status);
        Directory.Delete(source.Source.Root, recursive: true);
        var reopened = RecipeCatalog.Open(new PrivateStorage(destination.PathFor(result.Output!.Directory)), cards)!;
        Assert.Equal("Imported source recipe", Assert.Single(reopened.Records).Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, reopened.ReadTexture("fixture")!.Value.Pixels);
    }

    [Fact]
    public void RepeatImportReusesTheVerifiedOutputAndACorruptedSourceBlobIsRejected()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        var first = Import(source.File, destination);
        Assert.Equal(StageStatus.Reused, Import(source.File, destination).Status);
        string blob = Directory.GetFiles(source.Source.PathFor("recipes/blobs")).Single();
        File.WriteAllBytes(blob, [4, 3, 2, 1]);
        Assert.Throws<InvalidDataException>(() => Import(source.File, destination, reuse: false));
        Assert.True(StageCache.Matches(destination.PathFor(first.Output!.Directory), first.Output.Artifacts));
    }

    [Fact]
    public void RecipesNamingUnknownCardsOrAnotherBuildAreRejected()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        var otherCards = new CardCatalog(new(null, "other"), [new CardDefinition(2, "Other", "", new(0, 0, 1))]);
        Assert.Throws<InvalidDataException>(() =>
            RecipeStage.Run(new StageCache(destination), source.File, GameBuildProfile.English21, otherCards, true, null, default));
        File.WriteAllText(source.File, File.ReadAllText(source.File).Replace(GameBuildProfile.English21.BuildId, "other-build"));
        Assert.Throws<InvalidDataException>(() => Import(source.File, destination));
    }

    [Fact]
    public void CancelledImportPublishesNothing()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Import(source.File, destination, cancellation: cancellation.Token));
        string stages = destination.PathFor("import/stages/recipes");
        Assert.Empty(Directory.Exists(stages) ? Directory.EnumerateFileSystemEntries(stages) : []);
    }

    [Fact]
    public void LegacyRecipeImportsAreFoundForCarryForward()
    {
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        Assert.Null(RecipeStage.FindLegacy(destination));
        string generation = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(destination.PathFor($"recipes/imports/{generation}/recipes"));
        File.WriteAllText(destination.PathFor($"recipes/imports/{generation}/recipes/viewer.json"), "{}");
        File.WriteAllText(destination.PathFor("recipes/active.json"), $$"""{"schemaVersion":1,"generation":"{{generation}}","documentSha256":"{{Digest}}"}""");
        Assert.Equal(destination.PathFor($"recipes/imports/{generation}/recipes/viewer.json"), RecipeStage.FindLegacy(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
