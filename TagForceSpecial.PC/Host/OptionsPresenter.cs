using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>The converted OriginalOptions scene bound to the Options layers on GE layers: the animated background with the Options title, the
/// static panel, the text and piece atlases, and the two choice dialogs. Values, focus and pulse only switch slots, widths and visibility, as
/// the Gum bindings did.</summary>
internal sealed partial class OptionsPresenter : Control
{
    public const string FontKey = "pgf.options";
    private static readonly string[] DialogNames = ["CancelDialog", "SaveDialog"];
    private readonly OptionsLayers layers;
    private readonly FlowTextures textures;
    private readonly GeLayer background, panel, pieces, text, dialogText, dialogPieces;
    private readonly Dictionary<string, GeLayer> dialogBacks = new(StringComparer.Ordinal);
    private Control screen = null!;
    private int? renderedBackground;

    public OptionsPresenter(OptionsLayers layers, FlowTextures textures, FlowTextSources sources, PgfGlyphAtlas font)
    {
        this.layers = layers; this.textures = textures;
        background = new GeLayer(textures.Pixels);
        panel = new GeLayer(textures.Pixels);
        pieces = new GeLayer(textures.Pixels, OptionsLayers.PiecesWidth, OptionsLayers.PiecesHeight);
        text = new GeLayer(textures.Pixels, OptionsLayers.TextAtlasWidth, layers.TextAtlasHeight);
        dialogText = new GeLayer(textures.Pixels, OptionsLayers.DialogTextWidth, layers.DialogTextHeight);
        dialogPieces = new GeLayer(textures.Pixels, OptionsLayers.DialogPiecesWidth, OptionsLayers.DialogPiecesHeight);
        // Both text layers fill the glyph atlas first; the layers upload it when drawn.
        var textCommands = layers.Text(sources, font, FontKey);
        var dialogCommands = layers.DialogText(sources, font, FontKey);
        textures.SetDynamic(FontKey, font.Pixels, PgfGlyphAtlas.Size, PgfGlyphAtlas.Size, font.Version);
        text.Render(GeGeometry.FromFlows(textCommands, text.Width, text.Height));
        dialogText.Render(GeGeometry.FromFlows(dialogCommands, dialogText.Width, dialogText.Height));
        panel.Render(GeGeometry.FromFlows(layers.Static()));
        pieces.Render(GeGeometry.FromFlows(layers.Pieces(), pieces.Width, pieces.Height));
        dialogPieces.Render(GeGeometry.FromFlows(layers.DialogPieces(), dialogPieces.Width, dialogPieces.Height));
        foreach (string id in new[] { OptionsFrameProfile.CancelDialog, OptionsFrameProfile.SaveQuestion })
        {
            var back = new GeLayer(textures.Pixels);
            back.Render(GeGeometry.FromFlows(layers.DialogBack(id)));
            dialogBacks[id] = back;
        }
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        foreach (var layer in new[] { background, panel, pieces, text, dialogText, dialogPieces }.Concat(dialogBacks.Values)) layer.Dispose();
    }

    public override void _Ready()
    {
        screen = GD.Load<PackedScene>("res://Gum/OriginalOptions.tscn").Instantiate<Control>();
        AddChild(screen);
        var p = layers.Profile;
        Bind(Sprite("Background"), background.Texture, (0, 0, 480, 272));
        Bind(Sprite("Static"), panel.Texture, (0, 0, 480, 272));
        BindDialog(DialogNames[0], OptionsFrameProfile.CancelDialog);
        BindDialog(DialogNames[1], OptionsFrameProfile.SaveQuestion);
        for (int g = 0; g < p.Groups.Length; g++) Bind(Sprite("Group" + g), text.Texture, layers.GroupSlot(g));
        var atlas = textures.Get(p.AtlasKey);
        for (int i = 0; i < p.Rows.Length; i++)
        {
            var row = Row(i);
            Bind(row.GetNode<TextureRect>("Focus"), pieces.Texture, OptionsLayers.FocusPieceSource);
            Bind(row.GetNode<TextureRect>("Label"), text.Texture, layers.LabelSlot(i));
            if (p.Rows[i].Values.Length > 0) Bind(row.GetNode<TextureRect>("Value"), text.Texture, layers.ValueSlot(i, 0));
            Bind(row.GetNode<TextureRect>("ArrowLeft"), atlas, OptionsLayers.ArrowSource(p.Arrows["left"]), straight: true);
            Bind(row.GetNode<TextureRect>("ArrowRight"), atlas, OptionsLayers.ArrowSource(p.Arrows["right"]), straight: true);
            Bind(row.GetNode<TextureRect>("SlotsEmpty"), pieces.Texture, OptionsLayers.SegmentsEmptySource);
            Bind(row.GetNode<TextureRect>("SlotsFull"), pieces.Texture, OptionsLayers.SegmentsFullSource);
            bool slider = OptionsCatalog.Rows[i].Kind == OptionKind.Slider;
            foreach (string piece in new[] { "Value", "ArrowLeft", "ArrowRight" }) row.GetNode<TextureRect>(piece).Visible = !slider;
            foreach (string piece in new[] { "SlotsEmpty", "SlotsFull" }) row.GetNode<TextureRect>(piece).Visible = slider;
        }
        Bind(Sprite("Help"), text.Texture, layers.HelpSlot(0));
        GumText.ApplyFonts(screen);
        ShowList(null);
    }

    /// <summary>The list at Main Menu loop frame <paramref name="gameFrame"/> and entry frame <paramref name="entryFrame"/>, with the focus band at
    /// <paramref name="pulse"/>, and the cancel question over it when <paramref name="cancelFocus"/> is set.</summary>
    public void ShowList(IReadOnlyList<int>? values, int focus = 0, int gameFrame = 0, int entryFrame = MainMenuEntry.ReadyFrame, int pulse = 255,
        int? cancelFocus = null)
    {
        ShowSaveQuestion(null);
        if (values is null) return;
        RenderBackground(gameFrame, entryFrame);
        var p = layers.Profile;
        for (int i = 0; i < p.Rows.Length; i++)
        {
            var row = Row(i);
            var band = row.GetNode<TextureRect>("Focus");
            band.Visible = i == focus;
            // Premultiplied layer: scaling every channel by the pulse scales the band's alpha.
            float glow = pulse / 255f;
            band.Modulate = new Color(glow, glow, glow, glow);
            var definition = OptionsCatalog.Rows[i];
            if (definition.Kind == OptionKind.Slider)
            {
                // A level is the number of filled cells: 0 to 15.
                var full = row.GetNode<TextureRect>("SlotsFull");
                int width = values[i] * p.SegmentStep;
                full.Visible = width > 0;
                full.Size = new Vector2(width, full.Size.Y);
                var (sx, sy, _, sh) = OptionsLayers.SegmentsFullSource;
                Region(full, (sx, sy, Math.Max(1, width), sh));
                continue;
            }
            Region(row.GetNode<TextureRect>("Value"), layers.ValueSlot(i, values[i]));
            // Dims the arrow on a side with no further value.
            Region(row.GetNode<TextureRect>("ArrowLeft"), OptionsLayers.ArrowSource(p.Arrows[values[i] == 0 ? "left-dim" : "left"]));
            Region(row.GetNode<TextureRect>("ArrowRight"), OptionsLayers.ArrowSource(p.Arrows[values[i] == definition.Count - 1 ? "right-dim" : "right"]));
        }
        Region(Sprite("Help"), layers.HelpSlot(focus));
        // Authored PC note, never original UI: every row whose value no PC system reads yet says so.
        var note = screen.GetNode<Label>("PcNote");
        note.Visible = !OptionsCatalog.Rows[focus].Applied;
        note.Position = new Vector2(note.Position.X, p.Rows[focus].BandTop - 1);
        ShowDialog(DialogNames[0], cancelFocus);
    }

    /// <summary>The save question on the bare animated background (entry frame 0 draws neither header nor title); the list layers hide.</summary>
    public void ShowSaveQuestion(int? focus, int gameFrame = 0)
    {
        bool list = focus is null;
        foreach (string name in new[] { "Static", "Help" }) Sprite(name).Visible = list;
        for (int g = 0; g < layers.Profile.Groups.Length; g++) Sprite("Group" + g).Visible = list;
        for (int i = 0; i < layers.Profile.Rows.Length; i++) Row(i).Visible = list;
        if (!list) { screen.GetNode<Label>("PcNote").Visible = false; ShowDialog(DialogNames[0], null); RenderBackground(gameFrame, 0); }
        ShowDialog(DialogNames[1], focus);
    }

    private void RenderBackground(int gameFrame, int entryFrame)
    {
        int key = gameFrame * 64 + Math.Min(entryFrame, MainMenuEntry.ReadyFrame);
        if (key == renderedBackground) return;
        background.Render(GeGeometry.FromFlows(layers.Background(gameFrame, entryFrame)));
        renderedBackground = key;
    }

    /// <summary>One OriginalChoiceDialog instance per measured dialog: its back layer, message lines and buttons come from that dialog's frame.</summary>
    private void BindDialog(string instance, string id)
    {
        var dialog = screen.GetNode<Control>(instance);
        var frame = layers.Dialog(id);
        Bind(dialog.GetNode<TextureRect>("Back"), dialogBacks[id].Texture, (0, 0, 480, 272));
        for (int line = 0; line < 2; line++)
        {
            var message = dialog.GetNode<TextureRect>("Message" + line);
            message.Visible = line < frame.Text.Length;
            if (!message.Visible) continue;
            Bind(message, dialogText.Texture, layers.DialogMessageSlot(id, line));
            message.Position = new Vector2(message.Position.X, OptionsLayers.SlotTop(frame.Text[line].Y));
        }
        var buttons = frame.Buttons ?? throw new InvalidDataException("Choice dialogs need two buttons.");
        for (int b = 0; b < 2; b++)
        {
            var button = dialog.GetNode<Control>("Button" + b);
            button.Position = new Vector2(buttons[b].Rect[0], buttons[b].Rect[1]);
            Bind(button.GetNode<TextureRect>("Plain"), dialogPieces.Texture, OptionsLayers.DialogPlainSource);
            Bind(button.GetNode<TextureRect>("Focus"), dialogPieces.Texture, OptionsLayers.DialogFocusSource);
            var label = button.GetNode<TextureRect>("Label");
            Bind(label, dialogText.Texture, layers.DialogLabelSlot(id, b));
            label.Position = new Vector2(label.Position.X, OptionsLayers.SlotTop(buttons[b].Label.Y) - buttons[b].Rect[1]);
        }
    }

    /// <summary>The focused button shows only its focus pieces, the other only its plain piece.</summary>
    private void ShowDialog(string instance, int? focus)
    {
        var dialog = screen.GetNode<Control>(instance);
        dialog.Visible = focus is not null;
        if (focus is not { } value) return;
        for (int b = 0; b < 2; b++)
        {
            var button = dialog.GetNode<Control>("Button" + b);
            button.GetNode<TextureRect>("Focus").Visible = b == value;
            button.GetNode<TextureRect>("Plain").Visible = b != value;
        }
    }

    /// <summary>GE layers hold premultiplied colour; the Options atlas is straight alpha.</summary>
    private static void Bind(TextureRect sprite, Texture2D texture, (int X, int Y, int Width, int Height) source, bool straight = false)
    {
        sprite.Texture = new AtlasTexture { Atlas = texture, Region = new Rect2(source.X, source.Y, source.Width, source.Height) };
        if (!straight) sprite.Material = CanvasMaterials.Premultiplied;
    }

    private static void Region(TextureRect sprite, (int X, int Y, int Width, int Height) source) =>
        ((AtlasTexture)sprite.Texture).Region = new Rect2(source.X, source.Y, source.Width, source.Height);

    private TextureRect Sprite(string name) => screen.GetNode<TextureRect>(name);
    private Control Row(int row) => screen.GetNode<Control>("Row" + row);
}
