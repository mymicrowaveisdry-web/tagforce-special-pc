using System.Security.Cryptography;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import;
using TagForceSpecial.PC.Game;

namespace TagForceSpecial.Tests;

public sealed class DecoderIdentityTests
{
    private static string FileHash()
    {
        using var stream = File.OpenRead(typeof(GimTextureDecoder).Assembly.Location);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    [Fact]
    public void HostsWithADecoderFileHashItAndAcceptOnlyTheMatchingLauncherValue()
    {
        string actual = FileHash();
        Assert.Equal(actual, DecoderIdentity.Sha256);
        Assert.Equal(actual, ToolIdentity.DecoderSha256);
        DecoderIdentity.Supply(actual);
        Assert.Throws<InvalidOperationException>(() => DecoderIdentity.Supply(new string('0', 64)));
        Assert.Equal(actual, DecoderIdentity.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void MalformedLauncherValuesAreRejected(string value) =>
        Assert.Throws<ArgumentException>(() => DecoderIdentity.Supply(value));

    [Fact]
    public void TheGameHostAcceptsTheWorkbenchDecoderSwitch()
    {
        string hash = new('a', 64);
        Assert.Equal(hash, HostOptions.Parse(["--game", "--data-root", Path.GetTempPath(), "--decoder-sha256", hash]).DecoderSha256);
        Assert.Null(HostOptions.Parse(["--game"]).DecoderSha256);
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--decoder-sha256"]));
    }
}
