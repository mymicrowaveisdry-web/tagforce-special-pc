using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Separate native-size layers composed from validated source textures and measured original geometry.
/// This freezes one reference frame; it does not infer EHF animation or intro timings.</summary>
public sealed class OriginalTitleVisuals : IDisposable
{
    private sealed record Draw(string Group, int Ordinal, string? AssetKey, int Primitive,
        float[][] Points, float[][] Uv, int[] Color, bool ClearStencil, bool WriteMask, bool Stencil, bool Additive, bool LinearSampling);
    private sealed record Frame(int SchemaVersion, string BuildId, string FrameDumpSha256, string Scope, string[] Groups, Draw[] Draws);
    private readonly Dictionary<string, RenderTarget2D> layers = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, RenderTarget2D> Layers => layers;
    public void ExportReviewLayers(string directory)
    {
        string root = Path.GetFullPath(directory);
        for (string? at = root; at is not null; at = Path.GetDirectoryName(at))
            if (Directory.Exists(Path.Combine(at, ".git")) || File.Exists(Path.Combine(at, ".git")))
                throw new InvalidDataException("Derived original title layers must stay outside Git worktrees.");
        Setup.PrivateStorage.Check(root);
        Directory.CreateDirectory(root);
        foreach (var (name, layer) in layers)
        {
            // GPU composites are premultiplied; editable PNGs use straight alpha. Source RGBA blobs stay unchanged.
            var pixels = new Color[480 * 272]; layer.GetData(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.A == 0) { pixels[i] = Color.Transparent; continue; }
                pixels[i] = new Color(Math.Min(255, (p.R * 255 + p.A / 2) / p.A),
                    Math.Min(255, (p.G * 255 + p.A / 2) / p.A), Math.Min(255, (p.B * 255 + p.A / 2) / p.A), p.A);
            }
            using var texture = new Texture2D(layer.GraphicsDevice, 480, 272);
            texture.SetData(pixels);
            using var output = new FileStream(Path.Combine(root, name + ".png"), FileMode.CreateNew, FileAccess.Write);
            texture.SaveAsPng(output, 480, 272);
        }
    }
    public OriginalTitleVisuals(GraphicsDevice device, OriginalScreenAssetResolver assets)
    {
        using var stream = typeof(OriginalTitleVisuals).Assembly.GetManifestResourceStream(
            "TagForceSpecial.PC.App.Screens.Title.title-frame.json") ?? throw new InvalidDataException("Missing measured title profile.");
        var frame = JsonSerializer.Deserialize<Frame>(stream) ?? throw new InvalidDataException("Invalid title profile.");
        if (frame.SchemaVersion != 1 || frame.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Title profile/source mismatch.");
        // Resolve every required original texture before allocating derived layers.
        var textures = frame.Draws.Where(d => d.AssetKey is not null).Select(d => d.AssetKey!).Distinct(StringComparer.Ordinal)
            .ToDictionary(key => key, key => assets.Resolve(key).Texture, StringComparer.Ordinal);
        var previousTargets = device.GetRenderTargets();
        var previousViewport = device.Viewport;
        using var effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            Projection = Matrix.CreateOrthographicOffCenter(0, 480, 272, 0, 0, 1)
        };
        using var maskBlend = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
        using var alpha = new BlendState
        {
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha
        };
        using var additive = new BlendState
        {
            // Native 0xA2 uses FIXB=0xFFFFFF: source alpha plus the complete destination color.
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
        };
        using var writeStencil = new DepthStencilState
        {
            DepthBufferEnable = false, StencilEnable = true, ReferenceStencil = 255,
            StencilFunction = CompareFunction.Always, StencilPass = StencilOperation.Replace
        };
        using var testStencil = new DepthStencilState
        {
            DepthBufferEnable = false, StencilEnable = true, ReferenceStencil = 255,
            StencilFunction = CompareFunction.Equal, StencilPass = StencilOperation.Keep
        };
        try
        {
            foreach (string group in frame.Groups)
            {
                var target = new RenderTarget2D(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
                layers.Add(group, target);
                device.SetRenderTarget(target);
                device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
                device.RasterizerState = RasterizerState.CullNone;
                foreach (var draw in frame.Draws.Where(d => d.Group == group))
                {
                    if (draw.ClearStencil) { device.Clear(ClearOptions.Stencil, Color.Transparent, 1, 0); continue; }
                    if (draw.Points.Length < 3 || draw.Primitive is not (2 or 4 or 5))
                        throw new InvalidDataException("Unsupported measured title primitive.");
                    device.BlendState = draw.WriteMask ? maskBlend : draw.Additive ? additive : alpha;
                    device.DepthStencilState = draw.WriteMask ? writeStencil : draw.Stencil ? testStencil : DepthStencilState.None;
                    // Native GE filtering applies inside source composition; the final 480x272 viewport remains point sampled.
                    device.SamplerStates[0] = draw.LinearSampling ? SamplerState.LinearClamp : SamplerState.PointClamp;
                    effect.TextureEnabled = draw.AssetKey is not null;
                    effect.Texture = draw.AssetKey is { } key ? textures[key] : null;
                    var color = new Color(draw.Color[0], draw.Color[1], draw.Color[2], draw.Color[3]);
                    var input = draw.Points.Select((p, i) => new VertexPositionColorTexture(new Vector3(p[0], p[1], 0), color,
                        draw.Uv.Length == 0 ? Vector2.Zero : new Vector2(draw.Uv[i][0], draw.Uv[i][1]))).ToArray();
                    var triangles = new List<VertexPositionColorTexture>();
                    for (int i = 2; i < input.Length; i++)
                    {
                        triangles.Add(draw.Primitive == 5 ? input[0] : input[i - 2]);
                        triangles.Add(input[i - 1]); triangles.Add(input[i]);
                    }
                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        if (draw.Primitive == 2) device.DrawUserPrimitives(PrimitiveType.LineStrip, input, 0, input.Length - 1);
                        else device.DrawUserPrimitives(PrimitiveType.TriangleList, triangles.ToArray(), 0, triangles.Count / 3);
                    }
                }
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            device.SetRenderTargets(previousTargets); device.Viewport = previousViewport;
            device.BlendState = BlendState.AlphaBlend; device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
        }
    }
    public void Dispose()
    {
        foreach (var layer in layers.Values) layer.Dispose();
        layers.Clear();
    }
}
