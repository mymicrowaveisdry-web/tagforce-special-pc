using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tests;

public sealed class AlbumReferenceStageTests : IDisposable
{
    private const string Build = "album-stage-test-build";
    private readonly string root = Path.Combine(Path.GetTempPath(), "tfsp-album-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Source => Path.Combine(root, "source", "game.iso");
    private string Installed => Path.Combine(Data, AlbumReferenceStage.Folder, AlbumReferenceStage.FileName);

    [Fact]
    public void InstallsAHashVerifiedPackageFoundBesideTheSource()
    {
        byte[] package = WritePackage(Path.Combine(root, "source", AlbumReferenceStage.Folder), Build);
        var result = AlbumReferenceStage.Run(new PrivateStorage(Data), Source, Build, CancellationToken.None);
        Assert.Equal(StageStatus.Succeeded, result.Status);
        Assert.Equal(package, File.ReadAllBytes(Installed));
        Assert.True(File.Exists(Installed + ".sha256"));
    }

    [Fact]
    public void KeepsAnAlreadyInstalledValidPackage()
    {
        byte[] package = WritePackage(Path.Combine(Data, AlbumReferenceStage.Folder), Build);
        var result = AlbumReferenceStage.Run(new PrivateStorage(Data), Source, Build, CancellationToken.None);
        Assert.Equal(StageStatus.Succeeded, result.Status);
        Assert.Equal(package, File.ReadAllBytes(Installed));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RejectsAPackageWithAWrongHashOrBuild(bool corruptHash, bool otherBuild)
    {
        string folder = Path.Combine(root, "source", AlbumReferenceStage.Folder);
        WritePackage(folder, otherBuild ? "another-build" : Build);
        if (corruptHash) File.WriteAllText(Path.Combine(folder, AlbumReferenceStage.FileName + ".sha256"), new string('0', 64));
        var result = AlbumReferenceStage.Run(new PrivateStorage(Data), Source, Build, CancellationToken.None);
        Assert.Equal(StageStatus.Warning, result.Status);
        Assert.False(File.Exists(Installed));
    }

    [Fact]
    public void MissingPackageIsAWarningWithRemediation()
    {
        var result = AlbumReferenceStage.Run(new PrivateStorage(Data), Source, Build, CancellationToken.None);
        Assert.Equal(StageStatus.Warning, result.Status);
        var check = Assert.Single(result.AllChecks);
        Assert.Equal("album-reference.package", check.Id);
        Assert.NotNull(check.Remediation);
    }

    private static byte[] WritePackage(string folder, string build)
    {
        Directory.CreateDirectory(folder);
        byte[] data = Encoding.UTF8.GetBytes($$"""{"buildId":"{{build}}","meshes":[]}""");
        File.WriteAllBytes(Path.Combine(folder, AlbumReferenceStage.FileName), data);
        File.WriteAllText(Path.Combine(folder, AlbumReferenceStage.FileName + ".sha256"), Convert.ToHexStringLower(SHA256.HashData(data)));
        return data;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
