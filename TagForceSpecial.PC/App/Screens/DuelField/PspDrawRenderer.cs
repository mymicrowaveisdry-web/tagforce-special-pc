using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Where and with what a measured draw is repeated: a replacement world matrix, a screen offset in pixels, a replacement
/// texture and a UV offset; <paramref name="VertexAlpha"/> replaces the alpha of each vertex in GE order (the attack arrow head). The default
/// repeats the draw exactly as captured.</summary>
public readonly record struct PspPlacement(Matrix? World = null, Vector2 Offset = default, string? Texture = null, Vector2 UvOffset = default,
    float? Alpha = null, Vector4? Color = null, float[]? VertexAlpha = null);

/// <summary>Repeats measured GE draws on the GPU. Transformed draws go through the captured world, view and projection matrices and
/// viewport, so perspective, texturing and depth match the original; through-mode draws use their screen coordinates.</summary>
public sealed class PspDrawRenderer : IDisposable
{
    private const float DepthRange = 65535f;
    private readonly GraphicsDevice device;
    private readonly BasicEffect effect;
    private readonly Dictionary<(string, string, int, int), BlendState> blends = [];
    private readonly Dictionary<(bool, bool, int), DepthStencilState> depths = [];
    private readonly Dictionary<(bool, bool, bool), SamplerState> samplers = [];

    public PspDrawRenderer(GraphicsDevice device)
    {
        this.device = device;
        effect = new(device) { VertexColorEnabled = true, LightingEnabled = false };
    }

    /// <summary>Draws one measured draw. <paramref name="texture"/> maps an asset key (and whether texture alpha is used) to a texture.</summary>
    public void Draw(PspDraw draw, DuelFieldProfile profile, Func<string, bool, Texture2D> texture, PspPlacement placement = default)
    {
        if (draw.Clear is { } clear) { Clear(draw, clear); return; }
        if (draw.Vertices is not { Length: > 0 } vertices) return;
        string? key = placement.Texture ?? draw.Texture;
        bool rgbOnly = key is not null && (!draw.TextureAlpha || draw.TextureFunction is "decal");
        effect.TextureEnabled = key is not null;
        effect.Texture = key is null ? null : texture(key, !rgbOnly);
        effect.World = Matrix.Identity; effect.View = Matrix.Identity;
        effect.Projection = draw.Through ? ThroughProjection(placement.Offset) : Transform(draw, profile, placement);
        device.BlendState = Blend(draw);
        device.DepthStencilState = Depth(draw);
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = Sampler(draw);
        var triangles = Triangles(draw, vertices, placement);
        if (triangles.Length == 0) return;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, triangles, 0, triangles.Length / 3);
        }
    }

    private void Clear(PspDraw draw, PspClear clear)
    {
        var options = (clear.Color ? ClearOptions.Target : 0) | (clear.Depth ? ClearOptions.DepthBuffer : 0) | (clear.Stencil ? ClearOptions.Stencil : 0);
        if (options == 0) return;
        float[] c = draw.ClearRgba ?? [0, 0, 0, 0];
        // The layer stays opaque like the PSP framebuffer; GE alpha is stencil there, not coverage.
        device.Clear(options, new Color(c[0], c[1], c[2], 1f), Depth01(draw.ClearDepth), 0);
    }

    /// <summary>GE depth grows toward the viewer and passes GEQUAL; MonoGame depth is 1 − z / 65535 with the comparison reversed.</summary>
    private static float Depth01(float z) => Math.Clamp((DepthRange - z) / DepthRange, 0f, 1f);

    private static Matrix ThroughProjection(Vector2 offset) => new(
        1f / 240, 0, 0, 0,
        0, -1f / 136, 0, 0,
        0, 0, 1, 0,
        -1 + offset.X / 240, 1 - offset.Y / 136, 0, 1);

    /// <summary>World × view × projection × (GE viewport and offset → MonoGame clip space), all applied before the perspective divide.</summary>
    private static Matrix Transform(PspDraw draw, DuelFieldProfile profile, PspPlacement placement)
    {
        var world = placement.World ?? Matrix43(profile.Matrices[draw.World]);
        var view = Matrix43(profile.Matrices[draw.View]);
        float[] p = profile.Matrices[draw.Projection];
        var projection = new Matrix(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11], p[12], p[13], p[14], p[15]);
        float[] v = profile.Viewports[draw.Viewport];
        float xs = v[0], ys = v[1], zs = v[2], xc = v[3], yc = v[4], zc = v[5], ox = v[6], oy = v[7];
        var screen = new Matrix(
            xs / 240, 0, 0, 0,
            0, -ys / 136, 0, 0,
            0, 0, -zs / DepthRange, 0,
            (xc - ox + placement.Offset.X) / 240 - 1, 1 - (yc - oy + placement.Offset.Y) / 136, (DepthRange - zc) / DepthRange, 1);
        return world * view * projection * screen;
    }

    /// <summary>A GE 4×3 matrix (row-vector order, translation last) as an XNA matrix.</summary>
    public static Matrix Matrix43(float[] m) => new(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);

    private static VertexPositionColorTexture[] Triangles(PspDraw draw, float[][] vertices, PspPlacement placement)
    {
        bool doubleAlpha = draw.Blend?.Src == "doubleSrcAlpha";
        var input = new VertexPositionColorTexture[vertices.Length];
        for (int i = 0; i < input.Length; i++)
        {
            float[] p = vertices[i];
            float[] c = placement.Color is { } tint ? [tint.X, tint.Y, tint.Z, tint.W] : draw.Rgba?[i] ?? [1, 1, 1, 1];
            if (placement.VertexAlpha is { } alphas) c = [c[0], c[1], c[2], alphas[i]];
            float alpha = Math.Clamp(c[3] * (placement.Alpha ?? 1f) * (doubleAlpha ? 2 : 1), 0, 1);
            var uv = draw.Uv is { } uvs ? new Vector2(uvs[i][0], uvs[i][1]) + placement.UvOffset : Vector2.Zero;
            var position = draw.Through ? new Vector3(p[0], p[1], Depth01(p[2])) : new Vector3(p[0], p[1], p[2]);
            input[i] = new(position, new Color(c[0], c[1], c[2], alpha), uv);
        }
        return draw.Prim switch
        {
            3 => input,
            4 => Strip(input),
            5 => Fan(input),
            6 => Sprites(input),
            _ => []
        };
    }

    private static VertexPositionColorTexture[] Strip(VertexPositionColorTexture[] v)
    {
        var output = new List<VertexPositionColorTexture>();
        for (int i = 2; i < v.Length; i++) { output.Add(v[i - 2]); output.Add(v[i - 1]); output.Add(v[i]); }
        return [.. output];
    }

    private static VertexPositionColorTexture[] Fan(VertexPositionColorTexture[] v)
    {
        var output = new List<VertexPositionColorTexture>();
        for (int i = 2; i < v.Length; i++) { output.Add(v[0]); output.Add(v[i - 1]); output.Add(v[i]); }
        return [.. output];
    }

    /// <summary>GE sprites: each vertex pair is a rectangle's opposite corners; the second vertex's colour applies to the whole rectangle.</summary>
    private static VertexPositionColorTexture[] Sprites(VertexPositionColorTexture[] v)
    {
        var output = new List<VertexPositionColorTexture>();
        for (int i = 0; i + 1 < v.Length; i += 2)
        {
            var a = v[i]; var b = v[i + 1];
            // GE rotated sprite: when the corners are mirrored on exactly one axis, u follows y and v follows x.
            bool rotated = a.Position.X > b.Position.X != a.Position.Y > b.Position.Y;
            Vector2 Uv(float x, float y)
            {
                float tx = (x - a.Position.X) / (b.Position.X - a.Position.X), ty = (y - a.Position.Y) / (b.Position.Y - a.Position.Y);
                return rotated ? new(MathHelper.Lerp(a.TextureCoordinate.X, b.TextureCoordinate.X, ty), MathHelper.Lerp(a.TextureCoordinate.Y, b.TextureCoordinate.Y, tx))
                    : new(MathHelper.Lerp(a.TextureCoordinate.X, b.TextureCoordinate.X, tx), MathHelper.Lerp(a.TextureCoordinate.Y, b.TextureCoordinate.Y, ty));
            }
            VertexPositionColorTexture At(float x, float y, float z) => new(new(x, y, z), b.Color, Uv(x, y));
            var tl = At(a.Position.X, a.Position.Y, a.Position.Z);
            var tr = At(b.Position.X, a.Position.Y, b.Position.Z);
            var bl = At(a.Position.X, b.Position.Y, b.Position.Z);
            var br = At(b.Position.X, b.Position.Y, b.Position.Z);
            output.AddRange([tl, tr, bl, bl, tr, br]);
        }
        return [.. output];
    }

    private BlendState Blend(PspDraw draw)
    {
        var b = draw.Blend ?? new PspBlend("one", "zero", 0, 0);
        var cacheKey = (b.Src, b.Dst, b.FixA, b.FixB);
        if (blends.TryGetValue(cacheKey, out var state)) return state;
        int fix = b.Src == "fix" ? b.FixA : b.FixB;
        state = new BlendState
        {
            ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
            ColorSourceBlend = Factor(b.Src, b.FixA, source: true),
            ColorDestinationBlend = Factor(b.Dst, b.FixB, source: false),
            AlphaSourceBlend = Microsoft.Xna.Framework.Graphics.Blend.One,
            AlphaDestinationBlend = Microsoft.Xna.Framework.Graphics.Blend.Zero,
            BlendFactor = new Color(fix & 255, fix >> 8 & 255, fix >> 16 & 255, 255)
        };
        blends.Add(cacheKey, state);
        return state;
    }

    private static Blend Factor(string factor, int fix, bool source) => factor switch
    {
        "one" => Microsoft.Xna.Framework.Graphics.Blend.One,
        "zero" => Microsoft.Xna.Framework.Graphics.Blend.Zero,
        "srcAlpha" or "doubleSrcAlpha" => Microsoft.Xna.Framework.Graphics.Blend.SourceAlpha,
        "invSrcAlpha" => Microsoft.Xna.Framework.Graphics.Blend.InverseSourceAlpha,
        "dstColor" => Microsoft.Xna.Framework.Graphics.Blend.DestinationColor,
        "invDstColor" => Microsoft.Xna.Framework.Graphics.Blend.InverseDestinationColor,
        "srcColor" => Microsoft.Xna.Framework.Graphics.Blend.SourceColor,
        "invSrcColor" => Microsoft.Xna.Framework.Graphics.Blend.InverseSourceColor,
        "fix" when (fix & 0xffffff) == 0xffffff => Microsoft.Xna.Framework.Graphics.Blend.One,
        "fix" when (fix & 0xffffff) == 0 => Microsoft.Xna.Framework.Graphics.Blend.Zero,
        "fix" => Microsoft.Xna.Framework.Graphics.Blend.BlendFactor,
        _ => throw new InvalidDataException($"Unsupported GE blend factor {factor} ({(source ? "source" : "destination")}).")
    };

    private DepthStencilState Depth(PspDraw draw)
    {
        var cacheKey = (draw.DepthTest, draw.DepthWrite, draw.DepthFunction);
        if (depths.TryGetValue(cacheKey, out var state)) return state;
        state = new DepthStencilState
        {
            DepthBufferEnable = draw.DepthTest,
            DepthBufferWriteEnable = draw.DepthTest && draw.DepthWrite,
            // Reversed: GE z grows toward the viewer, MonoGame depth shrinks.
            DepthBufferFunction = draw.DepthFunction switch
            {
                0 => CompareFunction.Never, 1 => CompareFunction.Always, 2 => CompareFunction.Equal, 3 => CompareFunction.NotEqual,
                4 => CompareFunction.Greater, 5 => CompareFunction.GreaterEqual, 6 => CompareFunction.Less, _ => CompareFunction.LessEqual
            }
        };
        depths.Add(cacheKey, state);
        return state;
    }

    private SamplerState Sampler(PspDraw draw)
    {
        bool clampU = draw.Clamp?[0] ?? true, clampV = draw.Clamp?[1] ?? true;
        var cacheKey = (draw.Linear, clampU, clampV);
        if (samplers.TryGetValue(cacheKey, out var state)) return state;
        state = new SamplerState
        {
            Filter = draw.Linear ? TextureFilter.Linear : TextureFilter.Point,
            AddressU = clampU ? TextureAddressMode.Clamp : TextureAddressMode.Wrap,
            AddressV = clampV ? TextureAddressMode.Clamp : TextureAddressMode.Wrap
        };
        samplers.Add(cacheKey, state);
        return state;
    }

    public void Dispose()
    {
        effect.Dispose();
        foreach (var state in blends.Values) state.Dispose();
        foreach (var state in depths.Values) state.Dispose();
        foreach (var state in samplers.Values) state.Dispose();
    }
}
