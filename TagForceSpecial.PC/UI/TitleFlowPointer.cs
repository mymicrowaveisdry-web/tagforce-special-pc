using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.UI;

/// <summary>Mouse targets for the title flow, from the layouts the screens are drawn with: Gum instances (menus, Options rows, save and
/// cancel dialogs) and measured profiles (dialog buttons, Deck Edit, Recipe Viewer). Hit areas follow the drawn pieces; tab areas and
/// the Options accept strip are PC choices. Output is the same actions the keys give, so the screens keep one input path.</summary>
public sealed class TitleFlowPointer(GumPresentation gum)
{
    public InputFrame Translate(TitleFlow flow, TitleFlowVisuals visuals, InputFrame input, string? dialogFrame)
    {
        if (!input.Pointer.Any || flow.PendingStep is not null) return input;
        var at = input.Pointer.Position;
        switch (flow.Step)
        {
            case TitleFlowStep.MainMenu:
                return PointerTargets.Apply(input, gum.HitIndex("OriginalMainMenu", "Bar", TitleFlow.MainMenuRows, at), flow.MenuFocus, flow.PointMenu);
            case TitleFlowStep.MainMenuUnavailable:
                return PointerTargets.Clicks(input);
            case TitleFlowStep.DeckMenu when flow.DeckMenu is { } menu:
                if (menu.NoticeOpen) return PointerTargets.Clicks(input);
                return PointerTargets.Apply(input, menu.Ready ? gum.HitIndex("OriginalDeckMenu", "Bar", menu.Rows, at) : null, menu.Focus, menu.SetFocus);
            case TitleFlowStep.Options when flow.Options is { } options:
                return Options(input, options);
            case TitleFlowStep.OptionsSaveQuestion:
                return PointerTargets.Apply(input, gum.HitIndex("OriginalOptions", "SaveDialog.Button", 2, at), flow.ButtonFocus, flow.PointButton,
                    (InputAction.Left, InputAction.Right));
            case TitleFlowStep.RecipeViewer when flow.Recipes is { } recipes:
                return Recipes(input, recipes);
            case TitleFlowStep.DeckEdit when flow.DeckEdit is { } edit:
                return DeckEdit(input, edit, visuals);
            case TitleFlowStep.NameKeyboard:
                return input.Pointer.LeftPressed ? input.With(InputAction.Confirm) : input; // typing screen: a click finishes the name
        }
        if (flow.ButtonCount == 2 && dialogFrame is not null && visuals.DialogFrame(dialogFrame).Buttons is { Length: 2 } buttons)
            return PointerTargets.Apply(input, PointerTargets.Find(at, buttons.Select(b => b.Rect).ToArray()), flow.ButtonFocus, flow.PointButton,
                (InputAction.Left, InputAction.Right));
        return PointerTargets.Clicks(input);
    }

    /// <summary>Hovering a row focuses it; its arrows step the value; the help strip below the rows accepts (the original confirm);
    /// right click asks to cancel. A plain click on a row only focuses it, since confirm leaves the screen.</summary>
    private InputFrame Options(InputFrame input, OptionsSession options)
    {
        var p = input.Pointer;
        var at = p.Position;
        if (options.CancelQuestion)
            return PointerTargets.Apply(input, gum.HitIndex("OriginalOptions", "CancelDialog.Button", 2, at), options.CancelFocus, options.PointCancel,
                (InputAction.Left, InputAction.Right));
        int rows = OptionsCatalog.Rows.Count;
        if (gum.HitIndex("OriginalOptions", "Row", rows, at) is int row && (p.Moved || p.LeftPressed)) options.PointRow(row);
        var pressed = InputAction.None;
        if (p.LeftPressed)
        {
            if (gum.HitIndex("OriginalOptions", "Row", rows, at, ".ArrowLeft") is not null) pressed |= InputAction.Left;
            else if (gum.HitIndex("OriginalOptions", "Row", rows, at, ".ArrowRight") is not null) pressed |= InputAction.Right;
            else if (gum.Hit("OriginalOptions", "Help", at)) pressed |= InputAction.Confirm;
        }
        if (p.RightPressed) pressed |= InputAction.Back;
        if (p.Wheel != 0) pressed |= p.Wheel > 0 ? InputAction.Up : InputAction.Down;
        return input.With(pressed);
    }

    private static InputFrame Recipes(InputFrame input, RecipeViewerSession recipes)
    {
        if (recipes.Modal == RecipeViewerModal.Notice) return PointerTargets.Clicks(input);
        var l = recipes.Catalog.Layout;
        if (recipes.Modal != RecipeViewerModal.None)
        {
            // Modal rows have no separate hit layout; clicks confirm the focused row, the wheel moves it.
            return PointerTargets.Apply(input, null, recipes.ModalFocus, recipes.PointModal);
        }
        var row = Bounds(l.Row);
        var rects = Enumerable.Range(0, l.VisibleRows).Select(i => Offset(row, i * l.RowPitch)).ToArray();
        return PointerTargets.Apply(input, PointerTargets.Find(input.Pointer.Position, rects), recipes.Cursor - recipes.Scroll, recipes.PointRow);
    }

    private InputFrame DeckEdit(InputFrame input, DeckEditSession edit, TitleFlowVisuals visuals)
    {
        var at = input.Pointer.Position;
        var p = visuals.DeckEditProfile;
        switch (edit.Prompt)
        {
            case DeckEditPrompt.Move: return PointerTargets.Clicks(input);
            case DeckEditPrompt.SaveQuestion:
                return PointerTargets.Apply(input, gum.HitIndex("OriginalOptions", "SaveDialog.Button", 2, at), edit.PromptFocus, edit.PointPrompt,
                    (InputAction.Left, InputAction.Right));
            case DeckEditPrompt.ExitQuestion:
                var d = p.ExitDialog;
                var focus = Bounds(d.Focus);
                var buttons = d.Buttons.Select(b => Offset(focus, 0, b.X - d.Buttons[0].X)).ToArray();
                return PointerTargets.Apply(input, PointerTargets.Find(at, buttons), edit.PromptFocus, edit.PointPrompt, (InputAction.Left, InputAction.Right));
        }
        if (edit.Menus.Open) return Menus(input, edit, visuals.DeckEditMenuProfile);
        if (input.Pointer.LeftPressed && Tab(at, p) is { } tab && tab != edit.Tab) { edit.PointTab(tab); return input; }
        var row = Bounds(p.Row.Values);
        var rects = Enumerable.Range(0, p.VisibleRows).Select(i => Offset(row, i * p.RowPitch)).ToArray();
        return PointerTargets.Apply(input, PointerTargets.Find(at, rects), edit.Cursor - edit.Scroll, edit.PointRow);
    }

    /// <summary>Tab hit areas (PC choice): from each label to the next label (the last ends past its count), around the label line.</summary>
    private static DeckEditTab? Tab(CanvasPoint? at, DeckEditProfile p)
    {
        if (at is not { } point) return null;
        var t = p.TabText;
        DeckEditTab[] onScreen = [DeckEditTab.Deck, DeckEditTab.Extra, DeckEditTab.Side, DeckEditTab.Trunk];
        float top = Math.Min(t.ActiveY, t.InactiveY) - 3, bottom = Math.Max(t.ActiveY, t.InactiveY) + 15;
        for (int i = 0; i < 4; i++)
        {
            float x0 = t.Labels[i].X - 6, x1 = i < 3 ? t.Labels[i + 1].X - 6 : t.CountRight[i] + 10;
            if (PointerTargets.Inside(point, x0, top, x1, bottom)) return onScreen[i];
        }
        return null;
    }

    private static InputFrame Menus(InputFrame input, DeckEditSession edit, DeckEditMenuProfile profile)
    {
        var menus = edit.Menus;
        if (menus.Notice is not null || menus.RunCount is not null || menus.Top is not { } top) return PointerTargets.Clicks(input);
        var popup = profile[top.Id];
        var at = input.Pointer.Position;
        if (!popup.IsGrid)
        {
            var b = popup.Band!;
            var rects = popup.Rows!.Select(r => new[] { r.Text.X + b.DX0, r.Text.Y + b.DY0, b.X1, r.Text.Y + b.DY0 + b.Height }).ToArray();
            return PointerTargets.Apply(input, PointerTargets.Find(at, rects), top.Focus, i => menus.Point(i, false, edit));
        }
        var o = popup.CellBand!;
        var cells = popup.Cells!.Select(c => new[] { c.Box[0] + o[0], c.Box[1] + o[1], c.Box[0] + o[2], c.Box[1] + o[3] }).ToList();
        int run = cells.Count;
        if (popup.Run is { } button) cells.Add(Bounds([button.Button]));
        int focus = top.OnRun ? run : top.Focus;
        return PointerTargets.Apply(input, PointerTargets.Find(at, cells), focus,
            i => menus.Point(i == run ? top.Focus : i, i == run, edit), (InputAction.Up, InputAction.Down));
    }

    private static float[] Bounds(IEnumerable<FlowDraw> draws)
    {
        var points = draws.SelectMany(d => d.Points).ToArray();
        return [points.Min(q => q[0]), points.Min(q => q[1]), points.Max(q => q[0]), points.Max(q => q[1])];
    }

    private static float[] Offset(float[] r, float dy, float dx = 0) => [r[0] + dx, r[1] + dy, r[2] + dx, r[3] + dy];
}
