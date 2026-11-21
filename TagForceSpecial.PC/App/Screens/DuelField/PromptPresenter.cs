using Microsoft.Xna.Framework;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the current prompt, a page of legal choices with the selected one highlighted, the control hint and the last notice.</summary>
public static class PromptPresenter
{
    public const int PageSize = 4;
    /// <summary>Measured command windows: 15-pixel rows, height 19 + 15 × rows; over a hand card the window starts 47 left of the pointer
    /// and ends 32 below it, over a board object 53 left and 3 above. Window width here is the captured 106.</summary>
    public const int RowHeight = 15, WindowWidth = 106;
    private static readonly Color Panel = new(18, 27, 43, 235), Highlight = new(40, 170, 230), Edge = new(220, 230, 240);

    public static void Draw(DuelDrawList list, DuelView view, int choice, string notice)
    {
        list.Wrapped(DuelPart.Prompt, view.Prompt, 350, 146, 20, 2, DuelPalette.Gold);
        int start = choice / PageSize * PageSize;
        for (int i = start; i < Math.Min(start + PageSize, view.Choices.Count); i++)
        {
            int y = 168 + (i - start) * 17;
            if (i == choice) list.Fill(DuelPart.Prompt, new(348, y - 2, 124, 17), new(49, 65, 79));
            list.Wrapped(DuelPart.Prompt, view.Choices[i].Label, 350, y, 20, 2, i == choice ? DuelPalette.Gold : Color.White);
        }
        list.Text(DuelPart.Prompt, view.Choices.Count == 0 ? "WAIT" : $"{choice + 1}/{view.Choices.Count}  UP/DOWN", 350, 240, DuelPalette.Muted);
        list.Text(DuelPart.Prompt, "ENTER/A SELECT  TAB/Y INSPECT  ESC/B MENU", 8, 256, DuelPalette.Muted);
        list.Text(DuelPart.Prompt, notice.Length > 77 ? notice[..77] : notice, 8, 265, DuelPalette.Gold);
    }

    /// <summary>Interim drawing of the input session's open window (PC font, measured placement) and its feedback line.</summary>
    public static void DrawModal(DuelDrawList list, DuelView view, DuelFieldInput input, string notice)
    {
        switch (input.Modal)
        {
            case CardMenuModal m: Menu(list, view, m.Target, m.Items.Select(i => i.Label).ToArray(), m.Index); break;
            case GlobalMenuModal g: Menu(list, view, input.Focus.Current, DuelFieldCommands.GlobalMenu, g.Index); break;
            case PhaseModal p: Phases(list, p); break;
            case ResponseModal r: Response(list, view, r); break;
            case ListModal l: Choices(list, "CHOOSE", l.Choices.Select(c => c.Label).ToArray(), l.Index); break;
            case TargetModal t: Choices(list, "ATTACK TARGET", t.Choices.Select(c => c.Label).ToArray(), t.Index); break;
        }
        string line = input.Feedback.Kind == DuelFeedbackKind.None ? notice : input.Feedback.Text;
        if (line.Length > 0) list.Text(DuelPart.Prompt, line.Length > 77 ? line[..77] : line, 8, 263, input.Feedback.Kind == DuelFeedbackKind.None ? DuelPalette.Muted : DuelPalette.Gold);
    }

    public static Rectangle MenuWindow(Vector2 pointer, bool handCard, int rows)
    {
        int height = 19 + RowHeight * rows;
        int x = (int)pointer.X - (handCard ? 47 : 53), bottom = handCard ? (int)pointer.Y + 32 : (int)pointer.Y - 3;
        x = Math.Clamp(x, 0, 480 - WindowWidth);
        return new(x, Math.Max(0, bottom - height), WindowWidth, height);
    }

    private static void Menu(DuelDrawList list, DuelView view, FieldObject target, IReadOnlyList<string> labels, int index)
    {
        var field = DuelFieldProfileCache.Profile;
        var (centre, _) = InputFocusOverlay.Place(new(view, []), target);
        var pointer = field is null ? new Vector2(240, 136) : InputFocusOverlay.Pointer(field, target, centre);
        var window = MenuWindow(pointer, target.Own && target.Kind == FieldObjectKind.Hand, labels.Count);
        Box(list, window);
        for (int i = 0; i < labels.Count; i++)
        {
            int y = window.Y + 7 + i * RowHeight;
            if (i == index) list.Fill(DuelPart.Prompt, new(window.X + 6, y, window.Width - 12, RowHeight), Highlight, SceneLayer.PanelHighlight);
            list.Text(DuelPart.Prompt, labels[i].ToUpperInvariant(), window.X + 10, y + 4, i == index ? Color.White : Color.Black);
        }
    }

    /// <summary>Measured selector window [180,78,449,171) with six phase icons 40 apart from x 204.</summary>
    private static void Phases(DuelDrawList list, PhaseModal p)
    {
        var window = new Rectangle(180, 78, 269, 93);
        Box(list, window);
        list.Text(DuelPart.Prompt, p.Options[p.Index].Phase.ToString().ToUpperInvariant() + " PHASE", 192, 92, Color.Black);
        string[] names = ["DP", "SP", "MP1", "BP", "MP2", "EP"];
        for (int i = 0; i < 6; i++)
        {
            var cell = new Rectangle(204 + i * 40, 123, 20, 32);
            bool reachable = p.Options.Any(o => (int)o.Phase == i);
            bool selected = (int)p.Options[p.Index].Phase == i;
            list.Fill(DuelPart.Prompt, cell, selected ? Highlight : reachable ? new Color(120, 130, 145) : new Color(60, 66, 76), SceneLayer.PanelHighlight);
            list.Text(DuelPart.Prompt, names[i], cell.X, cell.Bottom + 3, Color.Black);
        }
    }

    /// <summary>Measured Chain prompt area [182,70,445,193) with YES and NO side by side around y 118–141.</summary>
    private static void Response(DuelDrawList list, DuelView view, ResponseModal r)
    {
        var window = new Rectangle(182, 70, 263, 123);
        Box(list, window);
        list.Wrapped(DuelPart.Prompt, "CHAIN ANOTHER CARD OR EFFECT?", 196, 86, 38, 2, Color.Black);
        for (int i = 0; i < 2; i++)
        {
            var button = new Rectangle(232 + i * 96, 118, 68, 23);
            list.Fill(DuelPart.Prompt, button, i == r.Index ? Highlight : new Color(40, 44, 52), SceneLayer.PanelHighlight);
            list.Text(DuelPart.Prompt, i == 0 ? "YES" : "NO", button.X + 24, button.Y + 8, Color.White);
        }
    }

    private static void Choices(DuelDrawList list, string title, IReadOnlyList<string> labels, int index)
    {
        int rows = Math.Min(labels.Count, 8), start = Math.Clamp(index - rows + 1, 0, Math.Max(0, labels.Count - rows));
        var window = new Rectangle(150, 40, 300, 22 + rows * RowHeight);
        Box(list, window);
        list.Text(DuelPart.Prompt, title + "  (PC LIST)", window.X + 8, window.Y + 6, Color.Black);
        for (int i = 0; i < rows; i++)
        {
            int y = window.Y + 18 + i * RowHeight;
            bool selected = start + i == index;
            if (selected) list.Fill(DuelPart.Prompt, new(window.X + 4, y, window.Width - 8, RowHeight), Highlight, SceneLayer.PanelHighlight);
            string label = labels[start + i];
            list.Text(DuelPart.Prompt, label.Length > 45 ? label[..45] : label, window.X + 8, y + 4, selected ? Color.White : Color.Black);
        }
    }

    private static void Box(DuelDrawList list, Rectangle window)
    {
        list.Fill(DuelPart.Prompt, window, new Color(236, 240, 246), SceneLayer.Panel);
        list.Border(DuelPart.Prompt, window, Edge, SceneLayer.Panel);
    }
}

/// <summary>The embedded field profile, loaded once, for placements that need the captured camera.</summary>
public static class DuelFieldProfileCache
{
    private static readonly Lazy<DuelFieldProfile?> Loaded = new(() =>
    {
        try { return DuelFieldProfile.LoadEmbedded(); }
        catch (InvalidDataException) { return null; }
    });
    public static DuelFieldProfile? Profile => Loaded.Value;
}
