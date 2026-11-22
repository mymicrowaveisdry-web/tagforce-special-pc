using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>The converted OriginalMainMenu scene bound to the Main Menu's flow layers, drawn by the GE layer. Geometry comes from the
/// generated scene; this class only supplies pixels and the measured per-frame values (bar fade, dim, focus pulse, label slot), as the Gum
/// bindings did.</summary>
internal sealed partial class MainMenuPresenter : Control
{
    public const string FontKey = "pgf.menu";
    private static readonly string[] BasePieces = ["BaseLeft", "BaseCenter", "BaseRight"], FocusPieces = ["FocusLeft", "FocusCenter", "FocusRight"];
    private readonly MainMenuLayers layers;
    private readonly FlowTextures textures;
    private readonly GeLayer background, labels;
    private Control screen = null!;
    private int? renderedBackground;

    public MainMenuPresenter(MainMenuLayers layers, FlowTextures textures, FlowTextSources text, PgfGlyphAtlas font)
    {
        this.layers = layers;
        this.textures = textures;
        background = new GeLayer(textures.Pixels);
        labels = new GeLayer(textures.Pixels, MainMenuLayers.LabelWidth, MainMenuLayers.LabelLayerHeight);
        var labelCommands = layers.Labels(text, font, FontKey);
        // The labels fill the atlas first; the layer uploads it when drawn.
        textures.SetDynamic(FontKey, font.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size, font.Version);
        labels.Render(GeGeometry.FromFlows(labelCommands, labels.Width, labels.Height));
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) { background.Dispose(); labels.Dispose(); }
    }

    public override void _Ready()
    {
        screen = GD.Load<PackedScene>("res://Gum/OriginalMainMenu.tscn").Instantiate<Control>();
        AddChild(screen);
        var backdrop = screen.GetNode<TextureRect>("Background");
        backdrop.Texture = background.Texture;
        backdrop.Material = CanvasMaterials.Premultiplied;
        var panel = textures.Get(MainMenuLayers.PanelKey);
        for (int row = 0; row < MainMenuLayers.Rows; row++)
        {
            var bar = Bar(row);
            foreach (string piece in BasePieces.Concat(FocusPieces))
            {
                var sprite = bar.GetNode<TextureRect>(piece);
                sprite.Texture = new AtlasTexture { Atlas = panel, Region = (Rect2)sprite.GetMeta("gum_texture_region") };
            }
            var label = bar.GetNode<TextureRect>("Label");
            label.Material = CanvasMaterials.Premultiplied;
        }
        GumText.ApplyFonts(screen);
        ShowNotice(null);
    }

    /// <summary>The PC notice for a Main Menu row the PC does not recreate, or none.</summary>
    public void ShowNotice(MainMenuDestination? destination) => GumText.ShowNotice(screen, destination?.Label, destination?.Notice);

    /// <summary>Shows the menu at loop frame <paramref name="gameFrame"/> and entry frame <paramref name="entryFrame"/>. While a submenu is open
    /// the menu is drawn without focus and tinted by <paramref name="dim"/> (255 = undimmed), bars and labels alike.</summary>
    public void Show(int focus, int gameFrame, int entryFrame, int pulse, int dim = 255)
    {
        int key = gameFrame * 64 + Math.Min(entryFrame, MainMenuEntry.ReadyFrame);
        if (key != renderedBackground) { background.Render(GeGeometry.FromFlows(layers.Background(gameFrame, entryFrame))); renderedBackground = key; }
        for (int row = 0; row < MainMenuLayers.Rows; row++)
        {
            var bar = Bar(row);
            bool focused = row == focus && dim == 255 && MainMenuEntry.FocusVisible(entryFrame);
            float barAlpha = MainMenuEntry.BarAlpha(row, entryFrame) / 255f, tint = dim / 255f, glow = pulse / 255f;
            foreach (string piece in BasePieces) bar.GetNode<TextureRect>(piece).Modulate = new Color(tint, tint, tint, barAlpha);
            foreach (string piece in FocusPieces)
            {
                var sprite = bar.GetNode<TextureRect>(piece);
                sprite.Visible = focused;
                // Measured: the pulse writes the same value to the material colour and its alpha.
                if (focused) sprite.Modulate = new Color(glow, glow, glow, glow);
            }
            var label = bar.GetNode<TextureRect>("Label");
            label.Visible = MainMenuEntry.LabelVisible(row, entryFrame);
            var (x, y, w, h) = MainMenuLayers.LabelSource(row, focused);
            label.Texture = new AtlasTexture { Atlas = labels.Texture, Region = new Rect2(x, y, w, h) };
            // Premultiplied layer: scaling RGB only greys the white text and leaves its black shadow and alpha, as measured.
            label.Modulate = new Color(tint, tint, tint, 1);
        }
    }

    /// <summary>Bar bounds in canvas pixels as [x0, y0, x1, y1], in row order (the first containing bar wins, as with the Gum hit test).</summary>
    public IReadOnlyList<float[]> BarRects() => Enumerable.Range(0, MainMenuLayers.Rows)
        .Select(row => Bar(row).GetRect()).Select(r => new[] { r.Position.X, r.Position.Y, r.End.X, r.End.Y }).ToArray();

    private Control Bar(int row) => screen.GetNode<Control>("Bar" + row);
}
