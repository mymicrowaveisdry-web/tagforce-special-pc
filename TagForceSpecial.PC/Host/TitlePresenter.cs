using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>The converted OriginalTitle and OriginalTitleStartMenu scenes: each measured layer group rendered once by its own GE layer and
/// bound to the sprite of the same name; the start menu's FlowFrame shows the measured start-menu frame with the focused choice.</summary>
internal sealed partial class TitlePresenter : Control
{
    private const string StartMenuFrame = "start-menu";
    private readonly TitleLayers layers;
    private readonly TitleFlowFrames frames;
    private readonly FlowTextures textures;
    private readonly PgfGlyphAtlas font;
    private readonly Dictionary<string, GeLayer> groups = new(StringComparer.Ordinal);
    private readonly GeLayer startMenu;
    private Control title = null!, choices = null!;
    private int uploadedFont = -1;
    private int? renderedChoice;

    public TitlePresenter(TitleLayers layers, TitleFlowFrames frames, FlowTextures textures, PgfGlyphAtlas font)
    {
        this.layers = layers; this.frames = frames; this.textures = textures; this.font = font;
        foreach (string group in layers.Groups)
        {
            var layer = new GeLayer(textures.Pixels);
            layer.Render(GeGeometry.FromFlows(layers.Commands(group)));
            groups.Add(group, layer);
        }
        startMenu = new GeLayer(textures.Pixels);
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    public override void _Ready()
    {
        title = GD.Load<PackedScene>("res://Gum/OriginalTitle.tscn").Instantiate<Control>();
        choices = GD.Load<PackedScene>("res://Gum/OriginalTitleStartMenu.tscn").Instantiate<Control>();
        AddChild(title);
        AddChild(choices);
        foreach (var screen in new[] { title, choices })
            foreach (var (group, layer) in groups) Bind(screen.GetNode<TextureRect>(group), layer);
        Bind(choices.GetNode<TextureRect>("FlowFrame"), startMenu);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        foreach (var layer in groups.Values) layer.Dispose();
        startMenu.Dispose();
    }

    /// <summary>The press-start title, or the start menu with <paramref name="choice"/> focused (0 New Game, 1 Continue).</summary>
    public void Show(bool choicesVisible, int choice = 0)
    {
        title.Visible = !choicesVisible;
        choices.Visible = choicesVisible;
        if (!choicesVisible || renderedChoice == choice) return;
        var commands = frames.Render(StartMenuFrame, menuFocus: choice);
        if (font.Version != uploadedFont)
        {
            textures.SetDynamic(TitleFlowPresenter.FontKey, font.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size, font.Version);
            startMenu.Invalidate(TitleFlowPresenter.FontKey);
            uploadedFont = font.Version;
        }
        startMenu.Render(GeGeometry.FromFlows(commands));
        renderedChoice = choice;
    }

    private static void Bind(TextureRect sprite, GeLayer layer)
    {
        // Rendered layers contain premultiplied RGB.
        sprite.Texture = layer.Texture;
        sprite.Material = CanvasMaterials.Premultiplied;
    }
}
