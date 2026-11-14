using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Recipes;
using TagForceSpecial.PC.Setup;

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
    public void ImportedRecipesSurviveReopeningAndRemovalOfTheSourceLibrary()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        Assert.Equal(1, new RecipeLibraryStore(destination).Import(source.File, cards));
        Directory.Delete(source.Source.Root, recursive: true);
        var reopened = new RecipeLibraryStore(new PrivateStorage(destination.Root)).OpenActive(cards)!;
        Assert.Equal("Imported source recipe", Assert.Single(reopened.Records).Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, reopened.ReadTexture("fixture")!.Value.Pixels);
    }

    [Fact]
    public void CorruptedReplacementRetainsPreviouslyActivatedRecipes()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        var store = new RecipeLibraryStore(destination);
        store.Import(source.File, cards);
        byte[] active = File.ReadAllBytes(destination.PathFor("recipes/active.json"));
        string blob = Directory.GetFiles(source.Source.PathFor("recipes/blobs")).Single();
        File.WriteAllBytes(blob, [4, 3, 2, 1]);
        Assert.Throws<InvalidDataException>(() => store.Import(source.File, cards));
        Assert.Equal(active, File.ReadAllBytes(destination.PathFor("recipes/active.json")));
        Assert.Equal("Imported source recipe", Assert.Single(store.OpenActive(cards)!.Records).Name);
    }

    [Fact]
    public void CanceledReplacementLeavesTheActiveGenerationUntouched()
    {
        var source = Source();
        var destination = new PrivateStorage(Path.Combine(root, "app"));
        var store = new RecipeLibraryStore(destination);
        store.Import(source.File, cards);
        byte[] active = File.ReadAllBytes(destination.PathFor("recipes/active.json"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.Import(source.File, cards, cancellation.Token));
        Assert.Equal(active, File.ReadAllBytes(destination.PathFor("recipes/active.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
