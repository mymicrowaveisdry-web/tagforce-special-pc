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

/// <summary>Draws flow frames from validated source textures and a PGF glyph atlas into one retained layer.</summary>
public sealed partial class TitleFlowVisuals : IDisposable
{
    public const string RequiredFontSha256 = PgfTextRenderer.RequiredFontSha256;
    private readonly GraphicsDevice device;
    private readonly FlowProfile profile;
    private readonly Dictionary<string, FlowFrame> frames = new(StringComparer.Ordinal);
    private readonly FlowTextureCache textures;
    private readonly PgfTextRenderer textRenderer;
    private readonly byte[] eboot, titleMenu;
    private readonly BasicEffect effect;
    private readonly BlendState alpha, additive, maskBlend;
    private readonly DepthStencilState writeStencil, testStencil;
    public RenderTarget2D Layer { get; }
    public IEnumerable<string> FrameIds => frames.Keys;
    public bool OriginalFontAvailable => textRenderer.OriginalFontAvailable;
    /// <summary>Fallback text used only when no game font was imported.</summary>
    public List<(string Text, int X, int Y, Color Color)> FallbackLines => textRenderer.FallbackLines;

    public static FlowProfile LoadProfile()
    {
        using var stream = typeof(TitleFlowVisuals).Assembly.GetManifestResourceStream(
            "TagForceSpecial.PC.App.Screens.Title.title-flow-frames.json") ?? throw new InvalidDataException("Missing measured title-flow profile.");
        var value = JsonSerializer.Deserialize<FlowProfile>(stream) ?? throw new InvalidDataException("Invalid title-flow profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Title-flow profile/source mismatch.");
        return value;
    }

    public const string CardTextFontSha256 = PgfTextRenderer.CardTextFontSha256;
    /// <summary>Card-face text renderer (system ltn0), or null when that font was not imported.</summary>
    private readonly PgfTextRenderer? cardTextRenderer;

    public TitleFlowVisuals(GraphicsDevice device, OriginalScreenAssetResolver assets, byte[]? fontFile, byte[]? cardTextFont = null)
    {
        cardTextRenderer = cardTextFont is null ? null
            : new(device, cardTextFont, (texture, quad, color) => DrawQuad(texture, quad, 0, 0, true, color), PgfTextRenderer.CardTextFontSha256);
        this.device = device;
        profile = LoadProfile();
        foreach (var frame in profile.Frames) frames.Add(frame.Id, frame);
        eboot = assets.ResolveBytes(TitleFlowReferenceResources.EbootKey);
        titleMenu = assets.ResolveBytes(TitleFlowReferenceResources.TitleMenuKey);
        textures = new(device, eboot);
        foreach (var embedded in profile.EbootTextures) textures.AddEmbedded(embedded);
        foreach (var key in profile.Frames.SelectMany(f => f.Draws).Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal))
            if (!textures.ContainsKey(key)) textures.AddBorrowed(key, assets.Resolve(key).Texture);
        // Menu bars come from the panel atlas referenced by the Main Menu/start menu profile.
        textures.AddBorrowed("title.flow.m-menu-panel01", assets.Resolve("title.flow.m-menu-panel01").Texture);
        textRenderer = new(device, fontFile, (texture, quad, color) => DrawQuad(texture, quad, 0, 0, true, color));
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        InitializeMainMenu();
        InitializeDeckMenu();
        InitializeOptions(assets);
        InitializeDeckEdit(assets);
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
        "eboot" => eboot, "title-menu" => titleMenu, "deck" => deckArchive, _ => throw new InvalidDataException("Unknown text source.")
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
                // Measures the level sentence once; its variants differ only in the bracketed name.
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
        effect.Texture = draw.AssetKey is { } key ? (draw.Add is { } add ? textures.Specular(key, draw.Color, add) : textures[key]) : null;
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

    private void DrawLine(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, float[]? measuredX = null, int alpha = 255) =>
        textRenderer.DrawLine(value, x, y, scaleX, scaleY, color, shadowAlpha, measuredX, alpha);

    public void Dispose()
    {
        textures.Dispose(); textRenderer.Dispose();
        MainMenuBackground.Dispose(); MainMenuLabels.Dispose();
        OptionsBackground.Dispose(); OptionsStatic.Dispose(); OptionsText.Dispose(); OptionsPieces.Dispose();
        DialogText?.Dispose(); DialogPieces?.Dispose();
        foreach (var back in dialogBacks.Values) back.Dispose();
        Layer.Dispose(); effect.Dispose(); alpha.Dispose(); additive.Dispose(); maskBlend.Dispose();
        writeStencil.Dispose(); testStencil.Dispose();
    }
}
