using System.Numerics;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Where and with what a measured draw is repeated (host-neutral form of the MonoGame host's placement): a replacement world matrix,
/// a screen offset in pixels, a replacement texture and a UV offset; <paramref name="VertexAlpha"/> replaces each vertex's alpha in GE order.</summary>
public sealed record PspGePlacement(Matrix4x4? World = null, Vector2 Offset = default, string? Texture = null, Vector2 UvOffset = default,
    float? Alpha = null, Vector4? Color = null, float[]? VertexAlpha = null);

/// <summary>Measured GE draws as host-neutral GE commands: transformed draws go through the captured world, view and projection matrices and
/// viewport; through-mode draws use their screen coordinates. Port of the MonoGame host's PSP draw renderer.</summary>
public static class PspGeCompiler
{
    private const float DepthRange = 65535f;

    public static GeCommand? Compile(PspDraw draw, DuelFieldProfile profile, PspGePlacement? placement = null)
    {
        placement ??= new();
        if (draw.Clear is { } clear)
        {
            if (!clear.Color && !clear.Depth && !clear.Stencil) return null;
            float[] c = draw.ClearRgba ?? [0, 0, 0, 0];
            // The layer stays opaque like the PSP framebuffer; GE alpha is stencil there, not coverage.
            return new GeClear(clear.Color, clear.Depth, clear.Stencil, GeGeometry.Byte(c[0]), GeGeometry.Byte(c[1]), GeGeometry.Byte(c[2]), 255, Depth01(draw.ClearDepth));
        }
        if (draw.Vertices is not { Length: > 0 }) return null;
        string? key = placement.Texture ?? draw.Texture;
        bool rgbOnly = key is not null && (!draw.TextureAlpha || draw.TextureFunction is "decal") && !GeTextureKeys.AlwaysOwnAlpha(key);
        var transform = draw.Through ? Through(placement.Offset) : Transform(draw, profile, placement);
        var triangles = Triangles(draw, placement, transform);
        if (triangles.Length == 0) return null;
        bool clampU = draw.Clamp?[0] ?? true, clampV = draw.Clamp?[1] ?? true;
        var state = new GeState(Blend(draw), draw.DepthTest, Compare(draw.DepthFunction), draw.DepthTest && draw.DepthWrite, draw.Linear, clampU, clampV, !rgbOnly);
        return new GeDraw(key is null ? null : new FlowTexture(key), state, triangles);
    }

    /// <summary>GE depth grows toward the viewer and passes GEQUAL; layer depth is 1 − z / 65535 with the comparison reversed.</summary>
    public static float Depth01(float z) => Math.Clamp((DepthRange - z) / DepthRange, 0f, 1f);

    public static Matrix4x4 Matrix43(float[] m) => new(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);

    private static Matrix4x4 Through(Vector2 offset) => new(
        1f / 240, 0, 0, 0,
        0, -1f / 136, 0, 0,
        0, 0, 1, 0,
        -1 + offset.X / 240, 1 - offset.Y / 136, 0, 1);

    /// <summary>World × view × projection × (GE viewport and offset → clip space), all before the perspective divide.</summary>
    private static Matrix4x4 Transform(PspDraw draw, DuelFieldProfile profile, PspGePlacement placement)
    {
        var world = placement.World ?? Matrix43(profile.Matrices[draw.World]);
        var view = Matrix43(profile.Matrices[draw.View]);
        float[] p = profile.Matrices[draw.Projection];
        var projection = new Matrix4x4(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11], p[12], p[13], p[14], p[15]);
        float[] v = profile.Viewports[draw.Viewport];
        float xs = v[0], ys = v[1], zs = v[2], xc = v[3], yc = v[4], zc = v[5], ox = v[6], oy = v[7];
        var screen = new Matrix4x4(
            xs / 240, 0, 0, 0,
            0, -ys / 136, 0, 0,
            0, 0, -zs / DepthRange, 0,
            (xc - ox + placement.Offset.X) / 240 - 1, 1 - (yc - oy + placement.Offset.Y) / 136, (DepthRange - zc) / DepthRange, 1);
        return world * view * projection * screen;
    }

    private readonly record struct Source(Vector3 Position, byte R, byte G, byte B, byte A, Vector2 Uv);

    private static GeVertex[] Triangles(PspDraw draw, PspGePlacement placement, Matrix4x4 transform)
    {
        bool doubleAlpha = draw.Blend?.Src == "doubleSrcAlpha";
        var input = new Source[draw.Vertices!.Length];
        for (int i = 0; i < input.Length; i++)
        {
            float[] p = draw.Vertices[i];
            float[] c = placement.Color is { } tint ? [tint.X, tint.Y, tint.Z, tint.W] : draw.Rgba?[i] ?? [1, 1, 1, 1];
            if (placement.VertexAlpha is { } alphas) c = [c[0], c[1], c[2], alphas[i]];
            float alpha = Math.Clamp(c[3] * (placement.Alpha ?? 1f) * (doubleAlpha ? 2 : 1), 0, 1);
            var uv = draw.Uv is { } uvs ? new Vector2(uvs[i][0], uvs[i][1]) + placement.UvOffset : Vector2.Zero;
            var position = draw.Through ? new Vector3(p[0], p[1], Depth01(p[2])) : new Vector3(p[0], p[1], p[2]);
            input[i] = new(position, GeGeometry.Byte(c[0]), GeGeometry.Byte(c[1]), GeGeometry.Byte(c[2]), GeGeometry.Byte(alpha), uv);
        }
        var list = draw.Prim switch { 3 => input, 4 => Strip(input), 5 => Fan(input), 6 => Sprites(input), _ => [] };
        return Array.ConvertAll(list, s =>
        {
            var clip = GeGeometry.Transform(s.Position, transform);
            return new GeVertex(clip.X, clip.Y, clip.Z, clip.W, s.R, s.G, s.B, s.A, s.Uv.X, s.Uv.Y);
        });
    }

    private static Source[] Strip(Source[] v)
    {
        var output = new List<Source>();
        for (int i = 2; i < v.Length; i++) { output.Add(v[i - 2]); output.Add(v[i - 1]); output.Add(v[i]); }
        return [.. output];
    }

    private static Source[] Fan(Source[] v)
    {
        var output = new List<Source>();
        for (int i = 2; i < v.Length; i++) { output.Add(v[0]); output.Add(v[i - 1]); output.Add(v[i]); }
        return [.. output];
    }

    /// <summary>GE sprites: each vertex pair is a rectangle's opposite corners; the second vertex's colour applies to the whole rectangle.</summary>
    private static Source[] Sprites(Source[] v)
    {
        var output = new List<Source>();
        for (int i = 0; i + 1 < v.Length; i += 2)
        {
            var a = v[i]; var b = v[i + 1];
            // GE rotated sprite: when the corners are mirrored on exactly one axis, u follows y and v follows x.
            bool rotated = a.Position.X > b.Position.X != a.Position.Y > b.Position.Y;
            Vector2 Uv(float x, float y)
            {
                float tx = (x - a.Position.X) / (b.Position.X - a.Position.X), ty = (y - a.Position.Y) / (b.Position.Y - a.Position.Y);
                return rotated ? new(Lerp(a.Uv.X, b.Uv.X, ty), Lerp(a.Uv.Y, b.Uv.Y, tx)) : new(Lerp(a.Uv.X, b.Uv.X, tx), Lerp(a.Uv.Y, b.Uv.Y, ty));
            }
            Source At(float x, float y, float z) => new(new(x, y, z), b.R, b.G, b.B, b.A, Uv(x, y));
            var tl = At(a.Position.X, a.Position.Y, a.Position.Z);
            var tr = At(b.Position.X, a.Position.Y, b.Position.Z);
            var bl = At(a.Position.X, b.Position.Y, b.Position.Z);
            var br = At(b.Position.X, b.Position.Y, b.Position.Z);
            output.AddRange([tl, tr, bl, bl, tr, br]);
        }
        return [.. output];
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>GE blending writes colour only (alpha keeps the layer's), with the draw's fixed colour as the constant.</summary>
    private static GeBlend Blend(PspDraw draw)
    {
        var b = draw.Blend ?? new PspBlend("one", "zero", 0, 0);
        int fix = b.Src == "fix" ? b.FixA : b.FixB;
        return new GeBlend(Factor(b.Src, b.FixA, true), Factor(b.Dst, b.FixB, false), GeFactor.One, GeFactor.Zero,
            (byte)(fix & 255), (byte)(fix >> 8 & 255), (byte)(fix >> 16 & 255), WriteAlpha: false);
    }

    private static GeFactor Factor(string factor, int fix, bool source) => factor switch
    {
        "one" => GeFactor.One,
        "zero" => GeFactor.Zero,
        "srcAlpha" or "doubleSrcAlpha" => GeFactor.SrcAlpha,
        "invSrcAlpha" => GeFactor.InvSrcAlpha,
        "dstColor" => GeFactor.DstColor,
        "invDstColor" => GeFactor.InvDstColor,
        "srcColor" => GeFactor.SrcColor,
        "invSrcColor" => GeFactor.InvSrcColor,
        "fix" when (fix & 0xffffff) == 0xffffff => GeFactor.One,
        "fix" when (fix & 0xffffff) == 0 => GeFactor.Zero,
        "fix" => GeFactor.Constant,
        _ => throw new InvalidDataException($"Unsupported GE blend factor {factor} ({(source ? "source" : "destination")}).")
    };

    /// <summary>Reversed: GE z grows toward the viewer, layer depth shrinks.</summary>
    private static GeCompare Compare(int function) => function switch
    {
        0 => GeCompare.Never, 1 => GeCompare.Always, 2 => GeCompare.Equal, 3 => GeCompare.NotEqual,
        4 => GeCompare.Greater, 5 => GeCompare.GreaterEqual, 6 => GeCompare.Less, _ => GeCompare.LessEqual
    };
}
