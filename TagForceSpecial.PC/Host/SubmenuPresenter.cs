using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>A converted OriginalDeckMenu or OriginalDatabaseMenu scene: bars from the panel atlas with the submenu's own base pieces, label
/// slots from a GE layer, and the submenu's measured opening alphas, label reveal and focus pulse, as the Gum bindings did.</summary>
internal sealed partial class SubmenuPresenter : Control
{
    private static readonly string[] BasePieces = ["BaseLeft", "BaseCenter", "BaseRight"], FocusPieces = ["FocusLeft", "FocusCenter", "FocusRight"];
    private readonly SubmenuKind kind;
    private readonly SubmenuLayers layers;
    private readonly FlowTextures textures;
    private readonly GeLayer labels;
    private Control screen = null!;

    public SubmenuPresenter(SubmenuKind kind, SubmenuLayers layers, FlowTextures textures, FlowTextSources text, PgfGlyphAtlas font, string fontKey)
    {
        this.kind = kind; this.layers = layers; this.textures = textures;
        labels = new GeLayer(textures.Pixels, MainMenuLayers.LabelWidth, layers.LabelLayerHeight);
        var commands = layers.Labels(text, font, fontKey);
        textures.SetDynamic(fontKey, font.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size, font.Version);
        labels.Render(GeGeometry.FromFlows(commands, labels.Width, labels.Height));
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    public override void _Ready()
    {
        screen = GD.Load<PackedScene>($"res://Gum/{Submenus.GumScreen(kind)}.tscn").Instantiate<Control>();
        AddChild(screen);
        var panel = textures.Get(MainMenuLayers.PanelKey);
        for (int row = 0; row < layers.Profile.Rows.Length; row++)
        {
            var bar = Bar(row);
            foreach (var (piece, x, y, w, h) in layers.BarPieces)
                bar.GetNode<TextureRect>(piece).Texture = new AtlasTexture { Atlas = panel, Region = new Rect2(x, y, w, h) };
            bar.GetNode<TextureRect>("Label").Material = CanvasMaterials.Premultiplied;
        }
        GumText.ApplyFonts(screen);
        ShowNotice(null);
    }

    /// <summary>The PC notice for a submenu row the PC does not recreate, or none.</summary>
    public void ShowNotice(SubmenuDestination? destination) => GumText.ShowNotice(screen, destination?.Label, destination?.Notice);

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) labels.Dispose();
    }

    /// <summary>The submenu at its own <paramref name="frame"/>; focus shows only once the menu is ready.</summary>
    public void Show(int focus, int frame, bool focusVisible)
    {
        var profile = layers.Profile;
        float glow = profile.PulseAt(frame) / 255f;
        for (int row = 0; row < profile.Rows.Length; row++)
        {
            var bar = Bar(row);
            bool focused = row == focus && focusVisible;
            foreach (string piece in BasePieces) bar.GetNode<TextureRect>(piece).Modulate = new Color(1, 1, 1, profile.BarAlpha(row, frame) / 255f);
            foreach (string piece in FocusPieces)
            {
                var sprite = bar.GetNode<TextureRect>(piece);
                sprite.Visible = focused;
                // Measured as on the Main Menu: the pulse is written to the material colour and its alpha.
                if (focused) sprite.Modulate = new Color(glow, glow, glow, glow);
            }
            var label = bar.GetNode<TextureRect>("Label");
            label.Visible = profile.LabelVisible(row, frame);
            var (x, y, w, h) = MainMenuLayers.LabelSource(row, focused);
            label.Texture = new AtlasTexture { Atlas = labels.Texture, Region = new Rect2(x, y, w, h) };
        }
    }

    private Control Bar(int row) => screen.GetNode<Control>("Bar" + row);
}
