using TagForceSpecial.Tools;

namespace TagForceSpecial.Tests;

public sealed class NativeByteProgressTests
{
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Code = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Evidence = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static ModuleInventory[] Inventories =>
    [
        new(new("EBOOT.BIN", "resident", "config/test.csv", 100, Source, Evidence),
            [new(0x08804000, 24), new(0x08804010, 16)])
    ];

    private static NativeFunctionMatch Valid => new("EBOOT.BIN", "0x08804000", 24,
        "game-c", Source, Code, Code, "config/match-evidence/example.json", Evidence);

    [Fact]
    public void ZeroBaselineAndReadableBar()
    {
        var r = NativeByteProgress.Measure(Inventories, []);
        Assert.Equal(0, r.MatchedBytes);
        Assert.Equal(0, r.MatchedFunctions);
        Assert.Equal("0.00%", NativeByteProgress.Percent(0, 100));
        Assert.Equal(new string('░', 24), NativeByteProgress.Bar(0, 100));
    }

    [Fact]
    public void RecordedCompleteCandidateCountsExactBytes()
    {
        var r = NativeByteProgress.Measure(Inventories, [Valid]);
        Assert.Equal(24, r.MatchedBytes);
        Assert.Equal(1, r.MatchedFunctions);
        Assert.Equal("24.00%", NativeByteProgress.Percent(24, 100));
    }

    [Fact]
    public void RejectsFalseMatchesAndOverlappingCandidates()
    {
        Assert.Throws<InvalidDataException>(() => NativeByteProgress.Measure(Inventories,
            [Valid with { RebuiltSha256 = Source }]));
        Assert.Throws<InvalidDataException>(() => NativeByteProgress.Measure(Inventories,
            [Valid with { Length = 20 }]));
        Assert.Throws<InvalidDataException>(() => NativeByteProgress.Measure(Inventories,
            [Valid, Valid]));
        Assert.Throws<InvalidDataException>(() => NativeByteProgress.Measure(Inventories,
            [Valid, Valid with { Address = "0x08804010", Length = 16 }]));
    }
}
