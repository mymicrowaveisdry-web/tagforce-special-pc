using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.Title;

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

    public static FlowProfile LoadProfile() => FlowProfile.LoadEmbedded();

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
        foreach (var label in submenuProfiles.Values.SelectMany(p => p.Rows).Select(r => r.Label)) _ = Source(label.Source, label.Offset, label.Start, label.Chars);
    }

    private string Source(string source, int offset, int start, int chars) => TitleFlowText.Read(source switch
    {
        "eboot" => eboot, "title-menu" => titleMenu, "deck" => deckArchive,
        "album" => albumArchive ?? throw new InvalidDataException("The Card Album archive is not in this import."),
        "cardinfo" => cardInfoArchive ?? throw new InvalidDataException("The card info archive is not in this import."),
        _ => storedSources.GetValueOrDefault(source) ?? throw new InvalidDataException("Unknown or missing text source " + source + ".")
    }, offset, start, chars);

    // Helpers other screens (the Card Album) draw with, so they share this layer's textures, font and measured-draw rules.
    private byte[]? albumArchive, cardInfoArchive;
    private readonly Dictionary<string, byte[]> storedSources = new(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="draw"/> with <paramref name="target"/> bound, restoring the previous target and default states.</summary>
    public void Within(RenderTarget2D target, Action draw) => WithTarget(target, draw);
    public void DrawFlow(FlowDraw draw) => DrawMeasured(draw);
    public void DrawRun(FlowText run) => DrawMeasuredText(Source(run.Source, run.Offset, run.Start, run.Chars), run);
    public void DrawText(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha, int alpha = 255) =>
        DrawLine(value, x, y, scaleX, scaleY, color, shadowAlpha, null, alpha);
    public float MeasureText(string value, float scaleX) => TextWidth(value, scaleX);
    /// <summary>Text in the card-text font (ltn0), whose advances the card detail uses (measured: 37 of 37 captured characters); the title
    /// font stands in when it is not imported.</summary>
    public void DrawCardFontText(string value, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha) =>
        (cardTextRenderer ?? textRenderer).DrawLine(value, x, y, scaleX, scaleY, color, shadowAlpha);
    public float MeasureCardFontText(string value, float scaleX) => (cardTextRenderer ?? textRenderer).TextWidth(value, scaleX);
    public bool HasCardFont => cardTextRenderer is not null;
    public IReadOnlyList<string> WrapText(string text, float width, float scaleX) => Wrap(text, width, scaleX);

    /// <summary>Adds imported textures under their asset keys (pinned to the original uploads) and the archive the album's labels come from.</summary>
    public void UseAlbumAssets(OriginalScreenAssetResolver assets, IEnumerable<string> keys, string archiveKey, string cardInfoKey)
    {
        UseTextures(assets, keys);
        albumArchive ??= assets.ResolveBytes(archiveKey);
        cardInfoArchive ??= assets.ResolveBytes(cardInfoKey);
    }

    /// <summary>Loads a stored archive used only as a named text source (Free Duel: "charalist", "start", "result").</summary>
    public void UseTextSource(OriginalScreenAssetResolver assets, string source, string key)
    {
        if (!storedSources.ContainsKey(source)) storedSources.Add(source, assets.ResolveBytes(key));
    }

    /// <summary>Adds imported textures under their asset keys (pinned to the original uploads) when not loaded yet.</summary>
    public void UseTextures(OriginalScreenAssetResolver assets, IEnumerable<string> keys)
    {
        foreach (string key in keys.Where(k => !textures.ContainsKey(k))) textures.AddBorrowed(key, assets.Resolve(key).Texture);
    }

    /// <summary>The whole message at a source offset (UTF-16 up to its terminator), normalized like other source text.</summary>
    public string Message(string source, int offset)
    {
        byte[] blob = source switch { "cardinfo" => cardInfoArchive, "album" => albumArchive, "deck" => deckArchive, "eboot" => eboot, "title-menu" => titleMenu, _ => storedSources.GetValueOrDefault(source) }
            ?? throw new InvalidDataException("Unknown or missing text source " + source + ".");
        if (offset < 0 || offset >= blob.Length || offset % 2 != 0) throw new InvalidDataException("Invalid text reference.");
        int end = offset;
        while (end + 1 < blob.Length && (blob[end] != 0 || blob[end + 1] != 0)) end += 2;
        return TitleFlowText.Normalize(System.Text.Encoding.Unicode.GetString(blob, offset, end - offset));
    }

    public Texture2D Texture(string key) => textures[key];

    /// <summary>The stored original bytes behind a text source ("eboot", "cardinfo"), for readers of other data in the same files.</summary>
    public byte[] SourceBytes(string source) => source switch { "eboot" => eboot, "cardinfo" => cardInfoArchive, _ => null }
        ?? throw new InvalidDataException("Unknown or missing source " + source + ".");

    /// <summary>Adds a measured EBOOT texture (decoded from the stored EBOOT and checked against its recorded pixel hash).</summary>
    public void UseEbootTexture(FlowEbootTexture source) { if (!textures.ContainsKey(source.Key)) textures.AddEmbedded(source); }

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
        foreach (var labels in submenuLabels.Values) labels.Dispose();
        OptionsBackground.Dispose(); OptionsStatic.Dispose(); OptionsText.Dispose(); OptionsPieces.Dispose();
        DialogText?.Dispose(); DialogPieces?.Dispose();
        foreach (var back in dialogBacks.Values) back.Dispose();
        Layer.Dispose(); effect.Dispose(); alpha.Dispose(); additive.Dispose(); maskBlend.Dispose();
        writeStencil.Dispose(); testStencil.Dispose();
    }
}
