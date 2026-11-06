using Gum;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.UI;

/// <summary>Retained visuals only. The application mapper owns all input; GumService.Update is deliberately not called.</summary>
public sealed class GumPresentation : IDisposable
{
    private readonly Dictionary<string, GraphicalUiElement> roots = new(StringComparer.Ordinal);
    private readonly GumService gum = GumService.Default;
    private readonly Texture2D noticeShade;
    public int RootCount => roots.Count;
    public int AttachedRootCount => gum.Root.Children.Count;
    public int CachedTextureCount => RenderingLibrary.Content.LoaderManager.Self.CachedDisposables.Values.OfType<Texture2D>().Count();
    public GumPresentation(GraphicsDevice device)
    {
        var project = gum.Initialize(device, Path.Combine(AppContext.BaseDirectory, "UI/Gum/GumProject.gumx"))
            ?? throw new InvalidDataException("Gum project did not load.");
        gum.CanvasWidth = 480; gum.CanvasHeight = 272;
        foreach (string name in new[] { "OriginalTitle", "OriginalTitleStartMenu", "OriginalTitleFlow", "OriginalMainMenu", "OriginalOptions", "NativeToolsMenu", "NativePreferences" })
        {
            var root = project.Screens.Single(s => s.Name == name).ToGraphicalUiElement();
            root.Visible = false;
            root.AddToRoot();
            roots.Add(name, root);
        }
        noticeShade = new Texture2D(device, 1, 1);
        noticeShade.SetData([Color.White]);
        var shade = MainMenuSprite(roots["OriginalMainMenu"], "NoticeShade");
        shade.Texture = noticeShade;
        shade.TextureAddress = Gum.Managers.TextureAddress.Custom;
        shade.SourceRectangle = new Rectangle(0, 0, 1, 1);
        shade.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.NonPremultiplied;
    }
    private static Gum.GueDeriving.SpriteRuntime MainMenuSprite(GraphicalUiElement parent, string name) =>
        parent.GetGraphicalUiElementByName(name) as Gum.GueDeriving.SpriteRuntime
            ?? throw new InvalidDataException("Missing Main Menu sprite " + name + ".");
    /// <summary>Binds the eight OriginalMenuBar instances to the source panel atlas and the label layer. The measured
    /// pieces and rectangles live in the Gum component; only private-cache textures arrive at runtime.</summary>
    public void BindMainMenu(App.Screens.Title.TitleFlowVisuals visuals)
    {
        var root = roots["OriginalMainMenu"];
        var background = MainMenuSprite(root, "Background");
        background.Texture = visuals.MainMenuBackground;
        background.TextureAddress = Gum.Managers.TextureAddress.Custom;
        background.SourceRectangle = new Rectangle(0, 0, 480, 272);
        background.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend;
        var seen = new HashSet<GraphicalUiElement>();
        for (int i = 0; i < App.Screens.Title.TitleFlow.MainMenuRows; i++)
        {
            var bar = root.GetGraphicalUiElementByName("Bar" + i) ?? throw new InvalidDataException("Expected eight menu bars.");
            if (!seen.Add(bar)) throw new InvalidDataException("Menu bars must be separate instances.");
            foreach (var (piece, source) in App.Screens.Title.TitleFlowVisuals.MenuBarPieces)
            {
                var sprite = MainMenuSprite(bar, piece);
                sprite.Texture = visuals.MenuPanel;
                sprite.TextureAddress = Gum.Managers.TextureAddress.Custom;
                sprite.SourceRectangle = source;
                sprite.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.NonPremultiplied;
            }
            var label = MainMenuSprite(bar, "Label");
            label.Texture = visuals.MainMenuLabels;
            label.TextureAddress = Gum.Managers.TextureAddress.Custom;
            label.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend;
        }
    }
    /// <summary>Focus comes from the application's flow state; Gum holds no selection of its own.</summary>
    public void UpdateMainMenu(int focus, int pulse, int entryFrame)
    {
        var root = roots["OriginalMainMenu"];
        for (int i = 0; i < App.Screens.Title.TitleFlow.MainMenuRows; i++)
        {
            var bar = root.GetGraphicalUiElementByName("Bar" + i)!;
            bool focused = i == focus && App.Screens.Title.MainMenuEntry.FocusVisible(entryFrame);
            int barAlpha = App.Screens.Title.MainMenuEntry.BarAlpha(i, entryFrame);
            foreach (string piece in new[] { "BaseLeft", "BaseCenter", "BaseRight" })
                MainMenuSprite(bar, piece).Alpha = barAlpha;
            foreach (string piece in new[] { "FocusLeft", "FocusCenter", "FocusRight" })
            {
                var sprite = MainMenuSprite(bar, piece);
                sprite.Visible = focused;
                // Measured: the pulse writes the same value to the material colour and its alpha.
                if (focused) { sprite.Red = pulse; sprite.Green = pulse; sprite.Blue = pulse; sprite.Alpha = pulse; }
            }
            var label = MainMenuSprite(bar, "Label");
            label.Visible = App.Screens.Title.MainMenuEntry.LabelVisible(i, entryFrame);
            label.SourceRectangle = App.Screens.Title.TitleFlowVisuals.MenuLabelSource(i, focused);
        }
    }
    private static void Bind(Gum.GueDeriving.SpriteRuntime sprite, Texture2D texture, Rectangle source, bool premultiplied)
    {
        sprite.Texture = texture;
        sprite.TextureAddress = Gum.Managers.TextureAddress.Custom;
        sprite.SourceRectangle = source;
        // Render targets hold premultiplied colour; source atlases are straight alpha.
        sprite.BlendState = premultiplied ? Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend : Microsoft.Xna.Framework.Graphics.BlendState.NonPremultiplied;
    }

    /// <summary>Binds the Options screen's measured layers once; the 13 OriginalOptionRow instances then only switch slots and widths.</summary>
    public void BindOptions(App.Screens.Title.TitleFlowVisuals visuals)
    {
        var root = roots["OriginalOptions"];
        Bind(MainMenuSprite(root, "Background"), visuals.OptionsBackground, new Rectangle(0, 0, 480, 272), true);
        Bind(MainMenuSprite(root, "Static"), visuals.OptionsStatic, new Rectangle(0, 0, 480, 272), true);
        BindChoiceDialog(root, "CancelDialog", visuals, App.Screens.Title.OptionsFrameProfile.CancelDialog);
        BindChoiceDialog(root, "SaveDialog", visuals, App.Screens.Title.OptionsFrameProfile.SaveQuestion);
        for (int g = 0; g < visuals.OptionsProfile.Groups.Length; g++)
            Bind(MainMenuSprite(root, "Group" + g), visuals.OptionsText, visuals.GroupSlot(g), true);
        var profile = visuals.OptionsProfile;
        for (int i = 0; i < profile.Rows.Length; i++)
        {
            var row = root.GetGraphicalUiElementByName("Row" + i) ?? throw new InvalidDataException("Expected 13 option rows.");
            Bind(MainMenuSprite(row, "Focus"), visuals.OptionsPieces, App.Screens.Title.TitleFlowVisuals.FocusPieceSource, true);
            Bind(MainMenuSprite(row, "Label"), visuals.OptionsText, visuals.LabelSlot(i), true);
            Bind(MainMenuSprite(row, "Value"), visuals.OptionsText, profile.Rows[i].Values.Length > 0 ? visuals.ValueSlot(i, 0) : Rectangle.Empty, true);
            Bind(MainMenuSprite(row, "ArrowLeft"), visuals.OptionsAtlas, Source(profile.Arrows["left"]), false);
            Bind(MainMenuSprite(row, "ArrowRight"), visuals.OptionsAtlas, Source(profile.Arrows["right"]), false);
            Bind(MainMenuSprite(row, "SlotsEmpty"), visuals.OptionsPieces, App.Screens.Title.TitleFlowVisuals.SegmentsEmptySource, true);
            Bind(MainMenuSprite(row, "SlotsFull"), visuals.OptionsPieces, App.Screens.Title.TitleFlowVisuals.SegmentsFullSource, true);
            bool slider = App.Screens.Title.OptionsCatalog.Rows[i].Kind == App.Screens.Title.OptionKind.Slider;
            foreach (string piece in new[] { "Value", "ArrowLeft", "ArrowRight" }) MainMenuSprite(row, piece).Visible = !slider;
            foreach (string piece in new[] { "SlotsEmpty", "SlotsFull" }) MainMenuSprite(row, piece).Visible = slider;
        }
        Bind(MainMenuSprite(root, "Help"), visuals.OptionsText, visuals.HelpSlot(0), true);
    }

    private static Rectangle Source(App.Screens.Title.OptionsPixelQuad q) => new(q.Source[0], q.Source[1], q.Source[2] - q.Source[0], q.Source[3] - q.Source[1]);

    /// <summary>Values, focus and help come from the application's Options session; Gum holds no state of its own.</summary>
    public void UpdateOptions(App.Screens.Title.TitleFlowVisuals visuals, IReadOnlyList<int> values, int focus, int pulse)
    {
        var root = roots["OriginalOptions"];
        var profile = visuals.OptionsProfile;
        for (int i = 0; i < profile.Rows.Length; i++)
        {
            var row = root.GetGraphicalUiElementByName("Row" + i)!;
            var band = MainMenuSprite(row, "Focus");
            band.Visible = i == focus;
            // Premultiplied layer: scaling every channel by the pulse scales the band's alpha.
            band.Red = pulse; band.Green = pulse; band.Blue = pulse; band.Alpha = pulse;
            var definition = App.Screens.Title.OptionsCatalog.Rows[i];
            if (definition.Kind == App.Screens.Title.OptionKind.Slider)
            {
                // A level is the number of filled cells: 0 to 15.
                var full = MainMenuSprite(row, "SlotsFull");
                int width = values[i] * profile.SegmentStep;
                full.Visible = width > 0;
                full.Width = width;
                full.SourceRectangle = new Rectangle(App.Screens.Title.TitleFlowVisuals.SegmentsFullSource.X, App.Screens.Title.TitleFlowVisuals.SegmentsFullSource.Y, Math.Max(1, width), 10);
                continue;
            }
            MainMenuSprite(row, "Value").SourceRectangle = visuals.ValueSlot(i, values[i]);
            // Observed: the arrow on a side with no further value is dimmed.
            MainMenuSprite(row, "ArrowLeft").SourceRectangle = Source(profile.Arrows[values[i] == 0 ? "left-dim" : "left"]);
            MainMenuSprite(row, "ArrowRight").SourceRectangle = Source(profile.Arrows[values[i] == definition.Count - 1 ? "right-dim" : "right"]);
        }
        MainMenuSprite(root, "Help").SourceRectangle = visuals.HelpSlot(focus);
        // Authored PC note, never original UI: every row whose value no PC system reads yet says so.
        var note = root.GetGraphicalUiElementByName("PcNote")!;
        note.Visible = !App.Screens.Title.OptionsCatalog.Rows[focus].Applied;
        note.Y = profile.Rows[focus].BandTop - 1;
    }

    /// <summary>One OriginalChoiceDialog instance per measured dialog: its back layer, message lines and buttons come from that dialog's frame.</summary>
    private static void BindChoiceDialog(GraphicalUiElement root, string instance, App.Screens.Title.TitleFlowVisuals visuals, string dialogId)
    {
        var dialog = root.GetGraphicalUiElementByName(instance) ?? throw new InvalidDataException("Missing OriginalChoiceDialog " + instance + ".");
        var frame = visuals.DialogFrame(dialogId);
        Bind(MainMenuSprite(dialog, "Back"), visuals.DialogBack(dialogId), new Rectangle(0, 0, 480, 272), true);
        for (int line = 0; line < 2; line++)
        {
            var message = MainMenuSprite(dialog, "Message" + line);
            message.Visible = line < frame.Text.Length;
            if (!message.Visible) continue;
            Bind(message, visuals.DialogText, visuals.DialogMessageSlot(dialogId, line), true);
            message.Y = MathF.Floor(frame.Text[line].Y);
        }
        var buttons = frame.Buttons ?? throw new InvalidDataException("Choice dialogs need two buttons.");
        for (int b = 0; b < 2; b++)
        {
            var button = dialog.GetGraphicalUiElementByName("Button" + b)!;
            button.X = buttons[b].Rect[0]; button.Y = buttons[b].Rect[1];
            Bind(MainMenuSprite(button, "Plain"), visuals.DialogPieces, App.Screens.Title.TitleFlowVisuals.DialogPlainSource, true);
            Bind(MainMenuSprite(button, "Focus"), visuals.DialogPieces, App.Screens.Title.TitleFlowVisuals.DialogFocusSource, true);
            var label = MainMenuSprite(button, "Label");
            Bind(label, visuals.DialogText, visuals.DialogLabelSlot(dialogId, b), true);
            label.Y = MathF.Floor(buttons[b].Label.Y) - buttons[b].Rect[1];
        }
    }

    /// <summary>Observed: the focused button shows only its focus pieces, the other only its plain piece.</summary>
    private void ShowChoiceDialog(string instance, int? focus)
    {
        var dialog = roots["OriginalOptions"].GetGraphicalUiElementByName(instance)!;
        dialog.Visible = focus is not null;
        if (focus is not { } value) return;
        for (int b = 0; b < 2; b++)
        {
            var button = dialog.GetGraphicalUiElementByName("Button" + b)!;
            MainMenuSprite(button, "Focus").Visible = b == value;
            MainMenuSprite(button, "Plain").Visible = b != value;
        }
    }

    public void ShowOptionsDialog(int? cancelFocus) => ShowChoiceDialog("CancelDialog", cancelFocus);

    /// <summary>The save question sits on the bare animated background, so the list layers hide while it shows.</summary>
    public void ShowOptionsSaveQuestion(int? focus, int rows, int groups)
    {
        var root = roots["OriginalOptions"];
        bool list = focus is null;
        foreach (string name in new[] { "Static", "Help" }) MainMenuSprite(root, name).Visible = list;
        for (int g = 0; g < groups; g++) MainMenuSprite(root, "Group" + g).Visible = list;
        for (int i = 0; i < rows; i++) root.GetGraphicalUiElementByName("Row" + i)!.Visible = list;
        if (!list) root.GetGraphicalUiElementByName("PcNote")!.Visible = false;
        ShowChoiceDialog("SaveDialog", focus);
    }

    public void ShowMainMenuNotice(App.Screens.Title.MainMenuDestination? destination)
    {
        var root = roots["OriginalMainMenu"];
        var notice = root.GetGraphicalUiElementByName("Notice") ?? throw new InvalidDataException("Missing Main Menu notice.");
        notice.Visible = destination is not null;
        if (destination is null) return;
        BindText("OriginalMainMenu", "NoticeTitle", destination.Label);
        BindText("OriginalMainMenu", "NoticeBody", destination.Notice);
        var shade = MainMenuSprite(root, "NoticeShade");
        shade.Red = 12; shade.Green = 18; shade.Blue = 32; shade.Alpha = 235;
    }
    public void Show(string name, bool visible) { if (roots.TryGetValue(name, out var root)) root.Visible = visible; }
    public void BindTitleLayers(App.Screens.Title.OriginalTitleVisuals visuals)
    {
        foreach (string screen in new[] { "OriginalTitle", "OriginalTitleStartMenu" })
            foreach (var (name, texture) in visuals.Layers)
            {
                var sprite = roots[screen].GetGraphicalUiElementByName(name) as Gum.GueDeriving.SpriteRuntime
                    ?? throw new InvalidDataException("Missing original title layer.");
                sprite.Texture = texture;
                // Rendered layers contain premultiplied RGB; source atlas textures and fallback fonts remain straight alpha.
                sprite.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend;
                sprite.TextureAddress = Gum.Managers.TextureAddress.Custom;
                sprite.SourceRectangle = new Rectangle(0, 0, 480, 272);
            }
    }
    /// <summary>One retained layer: the measured start-menu bars or a complete flow frame, rendered by TitleFlowVisuals.</summary>
    public void BindFlowLayer(Texture2D layer)
    {
        foreach (string screen in new[] { "OriginalTitleStartMenu", "OriginalTitleFlow" })
        {
            var sprite = roots[screen].GetGraphicalUiElementByName("FlowFrame") as Gum.GueDeriving.SpriteRuntime
                ?? throw new InvalidDataException("Missing title-flow layer.");
            sprite.Texture = layer;
            sprite.BlendState = Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend;
            sprite.TextureAddress = Gum.Managers.TextureAddress.Custom;
            // Explicit: layout otherwise falls back to Gum's default 100x100 texture window.
            sprite.TextureLeft = 0; sprite.TextureTop = 0; sprite.TextureWidth = 480; sprite.TextureHeight = 272;
            sprite.WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute; sprite.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            sprite.X = 0; sprite.Y = 0; sprite.Width = 480; sprite.Height = 272;
            sprite.SourceRectangle = new Rectangle(0, 0, 480, 272);
        }
    }
    public void BindSprite(string screenName, string instanceName, ResolvedScreenSprite asset, Rectangle bounds)
    {
        var sprite = roots[screenName].GetGraphicalUiElementByName(instanceName) as Gum.GueDeriving.SpriteRuntime
            ?? throw new InvalidDataException("Expected a Gum Sprite instance.");
        asset.Region.Validate(asset.Texture.Width, asset.Texture.Height);
        var target = asset.Region.Fit(bounds);
        sprite.Texture = asset.Texture;
        sprite.TextureAddress = Gum.Managers.TextureAddress.Custom;
        sprite.SourceRectangle = asset.Region.Source;
        sprite.X = target.X; sprite.Y = target.Y; sprite.Width = target.Width; sprite.Height = target.Height;
    }
    public void BindText(string screenName, string instanceName, string text)
    {
        var element = roots[screenName].GetGraphicalUiElementByName(instanceName)
            ?? throw new InvalidDataException("Missing Gum text instance.");
        element.SetProperty("Text", text);
    }
    public void BindMenu(string name, IReadOnlyList<string> rows, int focus)
    {
        var root = roots[name];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = root.GetGraphicalUiElementByName("Row" + i) ?? throw new InvalidDataException("Missing Gum menu row.");
            row.SetProperty("Text", (i == focus ? "> " : "  ") + rows[i]);
            row.SetProperty("Red", i == focus ? 238 : 255);
            row.SetProperty("Green", i == focus ? 196 : 255);
            row.SetProperty("Blue", i == focus ? 104 : 255);
        }
    }
    public void Draw() => gum.Draw();
    public void Dispose()
    {
        foreach (var root in roots.Values) root.RemoveFromManagers();
        roots.Clear(); gum.Uninitialize(); noticeShade.Dispose();
    }
}
