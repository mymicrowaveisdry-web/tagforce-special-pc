namespace TagForceSpecial.PC.Setup;

/// <summary>A process that plays the game: its executable and the arguments that come before the game's own switches
/// (for example <c>--path &lt;project&gt;</c> when the Godot editor runs the project).</summary>
public sealed record GameHost(string Executable, IReadOnlyList<string> Arguments);

/// <summary>The command-line contract between the Workbench and the game host, in both directions.</summary>
public static class GameHostCommand
{
    public const string HostSwitch = "--game-host";
    public const string HostArgumentSwitch = "--game-host-arg";

    /// <summary>Workbench arguments naming the host that opened it, so Play Game starts that same host.</summary>
    public static IEnumerable<string> ToWorkbench(GameHost host)
    {
        yield return HostSwitch;
        yield return host.Executable;
        foreach (string argument in host.Arguments)
        {
            yield return HostArgumentSwitch;
            yield return argument;
        }
    }

    /// <summary>The host named on the Workbench command line, or null when none was.</summary>
    public static GameHost? FromWorkbench(IReadOnlyList<string> args)
    {
        string? executable = null;
        var arguments = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == HostSwitch && i + 1 < args.Count) executable = Path.GetFullPath(args[++i]);
            else if (args[i] == HostArgumentSwitch && i + 1 < args.Count) arguments.Add(args[++i]);
        }
        return executable is null ? null : new(executable, arguments);
    }

    /// <summary>Arguments that start a game from the Workbench. The game's switches follow "--", which the engine passes through untouched.</summary>
    public static IEnumerable<string> PlayArguments(GameHost host, string dataRoot, string decoderSha256) =>
        [.. host.Arguments, "--", "--game", "--data-root", dataRoot, "--decoder-sha256", decoderSha256];
}
