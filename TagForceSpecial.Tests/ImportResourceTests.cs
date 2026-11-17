using TagForceSpecial.Import.Resources;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tests;

public sealed class ImportResourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-resources-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void RecipeDiscoverySkipsAnUnrelatedBuildAndFindsTheMatchingLocalLibrary()
    {
        var wrong = new PrivateStorage(Path.Combine(root, "wrong"));
        var matching = new PrivateStorage(Path.Combine(root, "matching"));
        wrong.WriteBytes("recipes/viewer.json", "{\"buildId\":\"another-game\"}"u8.ToArray());
        matching.WriteBytes("recipes/viewer.json", "{\"buildId\":\"expected-build\"}"u8.ToArray());
        Assert.Equal(matching.PathFor("recipes/viewer.json"), ResourceDiscovery.FindRecipes([wrong.Root, matching.Root], "expected-build"));
        Assert.Null(ResourceDiscovery.FindRecipes([wrong.Root], "expected-build"));
    }
    [Fact]
    public void CorruptRecipeCandidateDoesNotHideALaterValidLibrary()
    {
        var broken = new PrivateStorage(Path.Combine(root, "broken"));
        var valid = new PrivateStorage(Path.Combine(root, "valid"));
        broken.WriteBytes("recipes/viewer.json", "broken"u8.ToArray());
        valid.WriteBytes("recipes/viewer.json", "{\"buildId\":\"expected\"}"u8.ToArray());
        Assert.Equal(valid.PathFor("recipes/viewer.json"), ResourceDiscovery.FindRecipes([broken.Root, valid.Root], "expected"));
    }
    [Fact]
    public void FontsRequireBothPinnedIdentitiesRatherThanFileNames()
    {
        var storage = new PrivateStorage(root);
        storage.WriteBytes(GameFonts.MenuFile, "fake ltn4"u8.ToArray());
        storage.WriteBytes(GameFonts.CardFile, "fake ltn0"u8.ToArray());
        Assert.False(GameFonts.Ready(storage));
        Assert.Equal("fake ltn4", File.ReadAllText(storage.PathFor(GameFonts.MenuFile)));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
