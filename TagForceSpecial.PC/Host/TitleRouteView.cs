using Godot;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.Host;

/// <summary>Original placeholder artwork, controls and hit targets for the title route.</summary>
internal sealed partial class TitleRouteView : Control
{
    private static readonly Color Background = new("#101b29"), Panel = new("#203148"), Light = new("#edf4fb"),
        Muted = new("#b4c6d8"), Accent = new("#ffd176"), Ink = new("#131e2a"), Shade = new(0, 0, 0, 0.74f);
    private static readonly Rect2 Bounds = new(0, 0, 480, 272);
    private static readonly Rect2 NoticeButton = new(140, 133, 200, 28);
    private static readonly Rect2 OptionsAccept = new(353, 231, 102, 23);
    private TitleRoute route;
    private readonly KeyBindings bindings;
    private readonly record struct Prompt(string Heading, string Message, string[] Buttons);

    public TitleRouteView(TitleRoute route, KeyBindings bindings)
    {
        this.route = route;
        this.bindings = bindings;
    }

    public string? SaveError { get; set; }

    public void ShowRoute(TitleRoute value)
    {
        route = value;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(Bounds, Background);
        DrawRect(new Rect2(0, 0, 6, 272), Accent);
        if (route.Screen != TitleScreenKind.Flow) DrawStart();
        else DrawFlow(route.Flow);
        if (route.BlackAlpha > 0) DrawRect(Bounds, new Color(0, 0, 0, route.BlackAlpha / 255f));
    }

    private void DrawStart()
    {
        Text("Tag Force", 25, 76, 37, Light);
        Text("Special", 25, 114, 37, Accent);
        DrawRect(new Rect2(27, 143, 166, 1), Muted);
        if (route.Screen == TitleScreenKind.Title)
            Button(StartRow(0), "Start", true);
        else
        {
            Button(StartRow(0), "New Game", route.StartChoice == 0);
            Button(StartRow(1), "Continue", route.StartChoice == 1);
        }
        Footer(route.Screen == TitleScreenKind.Title ? $"{KeyName(InputAction.Confirm)} / Start" : NavigationHint);
    }

    private void DrawFlow(TitleFlow flow)
    {
        switch (flow.Step)
        {
            case TitleFlowStep.MainMenu:
            case TitleFlowStep.MainMenuUnavailable:
            case TitleFlowStep.DeckMenu:
            case TitleFlowStep.DatabaseMenu:
                DrawMenus(flow);
                break;
            case TitleFlowStep.Options when flow.Options is { } session:
                DrawOptions(session);
                break;
            default:
                DrawPrompt(PromptFor(flow), flow.ButtonFocus);
                break;
        }
    }

    private void DrawMenus(TitleFlow flow)
    {
        var submenu = flow.Submenu;
        Heading(submenu is null ? "Main Menu" : MainMenuDestinations.All[flow.MenuFocus].Label);
        for (int row = 0; row < MainMenuDestinations.All.Count; row++)
            Button(MenuRow(row), MainMenuDestinations.All[row].Label, row == flow.MenuFocus && flow.MenuReady);
        if (submenu is not null)
        {
            var items = Submenus.Destinations(submenu.Kind);
            for (int row = 0; row < items.Count; row++)
                Button(SubmenuRow(row), items[row].Label, row == submenu.Focus && submenu.Ready);
            if (submenu.NoticeOpen) DrawNotice(submenu.Focused.Label);
        }
        else if (flow.Step == TitleFlowStep.MainMenuUnavailable) DrawNotice(flow.FocusedDestination.Label);
        Footer(NavigationHint);
    }

    private void DrawNotice(string title)
    {
        DrawRect(Bounds, Shade);
        DrawRect(new Rect2(123, 58, 234, 122), Background);
        Text(title, 140, 88, 18, Light);
        Text("This feature is unavailable here.", 140, 113, 12, Muted);
        Button(NoticeButton, "Close", true);
    }

    private void DrawOptions(OptionsSession session)
    {
        Heading("Options");
        for (int row = 0; row < session.Values.Count; row++)
        {
            var option = OptionsCatalog.Rows[row];
            string label = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(option.Id.Replace('-', ' '));
            string value = row == OptionsCatalog.DuelistLevelRow
                ? (session.Values[row] + 1).ToString()
                : option.Kind == OptionKind.Slider ? $"{session.Values[row]}/{option.Count - 1}" : (session.Values[row] + 1).ToString();
            var rect = OptionRow(row);
            Button(rect, label, session.Focus == row, fontSize: 12);
            Text($"‹ {value} ›", rect.End.X - 53, rect.Position.Y + 15, 12, session.Focus == row ? Ink : Light);
        }
        if (session.CancelQuestion)
        {
            DrawRect(Bounds, Shade);
            DrawPrompt(new("Discard changes?", "Your previous options will be restored.", ["Discard", "Keep editing"]), session.CancelFocus);
        }
        else
        {
            Button(OptionsAccept, "Accept", false, fontSize: 12);
            Footer($"↑ ↓ Select     ← → Adjust     {KeyName(InputAction.Confirm)} Accept     Esc Back");
        }
    }

    private static Prompt PromptFor(TitleFlow flow) => flow.Step switch
    {
        TitleFlowStep.StorageCheck => new("Checking for a save", "Looking for a local player profile.", []),
        TitleFlowStep.NoData => new("No save found", "Create a profile by starting a new game.", ["OK"]),
        TitleFlowStep.CancelLoadNoData => new("Stop looking for a save?", "You can return to the title or retry.", ["Return to title", "Retry"]),
        TitleFlowStep.NameEntry => new("Player name", flow.Name, ["Edit name", "Next"]),
        TitleFlowStep.NameKeyboard => new("Type your player name", flow.Name + "│", ["Done"]),
        TitleFlowStep.RulesQuestion => new("Do you know the rules?", "Your answer sets the suggested duelist level.", ["Yes", "No"]),
        TitleFlowStep.ConfidenceQuestion => new("Ready for a challenge?", "Choose the starting level that suits you.", ["Yes", "No"]),
        TitleFlowStep.DuelistLevel => new("Duelist level", LevelName(flow.Level), ["Next"]),
        TitleFlowStep.CreateQuestion => new("Create a save?", "You can also continue without saving.", ["Create save", "Skip"]),
        TitleFlowStep.SystemSavePrompt => new("Save player data?", $"Profile: {flow.Name}", ["Save", "Cancel"]),
        TitleFlowStep.SystemSaveDone => new("Save complete", "Your player data has been saved.", ["Back"]),
        TitleFlowStep.OptionsSaveQuestion => new("Save your options?", "Save these settings to your player profile.", ["Save options", "Keep for this session"]),
        TitleFlowStep.QuitQuestion => new("Return to the title?", "Any unsaved changes will be lost.", ["Return to title", "Keep playing"]),
        _ => new("Loading", "", [])
    };

    private static string LevelName(DuelistLevelChoice level) => level switch
    {
        DuelistLevelChoice.FirstTimer => "Beginner",
        DuelistLevelChoice.BeenThere => "Experienced",
        _ => "Advanced"
    };

    private void DrawPrompt(Prompt prompt, int focus)
    {
        DrawRect(new Rect2(20, 42, 440, 186), Background);
        Text(prompt.Heading, 32, 82, 25, Light);
        Text(prompt.Message, 32, 115, 15, Muted);
        for (int i = 0; i < prompt.Buttons.Length; i++)
            Button(PromptButton(i), prompt.Buttons[i], i == focus && route.Flow.Step != TitleFlowStep.NameEntry);
        string hint = route.Flow.Step switch
        {
            TitleFlowStep.NameEntry => $"{KeyName(InputAction.Confirm)} Edit     {KeyName(InputAction.Start)} Next     Esc Back",
            TitleFlowStep.NameKeyboard => $"Type up to {TitleFlow.NameLimit} characters     {KeyName(InputAction.Confirm)} Done",
            TitleFlowStep.SystemSaveDone => "Esc / Back to close",
            TitleFlowStep.StorageCheck => "",
            _ => NavigationHint
        };
        if (SaveError is not null && route.Flow.Step == TitleFlowStep.SystemSavePrompt)
            Text(SaveError, 32, 203, 13, Accent);
        Footer(hint);
    }

    public InputFrame ApplyPointer(InputFrame input)
    {
        if (!input.Pointer.Any || route.Fade.Active) return input;
        var flow = route.Flow;
        if (route.Screen == TitleScreenKind.Title)
            return Click(input, StartRow(0), InputAction.Start);
        if (route.Screen == TitleScreenKind.StartMenu)
            return Point(input, StartRow, 2, route.StartChoice, route.PointStart);
        if (flow.PendingStep is not null) return input;
        if (flow.Step == TitleFlowStep.MainMenu && flow.MenuReady)
            return Point(input, MenuRow, TitleFlow.MainMenuRows, flow.MenuFocus, flow.PointMenu);
        if (flow.Step == TitleFlowStep.MainMenuUnavailable || flow.Submenu?.NoticeOpen == true)
            return Click(input, NoticeButton, InputAction.Confirm);
        if (flow.Submenu is { Ready: true } sub)
            return Point(input, SubmenuRow, sub.Rows, sub.Focus, sub.SetFocus);
        if (flow.Step == TitleFlowStep.Options && flow.MenuReady && flow.Options is { } session)
            return PointOptions(input, session);
        return PointPrompt(input, flow);
    }

    private static InputFrame PointPrompt(InputFrame input, TitleFlow flow) => flow.Step switch
    {
        TitleFlowStep.NameEntry => Click(Click(input, PromptButton(0), InputAction.Confirm), PromptButton(1), InputAction.Start),
        TitleFlowStep.NameKeyboard => Click(input, PromptButton(0), InputAction.Confirm),
        TitleFlowStep.SystemSaveDone => Click(input, PromptButton(0), InputAction.Back),
        _ => Point(input, PromptButton, flow.ButtonCount, flow.ButtonFocus, flow.PointButton, horizontal: true)
    };

    private static InputFrame PointOptions(InputFrame input, OptionsSession session)
    {
        if (session.CancelQuestion)
            return Point(input, PromptButton, 2, session.CancelFocus, session.PointCancel, horizontal: true);
        var hit = Hit(input, OptionRow, session.Values.Count);
        if (hit is int row && (input.Pointer.Moved || input.Pointer.LeftPressed)) session.PointRow(row);
        if (input.Pointer.LeftPressed && hit is int target && input.Pointer.Position is { } point)
        {
            float right = OptionRow(target).End.X;
            if (point.X >= right - 62) return input.With(point.X >= right - 20 ? InputAction.Right : InputAction.Left);
        }
        return PointerTargets.Apply(Click(input, OptionsAccept, InputAction.Confirm), null, session.Focus, session.PointRow);
    }

    private static InputFrame Click(InputFrame input, Rect2 rect, InputAction action)
    {
        if (input.Pointer.LeftPressed && Hit(input, _ => rect, 1) is not null) return input.With(action);
        return input.Pointer.RightPressed ? input.With(InputAction.Back) : input;
    }

    private static InputFrame Point(InputFrame input, Func<int, Rect2> rect, int count, int focus, Action<int> setFocus, bool horizontal = false) =>
        PointerTargets.Apply(input, Hit(input, rect, count), focus, setFocus,
            horizontal ? (InputAction.Left, InputAction.Right) : null);

    private static int? Hit(InputFrame input, Func<int, Rect2> rect, int count)
    {
        if (input.Pointer.Position is not { } p) return null;
        for (int i = 0; i < count; i++)
            if (rect(i).HasPoint(new Vector2(p.X, p.Y))) return i;
        return null;
    }

    private static Rect2 StartRow(int row) => new(249, 83 + 42 * row, 197, 34);
    private static Rect2 MenuRow(int row) => new(25, 51 + 23 * row, 194, 20);
    private static Rect2 SubmenuRow(int row) => new(239, 51 + 23 * row, 215, 20);
    private static Rect2 OptionRow(int row) => new(25 + row / 7 * 220, 51 + row % 7 * 25, 210, 21);
    private static Rect2 PromptButton(int button) => new(32 + button * 208, 141, 198, 33);
    private void Heading(string text) => Text(text, 25, 35, 25, Light);
    private void Footer(string text) => Text(text, 25, 255, 12, Muted);
    private string NavigationHint => $"{KeyName(InputAction.Confirm)} Confirm     Esc Back";
    private string KeyName(InputAction action) => KeyBindings.KeyName(bindings[action, 0] ?? bindings[action, 1]);

    private void Button(Rect2 rect, string label, bool focused, int fontSize = 14)
    {
        DrawRect(rect, focused ? Accent : Panel);
        Text(label, rect.Position.X + 8, rect.GetCenter().Y + fontSize / 3f, fontSize, focused ? Ink : Light);
    }

    private void Text(string text, float x, float baseline, int fontSize, Color color) =>
        DrawString(ThemeDB.FallbackFont, new Vector2(x, baseline), text, fontSize: fontSize, modulate: color);
}
