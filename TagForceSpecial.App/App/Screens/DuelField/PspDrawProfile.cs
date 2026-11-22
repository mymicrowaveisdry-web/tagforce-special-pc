using System.Text.Json;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public sealed record PspClear(bool Color, bool Stencil, bool Depth);
public sealed record PspBlend(string Src, string Dst, int FixA, int FixB);
public sealed record PspAlphaTest(int Function, int Reference, int Mask);

/// <summary>One measured GE draw: local vertices with matrix indices (transformed) or screen vertices (through mode), normalized UVs,
/// vertex colours and the GE state it used. A clear draw carries only its clear flags, colour and depth.</summary>
public sealed record PspDraw(string Src, int Prim, bool Through, PspClear? Clear, float[]? ClearRgba, float ClearDepth,
    string? Texture, float[][]? Vertices, float[][]? Uv, float[][]? Rgba, int World, int View, int Projection, int Viewport,
    PspBlend? Blend, bool DepthTest, int DepthFunction, bool DepthWrite, PspAlphaTest? AlphaTest,
    string? TextureFunction, bool TextureAlpha, bool Linear, bool[]? Clamp);

public sealed record PspTextureInfo(int[] Size);

/// <summary>The measured Duel Field (duel-field-frame.json): numbers and asset keys only, grouped into named pieces in GE order.</summary>
public sealed record DuelFieldProfile(int SchemaVersion, string BuildId, IReadOnlyDictionary<string, PspTextureInfo> Textures,
    float[][] Matrices, float[][] Viewports, IReadOnlyDictionary<string, PspDraw[]> Pieces)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static DuelFieldProfile LoadEmbedded()
    {
        using var stream = typeof(DuelFieldProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.DuelField.duel-field-frame.json")
            ?? throw new InvalidDataException("Missing measured duel field profile.");
        var profile = JsonSerializer.Deserialize<DuelFieldProfile>(stream, Options) ?? throw new InvalidDataException("Invalid duel field profile.");
        if (profile.SchemaVersion != 1 || profile.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Duel field profile/source mismatch.");
        return profile;
    }

    public PspDraw[] Piece(string name) => Pieces.TryGetValue(name, out var draws) ? draws
        : throw new InvalidDataException("Duel field profile has no piece " + name + ".");
}
