using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public sealed class GameHostCommandTests
{
    private static readonly string Executable = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Godot.exe"));
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "TagForceSpecial.PC");

    [Fact]
    public void TheWorkbenchRecoversTheHostThatOpenedItIncludingItsProjectArguments()
    {
        var host = new GameHost(Executable, ["--path", Project]);
        string[] workbenchArgs = ["Workbench.exe", .. GameHostCommand.ToWorkbench(host), "--data-root", Project];
        var recovered = GameHostCommand.FromWorkbench(workbenchArgs);
        Assert.NotNull(recovered);
        Assert.Equal(host.Executable, recovered.Executable);
        Assert.Equal(host.Arguments, recovered.Arguments);
        Assert.Null(GameHostCommand.FromWorkbench(["Workbench.exe", "--data-root", Project]));
    }

    [Fact]
    public void PlayGameSendsTheGameSwitchesAfterTheEngineSeparatorAndTheHostReadsThem()
    {
        string hash = new('a', 64);
        string[] args = [.. GameHostCommand.PlayArguments(new(Executable, ["--path", Project]), Project, hash)];
        Assert.Equal(["--path", Project, "--"], args[..3]);
        var options = HostOptions.Parse(args[3..]);
        Assert.True(options.LaunchGame);
        Assert.False(options.OpenImporterOnLaunch);
        Assert.Equal(Path.GetFullPath(Project), options.DataRoot);
        Assert.Equal(hash, options.DecoderSha256);
    }
}
