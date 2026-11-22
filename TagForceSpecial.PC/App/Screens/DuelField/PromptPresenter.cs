using TagForceSpecial.Core.Views;
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
    public static void DrawModal(DuelDrawList list, DuelView view, DuelFieldInput input, string notice) =>
        DrawModal(list, view, input, notice, originalField: true);

    internal static void DrawModal(DuelDrawList list, DuelView view, DuelFieldInput input, string notice, bool originalField)
    {
        var profile = originalField ? DuelFieldProfileCache.Profile : null;
        switch (input.Modal)
        {
            case CardMenuModal m: OriginalMenu(list, profile, view, m.Target, m.Items, m.Index); break;
            case GlobalMenuModal g: OriginalMenu(list, profile, view, input.Focus.Current, DuelFieldCommands.GlobalVerbs.Select((v, i) => new MenuItem(DuelFieldCommands.GlobalMenu[i], new UnsupportedCommand(""), v)).ToArray(), g.Index); break;
            case PhaseModal p: if (profile is { } field) OriginalPhases(list, field, p, view); else Phases(list, p); break;
            case ResponseModal r: if (profile is not null && view.ChainTop is { } top) list.Add(new DuelChainPrompt(DuelPart.Prompt, top.Name, ResponseCards(view), r.Index)); else Response(list, view, r); break;
            case ListModal l:
                if (profile is not null && DuelBoardDecision.OnField(l)) break;
                if (profile is not null) DuelDecisionPanel.DrawOriginal(list, l);
                else DuelDecisionPanel.Draw(list, l);
                break;
            case TargetModal t when profile is null: DuelDecisionPanel.Draw(list, new(t.Choices, t.Index, Cancelable: true)); break;
            case TargetModal: break; // drawn on the field (AttackTargetOverlay)
            case TributeConfirmModal c when profile is not null: list.Add(TributeSelection.Prompt(c)); break;
            case TributeConfirmModal c: DuelDecisionPanel.Draw(list, new(c.Choices, 0, Cancelable: true)); break;
            case TributeModal t when profile is null: DuelDecisionPanel.Draw(list, new(t.Choices, 0, Cancelable: true)); break;
            case TributeModal: break; // the field cursor on the monster (measured: no window)
        }
        if (profile is not null) DuelNoticeLine.DrawOriginal(list, profile, input.Feedback);
        else DuelNoticeLine.Draw(list, view, input.Feedback, notice);
    }

    /// <summary>The menu ○ just closed by choosing Summon, still drawn as it was (measured: it stays 26 vblanks).</summary>
    internal static void DrawClosingMenu(DuelDrawList list, DuelView view, CardMenuModal menu) =>
        OriginalMenu(list, DuelFieldProfileCache.Profile, view, menu.Target, menu.Items, menu.Index);

    public static Rectangle MenuWindow(Vector2 pointer, bool handCard, int rows)
    {
        int height = 19 + RowHeight * rows;
        int x = (int)pointer.X - (handCard ? 47 : 53), bottom = handCard ? (int)pointer.Y + 32 : (int)pointer.Y - 3;
        x = Math.Clamp(x, 0, 480 - WindowWidth);
        return new(x, Math.Max(0, bottom - height), WindowWidth, height);
    }

    /// <summary>The original command window (<see cref="DuelMenuWindow"/>), anchored to the focused object's projected centre.</summary>
    private static void OriginalMenu(DuelDrawList list, DuelFieldProfile? field, DuelView view, FieldObject target, IReadOnlyList<MenuItem> items, int index)
    {
        if (field is null) { Menu(list, view, target, items.Select(i => i.Label).ToArray(), index); return; }
        var (centre, _) = InputFocusOverlay.Place(new(view, []), target);
        var screen = DuelLayoutProfile.Project(field, centre);
        list.Add(new DuelFieldMenu(DuelPart.Prompt, items, index, (int)screen.X, (int)screen.Y));
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

    /// <summary>The faces of the viewer's eligible response cards: a set-zone response shows that zone's card, a card response its card.</summary>
    public static IReadOnlyList<uint> ResponseCards(DuelView view)
    {
        var cards = DuelFieldRows.Build(view).SelectMany(r => r).Where(o => o.Own).Select(o => o.Card).OfType<CardView>().ToArray();
        var ids = new List<uint>();
        foreach (var choice in DuelFieldCommands.Responses(view).Responses)
        {
            var card = choice.FocusInstanceId is { } instance ? cards.FirstOrDefault(c => c.InstanceId == instance)
                : choice.Zone is { } zone && zone < view.Self.SpellTraps.Count ? view.Self.SpellTraps[zone] : null;
            if (card?.CardId is { } id && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Phase names in duel/basic_jj.ehp, Draw Phase to End Phase.</summary>
    public static readonly int[] PhaseNameOffsets = [401118, 401140, 401168, 401190, 401216, 401242];
    /// <summary>"Select @2phase@0 to enter." in duelsys/cardinfo_jpn.ehp.</summary>
    public const int SelectPhaseOffset = 1179180;
    private static readonly Dictionary<int, (int, int)> HeaderPalette = new() { [0] = (0xffffff, 128), [2] = (0x7f9900, 64) };
    private static readonly Dictionary<int, (int, int)> TitlePalette = new() { [0] = (0x000000, 64) };

    /// <summary>Original selector (Main Phase 1 capture): depth clear, window with header tab, phase name, header line, then per phase its
    /// icon (bright cell when current or reachable, else dim) and the arrow before it (bright out of the current phase), with the cyan
    /// highlight drawn just before the selected icon. Icons are 40 pixels apart.</summary>
    private static void OriginalPhases(DuelDrawList list, DuelFieldProfile field, PhaseModal p, DuelView view)
    {
        list.FieldPiece(DuelPart.Prompt, field, "select.clear");
        list.FieldPiece(DuelPart.Prompt, field, "select.window");
        int selected = (int)p.Options[p.Index].Phase, current = (int)view.Phase;
        list.Add(new DuelRichText(DuelPart.Prompt, Assets.Presentation.DuelFieldResources.BasicKey, PhaseNameOffsets[selected], null,
            189.8f, 103.42f, 0.6f, 0.54f, TitlePalette));
        list.Add(new DuelRichText(DuelPart.Prompt, Assets.Presentation.CardAlbumResources.CardInfoKey, SelectPhaseOffset, null,
            181.8f, 79.42f, 0.6f, 0.54f, HeaderPalette));
        var size = field.Textures[field.Piece("select.icon")[0].Texture!].Size;
        float dimV = 32f / size[1];
        for (int k = 0; k < 6; k++)
        {
            bool bright = k == current || p.Options.Any(o => (int)o.Phase == k);
            if (k == selected) list.FieldPiece(DuelPart.Prompt, field, "select.highlight", new(Offset: new((k - 2) * 40, 0)));
            list.Field(DuelPart.Prompt, "select.icon", 0, new(Offset: new(k * 40, 0), UvOffset: new(k * 20f / size[0], bright ? -dimV : 0)));
            if (k >= 1)
                list.Field(DuelPart.Prompt, "select.arrow", 0, new(Offset: new((k - 1) * 40, 0), UvOffset: new(0, k - 1 == current ? -dimV : 0)));
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
