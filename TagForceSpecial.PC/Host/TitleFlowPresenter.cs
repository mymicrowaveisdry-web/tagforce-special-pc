using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>The converted OriginalTitleFlow scene showing one measured title-flow frame (storage, data, name entry, questions, quit question),
/// drawn by the GE layer. New glyphs grow the atlas; the layer re-uploads it before drawing.</summary>
internal sealed partial class TitleFlowPresenter : Control
{
    public const string FontKey = "pgf.flow";
    private readonly TitleFlowFrames frames;
    private readonly FlowTextures textures;
    private readonly PgfGlyphAtlas font;
    private readonly GeLayer layer;
    private int uploadedFont = -1;
    private string? rendered;

    public TitleFlowPresenter(TitleFlowFrames frames, FlowTextures textures, PgfGlyphAtlas font)
    {
        this.frames = frames; this.textures = textures; this.font = font;
        layer = new GeLayer(textures.Pixels);
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    public override void _Ready()
    {
        var screen = GD.Load<PackedScene>("res://Gum/OriginalTitleFlow.tscn").Instantiate<Control>();
        AddChild(screen);
        var sprite = screen.GetNode<TextureRect>("FlowFrame");
        sprite.Texture = layer.Texture;
        sprite.Material = CanvasMaterials.Premultiplied;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) layer.Dispose();
    }

    /// <summary>Shows a frame; redrawn only when anything it shows changed.</summary>
    public void Show(string frameId, int buttonFocus = 0, int menuFocus = 0, string? name = null,
        DuelistLevelChoice level = DuelistLevelChoice.FirstTimer, int? menuBackgroundFrame = null)
    {
        string key = $"{frameId}/{buttonFocus}/{menuFocus}/{name}/{level}/{menuBackgroundFrame}";
        if (key == rendered) return;
        var commands = frames.Render(frameId, buttonFocus, menuFocus, name, level, menuBackgroundFrame);
        if (font.Version != uploadedFont)
        {
            textures.SetDynamic(FontKey, font.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size, font.Version);
            layer.Invalidate(FontKey);
            uploadedFont = font.Version;
        }
        layer.Render(GeGeometry.FromFlows(commands));
        rendered = key;
    }
}
