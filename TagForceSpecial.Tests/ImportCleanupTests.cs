using TagForceSpecial.Import.Maintenance;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tests;

public sealed class ImportCleanupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-cleanup-" + Guid.NewGuid().ToString("N"));
    private PrivateStorage Storage => new(root);

    [Fact]
    public void LegacyCleanupPreservesPlayerDataAndOnlyClearsOldGeneration()
    {
        var storage = Storage;
        storage.WriteBytes("caches/old.bin", [1]);
        storage.WriteBytes("decks/player.json", [2]);
        storage.WriteBytes("fonts/font.pgf", [3]);
        storage.WriteBytes("preferences.json", "{\"activeGeneration\":\"old\",\"volume\":42}"u8.ToArray());
        var plan = Cleanup.PlanLegacy(storage);
        Assert.Single(plan.Items);
        Assert.True(File.Exists(storage.PathFor("caches/old.bin")));
        Cleanup.Execute(storage, plan);
        Assert.False(Directory.Exists(storage.PathFor("caches")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(storage.PathFor("decks/player.json")));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(storage.PathFor("fonts/font.pgf")));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(storage.PathFor("preferences.json")))!;
        Assert.Null(json["activeGeneration"]);
        Assert.Equal(42, json["volume"]!.GetValue<int>());
    }

    [Fact]
    public void LegacyRecipesBlockDeletionEvenWhenMetadataIsUnreadable()
    {
        var storage = Storage;
        storage.WriteBytes("recipes/active.json", "broken"u8.ToArray());
        var plan = Cleanup.PlanLegacy(storage);
        Assert.NotNull(plan.Blocked);
        Assert.Throws<InvalidOperationException>(() => Cleanup.Execute(storage, plan));
        Assert.True(File.Exists(storage.PathFor("recipes/active.json")));
    }

    [Fact]
    public void ForgedAndStalePlansCannotDeleteUnrelatedFiles()
    {
        var storage = Storage;
        storage.WriteBytes("decks/player.json", [1]);
        var forged = new CleanupPlan("stages", [new("decks", true, 1, "forged")], null);
        Assert.Throws<InvalidOperationException>(() => Cleanup.Execute(storage, forged));
        storage.WriteBytes("caches/a.bin", [1]);
        var stale = Cleanup.PlanLegacy(storage);
        storage.WriteBytes("caches/a.bin", [1, 2]);
        Assert.Throws<InvalidOperationException>(() => Cleanup.Execute(storage, stale));
        Assert.True(File.Exists(storage.PathFor("caches/a.bin")));
    }

    [Fact]
    public void CleanupCannotRunWhileAnImportHoldsTheLease()
    {
        var storage = Storage;
        var plan = Cleanup.PlanUnusedStages(storage);
        Directory.CreateDirectory(storage.PathFor("import"));
        using var lease = new FileStream(storage.PathFor("import/import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => Cleanup.Execute(storage, plan));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
