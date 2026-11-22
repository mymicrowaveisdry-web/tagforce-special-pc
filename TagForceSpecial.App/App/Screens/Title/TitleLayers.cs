using System.Text.Json;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>One measured title draw, tagged with the layer group it composes.</summary>
public sealed record TitleDraw(string Group, int Ordinal, string? AssetKey, int Primitive,
    float[][] Points, float[][] Uv, int[] Color, bool ClearStencil, bool WriteMask, bool Stencil, bool Additive, bool LinearSampling);

/// <summary>The measured title frame (title-frame.json): its layer groups in draw order and their draws.</summary>
public sealed record TitleFrame(int SchemaVersion, string BuildId, string FrameDumpSha256, string Scope, string[] Groups, TitleDraw[] Draws)
{
    public static TitleFrame LoadEmbedded()
    {
        using var stream = typeof(TitleFrame).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.title-frame.json")
            ?? throw new InvalidDataException("Missing measured title profile.");
        var frame = JsonSerializer.Deserialize<TitleFrame>(stream) ?? throw new InvalidDataException("Invalid title profile.");
        if (frame.SchemaVersion != 1 || frame.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Title profile/source mismatch.");
        return frame;
    }
}

/// <summary>The title's native-size layers (panels, Yuya, logo and effects, copyright, prompt), each composed once from its group's draws
/// into its own cleared transparent layer. Port of the MonoGame host's title visuals.</summary>
public sealed class TitleLayers(TitleFrame frame)
{
    public IReadOnlyList<string> Groups => frame.Groups;

    public IEnumerable<string> TextureKeys => frame.Draws.Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    /// <summary>Flow commands of one layer group, in measured order.</summary>
    public IReadOnlyList<FlowCommand> Commands(string group)
    {
        var commands = new List<FlowCommand>();
        foreach (var draw in frame.Draws.Where(d => d.Group == group))
        {
            if (!draw.ClearStencil && (draw.Points.Length < 3 || draw.Primitive is not (2 or 4 or 5)))
                throw new InvalidDataException("Unsupported measured title primitive.");
            var flow = new FlowDraw(draw.AssetKey, draw.Primitive, draw.Points, draw.Uv, draw.Color, null, null, draw.Additive, draw.LinearSampling,
                draw.ClearStencil, draw.WriteMask, draw.Stencil);
            if (FlowDrawCompiler.Compile(flow) is { } command) commands.Add(command);
        }
        return commands;
    }
}
