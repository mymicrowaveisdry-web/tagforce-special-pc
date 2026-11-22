using System.Numerics;
using System.Text.Json;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>Phase 4 GPU pilot: composed Duel Field draw lists (measured pieces with placements, exported by the MonoGame host for the field
/// reference states) compiled by the shared PSP draw compiler and rasterized on the RenderingDevice. Field composition itself (the duel
/// scene) is ported in Phase 8; this pilot checks the renderer on identical input.</summary>
internal sealed class FieldPilot : IDisposable
{
    public const string ListSuffix = ".psp.json";
    private readonly DuelFieldProfile profile = DuelFieldProfile.LoadEmbedded();
    private readonly FlowTextures textures;
    public GeLayer Layer { get; }

    public FieldPilot(FlowTextures textures, ScreenAssets assets)
    {
        this.textures = textures;
        textures.Faces ??= new CardFaceSource(assets.Bytes(CardAlbumResources.FacesKey));
        // Every texture the profile names must resolve before the field is shown; a partial original field is never drawn.
        foreach (string key in profile.Textures.Keys) textures.AddImported(key);
        Layer = new GeLayer(textures.Pixels);
    }

    /// <summary>Renders an exported list: the field layer's clear (black, far depth), then each measured draw in order.</summary>
    public void Show(string listFile)
    {
        var commands = new List<GeCommand> { new GeClear(true, true, true, 0, 0, 0, 255, 1) };
        using var document = JsonDocument.Parse(File.ReadAllBytes(listFile));
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var template = profile.Piece(entry.GetProperty("piece").GetString()!)[entry.GetProperty("index").GetInt32()];
            PspDraw draw = template;
            PspGePlacement? placement = null;
            if (entry.GetProperty("kind").GetString() == "sprites")
            {
                float[] rgba = template.Rgba?[^1] ?? [1, 1, 1, 1];
                var vertices = Floats2(entry.GetProperty("vertices"));
                draw = template with { Vertices = vertices, Uv = Floats2(entry.GetProperty("uv")), Rgba = vertices.Select(_ => rgba).ToArray() };
            }
            else placement = Placement(entry);
            Register(placement?.Texture ?? draw.Texture);
            if (PspGeCompiler.Compile(draw, profile, placement) is { } command) commands.Add(command);
        }
        Layer.Render(commands);
    }

    /// <summary>Face icons seen in a capture are pinned; others are decoded with the same decoder and stay labelled unverified.</summary>
    private void Register(string? key)
    {
        if (key is null || key.StartsWith(GeTextureKeys.EbootPrefix, StringComparison.Ordinal) || GeTextureKeys.TryFace(key, out _)) return;
        bool pinned = profile.Textures.ContainsKey(key) || DuelFieldReferenceResources.Cases.Any(c => c.AssetKey == key);
        textures.AddImported(key, allowUnverified: key.StartsWith("duel.face.", StringComparison.Ordinal) && !pinned);
    }

    private static PspGePlacement Placement(JsonElement e)
    {
        float[]? Array(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null;
        float[]? w = Array("world"), offset = Array("offset"), uv = Array("uvOffset"), color = Array("color");
        return new(
            w is null ? null : new Matrix4x4(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], w[9], w[10], w[11], w[12], w[13], w[14], w[15]),
            offset is null ? default : new Vector2(offset[0], offset[1]),
            e.TryGetProperty("texture", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            uv is null ? default : new Vector2(uv[0], uv[1]),
            e.TryGetProperty("alpha", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetSingle() : null,
            color is null ? null : new Vector4(color[0], color[1], color[2], color[3]),
            Array("vertexAlpha"));
    }

    private static float[][] Floats2(JsonElement e) => e.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();

    public void Dispose() => Layer.Dispose();
}
