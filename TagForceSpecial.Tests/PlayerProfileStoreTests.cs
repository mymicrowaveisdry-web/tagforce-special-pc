using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public sealed class PlayerProfileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-profile-" + Guid.NewGuid().ToString("N"));
    private PlayerProfileStore Create() { Directory.CreateDirectory(root); return new PlayerProfileStore(new PrivateStorage(root)); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void AbsentProfileIsNotPresentAndDoesNotLoad()
    {
        var store = Create();
        Assert.False(store.Present);
        Assert.False(store.TryLoad());
        Assert.Null(store.Current);
    }

    [Fact]
    public void SavedProfileSurvivesReopening()
    {
        var options = new PlayerOptions(PlayerOptions.CurrentSchema, [2, 3, 15, 0, 3, 2, 0, 0, 3, 1, 1, 1]);
        Create().Save("Yuya", DuelistLevelChoice.BringItOn, options);
        var reopened = Create();
        Assert.True(reopened.Present);
        Assert.True(reopened.TryLoad());
        Assert.Equal("Yuya", reopened.Current!.Name);
        Assert.Equal(DuelistLevelChoice.BringItOn, reopened.Current.Level);
        Assert.Equal(options.Values, reopened.Current.Options!.Values);
    }

    [Fact]
    public void SchemaOneProfileLoadsWithNewGameOptionDefaults()
    {
        var store = Create();
        File.WriteAllText(Path.Combine(root, "profile.json"), "{\"schemaVersion\":1,\"name\":\"Yuya\",\"level\":1,\"savedUtc\":\"2026-11-05T00:00:00Z\"}"); // Numeric, as Save writes (1 = BeenThere).
        Assert.True(store.TryLoad());
        Assert.Equal(PlayerProfileStore.CurrentSchema, store.Current!.SchemaVersion);
        Assert.Equal(PlayerOptions.Defaults().Values, store.Current.Options!.Values);
        Assert.Equal(DuelistLevelChoice.BeenThere, store.Current.Level);
    }

    [Fact]
    public void OutOfRangeOptionsAreRefusedOnSaveAndOnLoad()
    {
        var store = Create();
        Assert.Throws<ArgumentException>(() => store.Save("Yuya", DuelistLevelChoice.FirstTimer, new PlayerOptions(1, [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])));
        File.WriteAllText(Path.Combine(root, "profile.json"),
            "{\"schemaVersion\":2,\"name\":\"Yuya\",\"level\":\"FirstTimer\",\"savedUtc\":\"2026-11-05T00:00:00Z\",\"options\":{\"schemaVersion\":1,\"values\":[0,16,0,0,0,0,0,0,0,0,0,0]}}");
        Assert.False(store.TryLoad());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"\",\"level\":\"FirstTimer\",\"savedUtc\":\"2026-11-05T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"TooLongName\",\"level\":\"FirstTimer\",\"savedUtc\":\"2026-11-05T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":2,\"name\":\"Yuya\",\"level\":\"FirstTimer\",\"savedUtc\":\"2026-11-05T00:00:00Z\"}")]
    public void InvalidProfileIsPresentButFailsToLoadSoContinueReportsNoData(string content)
    {
        var store = Create();
        File.WriteAllText(Path.Combine(root, "profile.json"), content);
        Assert.True(store.Present);
        Assert.False(store.TryLoad());
        Assert.Null(store.Current);
    }
}
