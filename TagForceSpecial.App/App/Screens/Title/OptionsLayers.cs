using System.Globalization;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The Options screen's layers as host-neutral commands: the Main Menu background with the Options title, a static panel and help
/// layer, a text atlas of measured runs in fixed slots, the focus band and slider pieces, and per choice dialog a back layer, text slots and
/// button pieces. Values and focus only change which slots the screen's sprites show. Port of the MonoGame host's Options layers.</summary>
public sealed class OptionsLayers
{
    public const int TextSlotHeight = 16, LabelOriginX = 110, LabelSlotWidth = 180, ValueOriginX = 250, ValueSlotWidth = 112, HelpOriginX = 20,
        HelpSlotWidth = 456, TextAtlasWidth = 464, PiecesWidth = 256, PiecesHeight = 48;
    public const int DialogSlotsPerDialog = 4, DialogButtonWidth = 60, DialogButtonHeight = 24, DialogTextWidth = 480, DialogPiecesWidth = 64,
        DialogPiecesHeight = 52;
    public static readonly (int X, int Y, int Width, int Height) FocusPieceSource = (0, 0, 240, 14), SegmentsFullSource = (0, 20, 90, 10),
        SegmentsEmptySource = (0, 32, 90, 10), DialogFocusSource = (0, 0, DialogButtonWidth, DialogButtonHeight),
        DialogPlainSource = (0, 26, DialogButtonWidth, DialogButtonHeight);
    private readonly MainMenuLayers menu;
    private readonly FlowProfile flow;
    private readonly Dictionary<string, int> valueSlots = new(StringComparer.Ordinal);

    public OptionsLayers(OptionsFrameProfile profile, FlowProfile flow, MainMenuLayers menu)
    {
        Profile = profile; this.flow = flow; this.menu = menu;
        // Slot layout: row labels, group titles, help lines, then one slot per distinct value string in row order.
        int slot = profile.Rows.Length * 2 + profile.Groups.Length;
        foreach (var row in profile.Rows)
            foreach (var value in row.Values)
                if (valueSlots.TryAdd(Key(value), slot)) slot++;
        TextAtlasHeight = slot * TextSlotHeight;
    }

    public OptionsFrameProfile Profile { get; }
    public int TextAtlasHeight { get; }
    public int DialogTextHeight => Profile.Dialogs.Length * DialogSlotsPerDialog * TextSlotHeight;

    /// <summary>Imported and EBOOT textures the layers sample.</summary>
    public IEnumerable<string> TextureKeys => menu.TextureKeys.Append(Profile.AtlasKey).Append(TitleFlowFrames.ButtonKey)
        .Concat(Profile.Dialogs.SelectMany(d => d.Draws).Select(d => d.AssetKey).OfType<string>()).Distinct(StringComparer.Ordinal);

    public (int X, int Y, int Width, int Height) LabelSlot(int row) => (0, row * TextSlotHeight, LabelSlotWidth, TextSlotHeight);
    public (int X, int Y, int Width, int Height) GroupSlot(int group) => (0, (Profile.Rows.Length + group) * TextSlotHeight, LabelSlotWidth, TextSlotHeight);
    public (int X, int Y, int Width, int Height) HelpSlot(int row) =>
        (0, (Profile.Rows.Length + Profile.Groups.Length + row) * TextSlotHeight, HelpSlotWidth, TextSlotHeight);
    public (int X, int Y, int Width, int Height) ValueSlot(int row, int value) =>
        (0, valueSlots[Key(Profile.Rows[row].Values[value])] * TextSlotHeight, ValueSlotWidth, TextSlotHeight);
    public static (int X, int Y, int Width, int Height) ArrowSource(OptionsPixelQuad q) => (q.Source[0], q.Source[1], q.Source[2] - q.Source[0], q.Source[3] - q.Source[1]);

    private int DialogIndex(string id) => Array.FindIndex(Profile.Dialogs, d => d.Id == id);
    public FlowFrame Dialog(string id) => Profile.Dialogs[DialogIndex(id)];
    /// <summary>Message lines use slots 0-1, button labels 2-3. Messages keep screen x; labels are relative to their button.</summary>
    public (int X, int Y, int Width, int Height) DialogMessageSlot(string id, int line) =>
        (0, (DialogIndex(id) * DialogSlotsPerDialog + line) * TextSlotHeight, DialogTextWidth, TextSlotHeight);
    public (int X, int Y, int Width, int Height) DialogLabelSlot(string id, int button) =>
        (0, (DialogIndex(id) * DialogSlotsPerDialog + 2 + button) * TextSlotHeight, DialogButtonWidth, TextSlotHeight);

    /// <summary>Background shared with the Main Menu, with the Options title from its own measured quad.</summary>
    public IReadOnlyList<FlowCommand> Background(int gameFrame, int entryFrame) => menu.Background(gameFrame, entryFrame, Profile.Title);

    /// <summary>Everything on the list that never changes: panel, help bar and its icon.</summary>
    public IReadOnlyList<FlowCommand> Static() => [.. Profile.Panel.Concat(Profile.Help).Select(q => Pixel(q, 0, 0))];

    /// <summary>The focus band (full alpha; the measured pulse modulates its sprite) and the full and empty slider strips.</summary>
    public IReadOnlyList<FlowCommand> Pieces()
    {
        var p = Profile;
        var commands = new List<FlowCommand>();
        foreach (var q in p.Focus) commands.Add(Pixel(q with { Color = [q.Color[0], q.Color[1], q.Color[2], 255] }, -p.Focus[0].X0, 0));
        for (int i = 0; i < p.SegmentCount; i++)
        {
            commands.Add(Pixel(p.SegmentFull with { X0 = i * p.SegmentStep, X1 = (i + 1) * p.SegmentStep, Y0 = 20, Y1 = 30 }, 0, 0));
            commands.Add(Pixel(p.SegmentEmpty with { X0 = i * p.SegmentStep, X1 = (i + 1) * p.SegmentStep, Y0 = 32, Y1 = 42 }, 0, 0));
        }
        return commands;
    }

    /// <summary>The text atlas (<see cref="TextAtlasWidth"/> × <see cref="TextAtlasHeight"/>).</summary>
    public IReadOnlyList<FlowCommand> Text(FlowTextSources text, PgfGlyphAtlas font, string fontKey)
    {
        var p = Profile;
        var commands = new List<FlowCommand>();
        for (int row = 0; row < p.Rows.Length; row++)
        {
            Slot(commands, text, font, fontKey, p.Rows[row].Label, LabelSlot(row).Y, LabelOriginX);
            Slot(commands, text, font, fontKey, p.Rows[row].Help, HelpSlot(row).Y, HelpOriginX);
        }
        for (int g = 0; g < p.Groups.Length; g++) Slot(commands, text, font, fontKey, p.Groups[g], GroupSlot(g).Y, LabelOriginX);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in p.Rows)
            foreach (var value in row.Values)
            {
                string key = Key(value);
                if (!done.Add(key)) continue;
                var run = p.ValueRuns[key];
                var line = new FlowText(value.Source, value.Offset, value.Start, value.Chars, run.X, row.Label.Y + run.DeltaY, run.ScaleX, run.ScaleY,
                    run.Color, run.ShadowAlpha);
                Slot(commands, text, font, fontKey, line, valueSlots[key] * TextSlotHeight, ValueOriginX);
            }
        return commands;
    }

    /// <summary>A dialog's back layer: shade and panel as measured (with their stencil masks).</summary>
    public IReadOnlyList<FlowCommand> DialogBack(string id) => [.. Dialog(id).Draws.Select(FlowDrawCompiler.Compile).OfType<FlowCommand>()];

    /// <summary>Button pieces (<see cref="DialogPiecesWidth"/> × <see cref="DialogPiecesHeight"/>): focused at the top, plain below.</summary>
    public IReadOnlyList<FlowCommand> DialogPieces() =>
        [.. flow.Button.Focus.Select(q => FlowDrawCompiler.Quad(TitleFlowFrames.ButtonKey, q, 0, 0, false)),
            FlowDrawCompiler.Quad(TitleFlowFrames.ButtonKey, flow.Button.Plain, 0, DialogPlainSource.Y, false)];

    /// <summary>Dialog text slots (<see cref="DialogTextWidth"/> × <see cref="DialogTextHeight"/>).</summary>
    public IReadOnlyList<FlowCommand> DialogText(FlowTextSources text, PgfGlyphAtlas font, string fontKey)
    {
        var commands = new List<FlowCommand>();
        foreach (var dialog in Profile.Dialogs)
        {
            for (int line = 0; line < Math.Min(2, dialog.Text.Length); line++)
                Slot(commands, text, font, fontKey, dialog.Text[line], DialogMessageSlot(dialog.Id, line).Y, 0);
            for (int b = 0; b < Math.Min(2, dialog.Buttons?.Length ?? 0); b++)
                Slot(commands, text, font, fontKey, dialog.Buttons![b].Label, DialogLabelSlot(dialog.Id, b).Y, (int)dialog.Buttons[b].Rect[0]);
        }
        return commands;
    }

    /// <summary>Top of a run's slot sprite on screen: the slot keeps the run's sub-pixel baseline below this whole pixel.</summary>
    public static int SlotTop(float glyphY) => (int)MathF.Floor(glyphY);

    /// <summary>A measured run in its slot: x relative to the slot's screen origin, y keeping the sub-pixel baseline.</summary>
    private static void Slot(List<FlowCommand> commands, FlowTextSources text, PgfGlyphAtlas font, string fontKey, FlowText run, int slotY, int originX)
    {
        float dy = slotY - SlotTop(run.Y);
        float[] x = Array.ConvertAll(run.X, v => v - originX);
        commands.AddRange(FlowTextLayout.Line(font, fontKey, text.Read(run), x[0], run.Y + dy, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha, x));
    }

    private FlowCommand Pixel(OptionsPixelQuad q, float dx, float dy) =>
        FlowDrawCompiler.Quad(Profile.AtlasKey, new FlowQuad(q.X0, q.Y0, q.X1, q.Y1, q.U0, q.V0, q.U1, q.V1, q.Color, q.Additive), dx, dy, false);

    private static string Key(FlowTextSource value) => value.Offset.ToString(CultureInfo.InvariantCulture);
}
