using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record FlowQuad(float X0, float Y0, float X1, float Y1, float U0, float V0, float U1, float V1, int[] Color, bool Additive);
public sealed record FlowTextSource(string Source, int Offset, int Start, int Chars);
public sealed record FlowText(string Source, int Offset, int Start, int Chars, float[] X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record FlowName(float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record FlowDraw(string? AssetKey, int Primitive, float[][] Points, float[][] Uv, int[] Color, uint[]? VertexColors, int[]? Add,
    bool Additive, bool LinearSampling, bool ClearStencil, bool WriteMask, bool Stencil);
public sealed record FlowButton(float[] Rect, FlowText Label);
public sealed record FlowMenuRow(float Y, FlowText Label);
public sealed record FlowMenu(FlowQuad[] Plain, FlowQuad[] Focus, FlowMenuRow[] Rows);
public sealed record FlowFrame(string Id, string DumpSha256, FlowDraw[] Draws, FlowText[] Text, FlowButton[]? Buttons, FlowMenu? Menu, FlowName? Name);
public sealed record FlowEbootTexture(string Key, int Offset, int Length, int ClutOffset, int ClutBytes, int Width, int Height, int Bpp, int Stride, string RgbaSha256);
public sealed record FlowButtonStyle(FlowQuad[] Focus, FlowQuad Plain);
public sealed record FlowProfile(int SchemaVersion, string BuildId, string Scope, FlowButtonStyle Button, FlowTextSource[] LevelNames,
    FlowEbootTexture[] EbootTextures, FlowFrame[] Frames);

/// <summary>Reads prompt text from pinned source bytes. Original strings stay in the private cache, never in Git.</summary>
public static partial class TitleFlowText
{
    [GeneratedRegex(@"\$C\d", RegexOptions.CultureInvariant)] private static partial Regex ColorCode();
    /// <summary>Same transform the exporter measured: colour codes and line breaks removed; %c is the trademark sign.</summary>
    public static string Normalize(string message) => ColorCode().Replace(message, "").Replace("\n", "").Replace("%c", "™");
    public static string Read(byte[] source, int offset, int start, int chars)
    {
        if (offset < 0 || offset >= source.Length || start < 0 || chars <= 0) throw new InvalidDataException("Invalid text reference.");
        int end = offset;
        while (end + 1 < source.Length && (source[end] != 0 || source[end + 1] != 0)) end += 2;
        string message = Normalize(Encoding.Unicode.GetString(source, offset, end - offset));
        if (start + chars > message.Length) throw new InvalidDataException("Text reference exceeds source message.");
        return message.Substring(start, chars);
    }
}

/// <summary>Draws measured frames from validated source textures plus a PGF glyph atlas into one retained layer.
/// Frames are frozen references: no animation timing is inferred.</summary>
public sealed partial class TitleFlowVisuals : IDisposable
{
    public const string RequiredFontSha256 = "250babd8a242d7eaaa84284c78d5e2273b29e61d0d8c01b65414e3248d7c1164";
    private const int CellSize = 30, CellPitch = 32, BaselineRow = 24, GlyphLeft = 7;
    private const char Bullet = '\u25CF';
    private readonly GraphicsDevice device;
    private readonly FlowProfile profile;
    private readonly Dictionary<string, FlowFrame> frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    private readonly List<Texture2D> owned = [];
    private readonly Dictionary<string, Texture2D> baked = new(StringComparer.Ordinal);
    private readonly byte[] eboot, titleMenu;
    private readonly PgfFont? font;
    private readonly Texture2D? atlas;
    private readonly Dictionary<char, (int X, int Y)> cells = [];
    private readonly Color[] atlasPixels = new Color[512 * 512];
    private bool atlasDirty;
    private readonly BasicEffect effect;
    private readonly BlendState alpha, additive, maskBlend;
    private readonly DepthStencilState writeStencil, testStencil;
    public RenderTarget2D Layer { get; }
    public IEnumerable<string> FrameIds => frames.Keys;
    public bool OriginalFontAvailable => font is not null;
    /// <summary>Authored-font text used only when no PSP font was imported; never presented as original typography.</summary>
    public List<(string Text, int X, int Y, Color Color)> FallbackLines { get; } = [];

    public static FlowProfile LoadProfile()
    {
        using var stream = typeof(TitleFlowVisuals).Assembly.GetManifestResourceStream(
            "TagForceSpecial.PC.App.Screens.Title.title-flow-frames.json") ?? throw new InvalidDataException("Missing measured title-flow profile.");
        var value = JsonSerializer.Deserialize<FlowProfile>(stream) ?? throw new InvalidDataException("Invalid title-flow profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Title-flow profile/source mismatch.");
        return value;
    }

    public TitleFlowVisuals(GraphicsDevice device, OriginalScreenAssetResolver assets, byte[]? fontFile)
    {
        this.device = device;
        profile = LoadProfile();
        foreach (var frame in profile.Frames) frames.Add(frame.Id, frame);
        eboot = assets.ResolveBytes(TitleFlowReferenceResources.EbootKey);
        titleMenu = assets.ResolveBytes(TitleFlowReferenceResources.TitleMenuKey);
        foreach (var embedded in profile.EbootTextures) textures.Add(embedded.Key, DecodeEboot(embedded));
        foreach (var key in profile.Frames.SelectMany(f => f.Draws).Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal))
            if (!textures.ContainsKey(key)) textures.Add(key, assets.Resolve(key).Texture);
        // Menu bars come from the panel atlas referenced by the Main Menu/start menu profile.
        textures.TryAdd("title.flow.m-menu-panel01", assets.Resolve("title.flow.m-menu-panel01").Texture);
        if (fontFile is not null)
        {
            var parsed = new PgfFont(fontFile);
            if (parsed.Sha256 != RequiredFontSha256) throw new InvalidDataException("PSP font file differs from the pinned system font.");
            font = parsed;
            atlas = new(device, 512, 512);
            owned.Add(atlas);
        }
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        InitializeMainMenu();
        InitializeOptions(assets);
        effect = new(device) { VertexColorEnabled = true, Projection = Matrix.CreateOrthographicOffCenter(0, 480, 272, 0, 0, 1) };
        alpha = new()
        {
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha
        };
        additive = new()
        {
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
        };
        maskBlend = new() { ColorWriteChannels = ColorWriteChannels.None };
        writeStencil = new()
        {
            DepthBufferEnable = false, StencilEnable = true, ReferenceStencil = 255,
            StencilFunction = CompareFunction.Always, StencilPass = StencilOperation.Replace
        };
        testStencil = new()
        {
            DepthBufferEnable = false, StencilEnable = true, ReferenceStencil = 255,
            StencilFunction = CompareFunction.Equal, StencilPass = StencilOperation.Keep
        };
    }

    public bool HasFrame(string id) => frames.ContainsKey(id);

    /// <summary>Validates every text reference against source bytes; a wrong offset fails before the screen is shown.</summary>
    public void ValidateText()
    {
        foreach (var frame in profile.Frames)
        {
            foreach (var text in frame.Text.Concat(frame.Buttons?.Select(b => b.Label) ?? []).Concat(frame.Menu?.Rows.Select(r => r.Label) ?? []))
                _ = Source(text.Source, text.Offset, text.Start, text.Chars);
        }
        foreach (var level in profile.LevelNames) _ = Source(level.Source, level.Offset, level.Start, level.Chars);
    }

    private string Source(string source, int offset, int start, int chars) => TitleFlowText.Read(source switch
    {
        "eboot" => eboot, "title-menu" => titleMenu, _ => throw new InvalidDataException("Unknown text source.")
    }, offset, start, chars);

    public void Render(string frameId, int buttonFocus = 0, int menuFocus = 0, string? name = null, DuelistLevelChoice level = DuelistLevelChoice.FirstTimer, int? menuBackgroundFrame = null)
    {
        var frame = frames[frameId];
        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        FallbackLines.Clear();
        try
        {
            device.SetRenderTarget(Layer);
            device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
            device.RasterizerState = RasterizerState.CullNone;
            bool overMenu = frameId == EndGameFrame;
            if (overMenu) DrawEndGameBehindDialog(frame, menuBackgroundFrame);
            else foreach (var draw in frame.Draws) DrawMeasured(draw);
            if (frame.Menu is { } menu) DrawMenu(menu, menuFocus);
            if (frame.Buttons is { } buttons) DrawButtons(buttons, buttonFocus);
            foreach (var text in frame.Text)
            {
                if (overMenu && IsMenuLabel(text)) continue;
                string value = Source(text.Source, text.Offset, text.Start, text.Chars);
                // The level sentence is measured once; the observed variants differ only in the bracketed name.
                if (frameId == "entry.level" && value.Contains('<'))
                {
                    var chosen = profile.LevelNames[(int)level];
                    string levelName = Source(chosen.Source, chosen.Offset, chosen.Start, chosen.Chars);
                    int open = value.IndexOf('<');
                    DrawAdvanced(value[..(open + 1)] + levelName + ">", text);
                    continue;
                }
                DrawMeasuredText(value, text);
            }
            if (frame.Name is { } run && name is not null)
                DrawLine(name, run.X, run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);
        }
        finally
        {
            device.SetRenderTargets(targets); device.Viewport = viewport;
            device.BlendState = BlendState.AlphaBlend; device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
        }
    }

    private void DrawMeasured(FlowDraw draw)
    {
        if (draw.ClearStencil) { device.Clear(ClearOptions.Stencil, Color.Transparent, 1, 0); return; }
        if (draw.Points.Length < 3 || draw.Primitive is not (3 or 4 or 5)) return;
        device.BlendState = draw.WriteMask ? maskBlend : draw.Additive ? additive : alpha;
        device.DepthStencilState = draw.WriteMask ? writeStencil : draw.Stencil ? testStencil : DepthStencilState.None;
        device.SamplerStates[0] = draw.LinearSampling ? SamplerState.LinearClamp : SamplerState.PointClamp;
        effect.TextureEnabled = draw.AssetKey is not null;
        effect.Texture = draw.AssetKey is { } key ? (draw.Add is { } add ? Specular(key, draw.Color, add) : textures[key]) : null;
        // A baked specular texture already holds tex x colour + specular; only alpha still modulates.
        var material = draw.Add is not null && draw.AssetKey is not null
            ? new Color(255, 255, 255, draw.Color[3]) : new Color(draw.Color[0], draw.Color[1], draw.Color[2], draw.Color[3]);
        var input = new VertexPositionColorTexture[draw.Points.Length];
        for (int i = 0; i < input.Length; i++)
        {
            Color color = material;
            if (draw.VertexColors is { } colors) { uint c = colors[i]; color = new Color((int)(c & 255), (int)(c >> 8 & 255), (int)(c >> 16 & 255), (int)(c >> 24 & 255)); }
            input[i] = new(new Vector3(draw.Points[i][0], draw.Points[i][1], 0), color,
                draw.Uv.Length == 0 ? Vector2.Zero : new Vector2(draw.Uv[i][0], draw.Uv[i][1]));
        }
        var triangles = draw.Primitive == 3 ? input : Expand(input, draw.Primitive);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, triangles, 0, triangles.Length / 3);
        }
    }

    /// <summary>GE separate specular: colour = texture x primary + specular, clamped, with texture alpha unchanged.
    /// BasicEffect has no post-texture add, so the same result is baked per texture and colour.</summary>
    private Texture2D Specular(string key, int[] primary, int[] add)
    {
        string id = $"{key}|{primary[0]},{primary[1]},{primary[2]}|{add[0]},{add[1]},{add[2]}";
        if (baked.TryGetValue(id, out var existing)) return existing;
        var source = textures[key];
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = new Color(Math.Min(255, p.R * primary[0] / 255 + add[0]), Math.Min(255, p.G * primary[1] / 255 + add[1]),
                Math.Min(255, p.B * primary[2] / 255 + add[2]), (int)p.A);
        }
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(pixels);
        owned.Add(texture); baked.Add(id, texture);
        return texture;
    }

    private static VertexPositionColorTexture[] Expand(VertexPositionColorTexture[] input, int primitive)
    {
        var output = new List<VertexPositionColorTexture>();
        for (int i = 2; i < input.Length; i++)
        {
            output.Add(primitive == 5 ? input[0] : input[i - 2]);
            output.Add(input[i - 1]); output.Add(input[i]);
        }
        return output.ToArray();
    }

    private void DrawQuad(Texture2D texture, FlowQuad q, float dx, float dy, bool linear, Color? tint = null)
    {
        device.BlendState = q.Additive ? additive : alpha;
        device.DepthStencilState = DepthStencilState.None;
        device.SamplerStates[0] = linear ? SamplerState.LinearClamp : SamplerState.PointClamp;
        effect.TextureEnabled = true; effect.Texture = texture;
        var color = tint ?? new Color(q.Color[0], q.Color[1], q.Color[2], q.Color[3]);
        float x0 = q.X0 + dx, x1 = q.X1 + dx, y0 = q.Y0 + dy, y1 = q.Y1 + dy;
        var v = new[]
        {
            new VertexPositionColorTexture(new(x0, y0, 0), color, new(q.U0, q.V0)),
            new VertexPositionColorTexture(new(x1, y0, 0), color, new(q.U1, q.V0)),
            new VertexPositionColorTexture(new(x0, y1, 0), color, new(q.U0, q.V1)),
            new VertexPositionColorTexture(new(x0, y1, 0), color, new(q.U0, q.V1)),
            new VertexPositionColorTexture(new(x1, y0, 0), color, new(q.U1, q.V0)),
            new VertexPositionColorTexture(new(x1, y1, 0), color, new(q.U1, q.V1))
        };
        foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, v, 0, 2); }
    }

    private void DrawMenu(FlowMenu menu, int focus)
    {
        var panel = textures["title.flow.m-menu-panel01"];
        for (int i = 0; i < menu.Rows.Length; i++)
        {
            var row = menu.Rows[i];
            foreach (var q in menu.Plain) DrawQuad(panel, q, 0, row.Y, false);
            bool focused = i == focus;
            if (focused) foreach (var q in menu.Focus) DrawQuad(panel, q, 0, row.Y, false);
            string label = Source(row.Label.Source, row.Label.Offset, row.Label.Start, row.Label.Chars);
            // Measured: focused rows use black text with a light shadow; others white with a heavier one.
            DrawLine(label, row.Label.X[0], row.Label.Y, row.Label.ScaleX, row.Label.ScaleY, focused ? 0 : 0xffffff, focused ? 0x40 : 0x80, row.Label.X);
        }
    }

    private void DrawButtons(FlowButton[] buttons, int focus)
    {
        var atlasTexture = textures["title.eboot.dialog-button"];
        for (int i = 0; i < buttons.Length; i++)
        {
            float x = buttons[i].Rect[0], y = buttons[i].Rect[1];
            if (i == focus) foreach (var q in profile.Button.Focus) DrawQuad(atlasTexture, q, x, y, false);
            else DrawQuad(atlasTexture, profile.Button.Plain, x, y, false);
            var label = buttons[i].Label;
            DrawMeasuredText(Source(label.Source, label.Offset, label.Start, label.Chars), label);
        }
    }

    private void DrawMeasuredText(string value, FlowText run) => DrawLine(value, run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha,
        run.X.Length == value.Length ? run.X : null);
    private void DrawAdvanced(string value, FlowText run) => DrawLine(value, run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);

    private void DrawLine(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, float[]? measuredX = null)
    {
        if (font is null || atlas is null)
        {
            FallbackLines.Add((value.ToUpperInvariant(), (int)x, (int)y + 3, new Color(color & 255, (color >> 8) & 255, (color >> 16) & 255)));
            return;
        }
        foreach (char c in value) EnsureGlyph(c);
        if (atlasDirty) { atlas.SetData(atlasPixels); atlasDirty = false; }
        var main = new Color((color) & 255, (color >> 8) & 255, (color >> 16) & 255, 255);
        // Measured shadow: two copies at (+0,+1) and (+1,+1) beneath the opaque text.
        if (shadowAlpha > 0)
        {
            var shadow = new Color(0, 0, 0, shadowAlpha);
            Glyphs(value, x, y + 1, scaleX, scaleY, shadow, measuredX, 0);
            Glyphs(value, x + 1, y + 1, scaleX, scaleY, shadow, measuredX, 1);
        }
        Glyphs(value, x, y, scaleX, scaleY, main, measuredX, 0);
    }

    private void Glyphs(string value, float x, float y, float scaleX, float scaleY, Color color, float[]? measuredX, float measuredOffset)
    {
        float pen = x;
        float cellW = CellSize * scaleX, cellH = CellSize * scaleY;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            float at = measuredX is not null ? measuredX[i] + measuredOffset : pen;
            var (cx, cy) = cells[c];
            var quad = new FlowQuad(at, y, at + cellW, y + cellH, cx / 512f, cy / 512f, (cx + CellSize) / 512f, (cy + CellSize) / 512f, [255, 255, 255, 255], false);
            DrawQuad(atlas!, quad, 0, 0, true, color);
            pen += font!.Glyph(c)?.AdvanceH26_6 / 64f * scaleX ?? cellW / 2;
        }
    }

    private void EnsureGlyph(char c)
    {
        if (cells.ContainsKey(c)) return;
        int index = cells.Count;
        int cx = 1 + index % 16 * CellPitch, cy = 1 + index / 16 * CellPitch;
        if (cy + CellSize > 512) throw new InvalidOperationException("Glyph atlas budget exhausted.");
        cells[c] = (cx, cy);
        var glyph = font!.Glyph(c);
        if (glyph is null && c == Bullet) { DrawBullet(cx, cy); atlasDirty = true; return; }
        if (glyph is null || glyph.Width == 0) return;
        for (int y = 0; y < glyph.Height; y++)
            for (int x = 0; x < glyph.Width; x++)
            {
                int ax = cx + GlyphLeft + glyph.Left + x, ay = cy + BaselineRow - glyph.Top + y;
                if (ax < cx || ay < cy || ax >= cx + CellSize || ay >= cy + CellSize) continue;
                int coverage = glyph.Coverage4[y * glyph.Width + x] * 17;
                atlasPixels[ay * 512 + ax] = new Color(255, 255, 255, coverage);
            }
        atlasDirty = true;
    }

    /// <summary>The Latin font has no bullet. This disc is authored and recorded as a known mismatch, not original typography.</summary>
    private void DrawBullet(int cx, int cy)
    {
        const float centerX = GlyphLeft + 7.5f, centerY = BaselineRow - 7.5f, radius = 6.5f;
        for (int y = 0; y < CellSize; y++)
            for (int x = 0; x < CellSize; x++)
            {
                float distance = MathF.Sqrt((x + .5f - centerX) * (x + .5f - centerX) + (y + .5f - centerY) * (y + .5f - centerY));
                int coverage = (int)(Math.Clamp(radius + .5f - distance, 0, 1) * 255);
                atlasPixels[(cy + y) * 512 + cx + x] = new Color(255, 255, 255, coverage);
            }
    }

    private Texture2D DecodeEboot(FlowEbootTexture source)
    {
        if (source.Width is <= 0 or > 512 || source.Height is <= 0 or > 512 || source.Bpp is not (4 or 8) || source.Offset < 0 ||
            source.ClutOffset < 0 || source.Offset + source.Length > eboot.Length || source.ClutOffset + source.ClutBytes > eboot.Length)
            throw new InvalidDataException("Invalid embedded texture reference.");
        int pitch = source.Stride * source.Bpp / 8, rowsPerBlock = Math.Max(1, pitch / 16);
        var pixels = new Color[source.Width * source.Height];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int byteX = x * source.Bpp / 8;
                int at = source.Offset + y / 8 * rowsPerBlock * 128 + byteX / 16 * 128 + y % 8 * 16 + byteX % 16;
                if (at >= eboot.Length) throw new InvalidDataException("Embedded texture exceeds source.");
                int index = eboot[at];
                if (source.Bpp == 4) index = index >> 4 * (x % 2) & 15;
                int clut = source.ClutOffset + index * 4;
                if (clut + 4 > source.ClutOffset + source.ClutBytes) throw new InvalidDataException("Embedded palette index out of range.");
                pixels[y * source.Width + x] = new Color(eboot[clut], eboot[clut + 1], eboot[clut + 2], eboot[clut + 3]);
            }
        byte[] rgba = new byte[pixels.Length * 4];
        Buffer.BlockCopy(Array.ConvertAll(pixels, p => p.PackedValue), 0, rgba, 0, rgba.Length);
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rgba)) != source.RgbaSha256)
            throw new InvalidDataException("Embedded texture pixels differ from the measured original: " + source.Key);
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(pixels);
        owned.Add(texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in owned) texture.Dispose();
        owned.Clear();
        MainMenuBackground.Dispose(); MainMenuLabels.Dispose();
        OptionsBackground.Dispose(); OptionsStatic.Dispose(); OptionsText.Dispose(); OptionsPieces.Dispose();
        DialogText?.Dispose(); DialogPieces?.Dispose();
        foreach (var back in dialogBacks.Values) back.Dispose();
        Layer.Dispose(); effect.Dispose(); alpha.Dispose(); additive.Dispose(); maskBlend.Dispose();
        writeStencil.Dispose(); testStencil.Dispose();
    }
}
