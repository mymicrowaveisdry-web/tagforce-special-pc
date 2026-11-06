using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Options layers bound to the Gum screen: the shared animated background with the Options title, a static panel layer,
/// a text atlas of measured runs, and small piece strips. Values and focus only change which slots the Gum sprites show.</summary>
public sealed partial class TitleFlowVisuals
{
    public const int TextSlotHeight = 16, LabelOriginX = 110, LabelSlotWidth = 180, ValueOriginX = 250, ValueSlotWidth = 112, HelpOriginX = 20, HelpSlotWidth = 456;
    private const int TextAtlasWidth = 464;
    private readonly Dictionary<string, int> valueSlots = new(StringComparer.Ordinal);
    public OptionsFrameProfile OptionsProfile { get; } = OptionsFrameProfile.LoadEmbedded();
    public Texture2D OptionsAtlas => textures[OptionsProfile.AtlasKey];
    public RenderTarget2D OptionsBackground { get; private set; } = null!;
    public RenderTarget2D OptionsStatic { get; private set; } = null!;
    public RenderTarget2D OptionsText { get; private set; } = null!;
    public RenderTarget2D OptionsPieces { get; private set; } = null!;
    public static Rectangle FocusPieceSource => new(0, 0, 240, 14);
    public static Rectangle SegmentsFullSource => new(0, 20, 90, 10);
    public static Rectangle SegmentsEmptySource => new(0, 32, 90, 10);

    private void InitializeOptions(OriginalScreenAssetResolver assets)
    {
        textures.TryAdd(OptionsProfile.AtlasKey, assets.Resolve(OptionsProfile.AtlasKey).Texture);
        foreach (var dialog in OptionsProfile.Dialogs) frames.Add(dialog.Id, dialog);
        OptionsBackground = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        OptionsStatic = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        int slots = OptionsProfile.Rows.Length * 2 + OptionsProfile.Groups.Length + OptionsProfile.ValueRuns.Count;
        OptionsText = new(device, TextAtlasWidth, slots * TextSlotHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        OptionsPieces = new(device, 256, 48, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    /// <summary>Slot layout: row labels, group titles, help lines, then one slot per distinct value string.</summary>
    public Rectangle LabelSlot(int row) => new(0, row * TextSlotHeight, LabelSlotWidth, TextSlotHeight);
    public Rectangle GroupSlot(int group) => new(0, (OptionsProfile.Rows.Length + group) * TextSlotHeight, LabelSlotWidth, TextSlotHeight);
    public Rectangle HelpSlot(int row) => new(0, (OptionsProfile.Rows.Length + OptionsProfile.Groups.Length + row) * TextSlotHeight, HelpSlotWidth, TextSlotHeight);
    public Rectangle ValueSlot(int row, int value)
    {
        string key = OptionsProfile.Rows[row].Values[value].Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(0, valueSlots[key] * TextSlotHeight, ValueSlotWidth, TextSlotHeight);
    }
    public static int SlotTop(float glyphY) => (int)MathF.Floor(glyphY);

    /// <summary>Background shared with the Main Menu, with the Options title from its own measured quad.</summary>
    public void RenderOptionsBackground(int gameFrame, int entryFrame) => WithTarget(OptionsBackground, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        DrawMenuBackgroundLayers(gameFrame, entryFrame, OptionsProfile.Title);
    });

    /// <summary>Everything that never changes: panel, help bar and its icon, plus all measured text runs and the piece strips.</summary>
    public void RenderOptionsLayers()
    {
        var p = OptionsProfile;
        var projection = effect.Projection;
        try
        {
            WithTarget(OptionsStatic, () =>
            {
                device.Clear(Color.Transparent);
                foreach (var q in p.Panel.Concat(p.Help)) DrawPixelQuad(q, 0, 0);
            });
            WithTarget(OptionsPieces, () =>
            {
                device.Clear(Color.Transparent);
                effect.Projection = Matrix.CreateOrthographicOffCenter(0, OptionsPieces.Width, OptionsPieces.Height, 0, 0, 1);
                // Full alpha here; the measured pulse modulates the band's sprite each frame.
                foreach (var q in p.Focus) DrawPixelQuad(q with { Color = [q.Color[0], q.Color[1], q.Color[2], 255] }, -p.Focus[0].X0, 0);
                for (int i = 0; i < p.SegmentCount; i++)
                {
                    DrawPixelQuad(p.SegmentFull with { X0 = i * p.SegmentStep, X1 = (i + 1) * p.SegmentStep, Y0 = 20, Y1 = 30 }, 0, 0);
                    DrawPixelQuad(p.SegmentEmpty with { X0 = i * p.SegmentStep, X1 = (i + 1) * p.SegmentStep, Y0 = 32, Y1 = 42 }, 0, 0);
                }
            });
            WithTarget(OptionsText, () =>
            {
                device.Clear(Color.Transparent);
                effect.Projection = Matrix.CreateOrthographicOffCenter(0, OptionsText.Width, OptionsText.Height, 0, 0, 1);
                for (int row = 0; row < p.Rows.Length; row++)
                {
                    SlotText(p.Rows[row].Label, LabelSlot(row), LabelOriginX);
                    SlotText(p.Rows[row].Help, HelpSlot(row), HelpOriginX);
                }
                for (int g = 0; g < p.Groups.Length; g++) SlotText(p.Groups[g], GroupSlot(g), LabelOriginX);
                int slot = p.Rows.Length * 2 + p.Groups.Length;
                foreach (var row in p.Rows)
                    foreach (var value in row.Values)
                    {
                        string key = value.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (valueSlots.ContainsKey(key)) continue;
                        valueSlots[key] = slot;
                        var run = p.ValueRuns[key];
                        float y = row.Label.Y + run.DeltaY;
                        var text = new FlowText(value.Source, value.Offset, value.Start, value.Chars, run.X, y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);
                        SlotText(text, new Rectangle(0, slot * TextSlotHeight, ValueSlotWidth, TextSlotHeight), ValueOriginX);
                        slot++;
                    }
            });
        }
        finally { effect.Projection = projection; }
    }

    /// <summary>Draws a measured run into its slot: x relative to the slot's screen origin, y keeping the sub-pixel baseline.</summary>
    private void SlotText(FlowText run, Rectangle slot, int originX)
    {
        float dy = slot.Y - SlotTop(run.Y);
        float[] x = Array.ConvertAll(run.X, v => v - originX);
        DrawLine(Source(run.Source, run.Offset, run.Start, run.Chars), x[0], run.Y + dy, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha, x);
    }

    private void DrawPixelQuad(OptionsPixelQuad q, float dx, float dy)
    {
        var quad = new FlowQuad(q.X0, q.Y0, q.X1, q.Y1, q.U0, q.V0, q.U1, q.V1, q.Color, q.Additive);
        DrawQuad(OptionsAtlas, quad, dx, dy, false);
    }

    /// <summary>OriginalChoiceDialog layers: per dialog a back layer (shade and panel), measured text slots, and the button pieces.</summary>
    public const int DialogSlotsPerDialog = 4, DialogButtonWidth = 60, DialogButtonHeight = 24;
    private readonly Dictionary<string, RenderTarget2D> dialogBacks = new(StringComparer.Ordinal);
    public RenderTarget2D DialogText { get; private set; } = null!;
    public RenderTarget2D DialogPieces { get; private set; } = null!;
    public static Rectangle DialogFocusSource => new(0, 0, DialogButtonWidth, DialogButtonHeight);
    public static Rectangle DialogPlainSource => new(0, 26, DialogButtonWidth, DialogButtonHeight);
    public FlowFrame DialogFrame(string id) => frames[id];
    public RenderTarget2D DialogBack(string id) => dialogBacks[id];
    private int DialogIndex(string id) => Array.FindIndex(OptionsProfile.Dialogs, d => d.Id == id);
    /// <summary>Message lines use slots 0-1, button labels 2-3. Messages keep screen x; labels are relative to their button.</summary>
    public Rectangle DialogMessageSlot(string id, int line) => new(0, (DialogIndex(id) * DialogSlotsPerDialog + line) * TextSlotHeight, 480, TextSlotHeight);
    public Rectangle DialogLabelSlot(string id, int button) => new(0, (DialogIndex(id) * DialogSlotsPerDialog + 2 + button) * TextSlotHeight, DialogButtonWidth, TextSlotHeight);

    public void RenderDialogLayers()
    {
        var projection = effect.Projection;
        try
        {
            DialogText ??= new(device, 480, OptionsProfile.Dialogs.Length * DialogSlotsPerDialog * TextSlotHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            DialogPieces ??= new(device, 64, 52, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            var buttonAtlas = textures["title.eboot.dialog-button"];
            foreach (var dialog in OptionsProfile.Dialogs)
            {
                if (!dialogBacks.TryGetValue(dialog.Id, out var back))
                    dialogBacks[dialog.Id] = back = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
                WithTarget(back, () =>
                {
                    device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
                    device.RasterizerState = RasterizerState.CullNone;
                    foreach (var draw in dialog.Draws) DrawMeasured(draw);
                });
            }
            WithTarget(DialogPieces, () =>
            {
                device.Clear(Color.Transparent);
                effect.Projection = Matrix.CreateOrthographicOffCenter(0, DialogPieces.Width, DialogPieces.Height, 0, 0, 1);
                foreach (var q in profile.Button.Focus) DrawQuad(buttonAtlas, q, 0, 0, false);
                DrawQuad(buttonAtlas, profile.Button.Plain, 0, 26, false);
            });
            WithTarget(DialogText, () =>
            {
                device.Clear(Color.Transparent);
                effect.Projection = Matrix.CreateOrthographicOffCenter(0, DialogText.Width, DialogText.Height, 0, 0, 1);
                foreach (var dialog in OptionsProfile.Dialogs)
                {
                    for (int line = 0; line < Math.Min(2, dialog.Text.Length); line++)
                        SlotText(dialog.Text[line], DialogMessageSlot(dialog.Id, line), 0);
                    for (int b = 0; b < Math.Min(2, dialog.Buttons?.Length ?? 0); b++)
                        SlotText(dialog.Buttons![b].Label, DialogLabelSlot(dialog.Id, b), (int)dialog.Buttons[b].Rect[0]);
                }
            });
        }
        finally { effect.Projection = projection; }
    }

    public void SetOptionsFallback(IReadOnlyList<int> values, int focus)
    {
        FallbackLines.Clear();
        if (OriginalFontAvailable) return;
        var p = OptionsProfile;
        for (int row = 0; row < p.Rows.Length; row++)
        {
            var label = p.Rows[row].Label;
            string text = Source(label.Source, label.Offset, label.Start, label.Chars);
            if (p.Rows[row].Values.Length > 0)
            {
                var v = p.Rows[row].Values[values[row]];
                text += "  " + Source(v.Source, v.Offset, v.Start, v.Chars);
            }
            FallbackLines.Add((text.ToUpperInvariant(), (int)label.X[0], (int)label.Y + 2, row == focus ? Color.Navy : Color.Black));
        }
    }
}
