using Godot;
using TagForceSpecial.AI;
using TagForceSpecial.Assets;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Import;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.Launch;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Scenes;

/// <summary>Entry scene. Routes the launch the way the MonoGame host did: a normal launch opens the Workbench and quits;
/// <c>--game</c> (sent by the Workbench's Play Game) plays. Host switches come after "--" or "++" on the command line.</summary>
public partial class Boot : Control
{
    /// <summary>Exit code for a launch mode the Godot host does not provide yet (smokes, captures, diagnostics).</summary>
    private const int NotPorted = 2;

    public override void _Ready()
    {
        var (report, exitCode) = Route(OS.GetCmdlineUserArgs());
        GetNode<Label>("Status").Text = report;
        GD.Print(report);
        if (exitCode is { } code) GetTree().Quit(code);
    }

    private static (string Report, int? ExitCode) Route(string[] args)
    {
        Game.HostOptions options;
        try { options = Game.HostOptions.Parse(args); }
        catch (ArgumentException error) { return (error.Message, 1); }
        string decoder = AcceptDecoderIdentity(options.DecoderSha256);
        if (options.OpenImporterOnLaunch)
        {
            try { WorkbenchLauncher.Start(HostLocation.Workbench(), HostLocation.Self(), options.DataRoot); }
            catch (InvalidOperationException error) { return (error.Message, 1); }
            return ("Opening the importer workbench.", 0);
        }
        if (!options.LaunchGame) return ("This launch mode is not available in the Godot host yet.", NotPorted);
        return (StatusReport(decoder, options.DataRoot), null);
    }

    private static string StatusReport(string decoder, string? dataRoot)
    {
        Type[] anchors =
        [
            typeof(TfspCardMetadata),
            typeof(DuelEngine),
            typeof(ConfirmedCardPool),
            typeof(DeterministicPolicy),
            typeof(CardDetailReader),
            typeof(ToolIdentity),
        ];
        var lines = new List<string> { $"Tag Force Special Godot host, engine {Engine.GetVersionInfo()["string"]}" };
        lines.AddRange(anchors.Select(type => $"{type.Assembly.GetName().Name} {type.Assembly.GetName().Version}"));
        lines.Add(decoder);
        lines.Add(dataRoot is null ? "No --data-root given" : DescribeImport(dataRoot));
        return string.Join('\n', lines);
    }

    // Godot loads game assemblies from memory, so Assembly.Location is empty and the decoder
    // hash that import manifests record must come from the launching Workbench.
    private static string AcceptDecoderIdentity(string? supplied)
    {
        try
        {
            if (supplied is not null) DecoderIdentity.Supply(supplied);
            return DecoderIdentity.TryGetSha256(out string? hash)
                ? $"Decoder SHA-256 {hash}{(supplied is null ? "" : " (from launcher)")}"
                : "Decoder SHA-256 unavailable: launch from the Workbench or pass --decoder-sha256";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return $"Decoder SHA-256 rejected: {error.Message}";
        }
    }

    private static string DescribeImport(string dataRoot)
    {
        // PrivateStorage creates its root; only open a directory that already exists.
        if (!Directory.Exists(dataRoot)) return $"Data root {dataRoot}: not found";
        try
        {
            var result = InstalledGameReader.Open(new PrivateStorage(dataRoot));
            string issues = string.Join(", ", result.Issues.Select(issue => issue.Code));
            return $"Data root {dataRoot}: import {(result.Usable ? "usable" : "not usable")}{(issues.Length == 0 ? "" : $" ({issues})")}";
        }
        catch (Exception error) when (PrivateStorage.Recoverable(error))
        {
            return $"Data root {dataRoot}: {error.Message}";
        }
    }
}
