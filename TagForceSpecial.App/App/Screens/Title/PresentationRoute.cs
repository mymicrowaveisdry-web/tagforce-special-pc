using TagForceSpecial.PC.App.Input;
using Keys = TagForceSpecial.PC.App.Input.PhysicalKey;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The scripted presentation route through the title and menus: one step per drawn frame, a key pressed on that frame (or none) and
/// the state label expected after it (<see cref="AnyState"/> is not asserted). A press is followed by a settled frame without a key. Screens a
/// host does not offer yet (Deck Edit, Recipe Viewer, Card Album, Free Duel) are walked as the PC notices their rows then show.</summary>
public static class PresentationRoute
{
    public const string AnyState = "*";
    public const int Cycles = 5, HoldDraws = 90;

    /// <summary>The title, Options and submenu route; Free Duel and the submenus' screens are walked as their PC notices until a host opens them.</summary>
    public static (Keys? Key, string Expected)[] Build()
    {
        var steps = new List<(Keys?, string)> { (null, "title") };
        void Press(Keys key, string expected, string? settled = null)
        {
            steps.Add((key, expected));
            steps.Add((null, settled ?? expected));
        }
        // Mid-hold frames are not asserted: catch-up updates may shift when a repeat lands, but the settled row cannot change.
        void Hold(Keys key, int draws, string settled)
        {
            for (int i = 0; i < draws; i++) steps.Add((key, AnyState));
            steps.Add((null, settled));
        }
        // Continue with no data: no-data, Cancel load No retries, Yes returns to the title.
        Press(Keys.F1, "start:0"); Press(Keys.Down, "start:1");
        Press(Keys.Enter, "StorageCheck/0/0", "NoData/0/0");
        Press(Keys.Escape, "NoData/0/0");
        Press(Keys.Enter, "CancelLoadNoData/0/0"); Press(Keys.Escape, "CancelLoadNoData/0/0");
        Press(Keys.Right, "CancelLoadNoData/1/0"); Press(Keys.Enter, "NoData/0/0");
        Press(Keys.Enter, "CancelLoadNoData/0/0"); Press(Keys.Enter, "title");
        // New Game opens name entry directly (TF6 transfer is unsupported); back returns to the title.
        Press(Keys.F1, "start:0"); Press(Keys.Enter, "NameEntry/0/0"); Press(Keys.Escape, "title");
        // New Game: keyboard, rules Yes, confidence, level, create No enters the Main Menu unsaved.
        Press(Keys.F1, "start:0"); Press(Keys.Enter, "NameEntry/0/0");
        Press(Keys.Enter, "NameKeyboard/0/0"); Press(Keys.F1, "NameEntry/0/0");
        Press(Keys.F1, "RulesQuestion/1/0"); Press(Keys.Left, "RulesQuestion/0/0"); Press(Keys.Enter, "ConfidenceQuestion/1/0");
        Press(Keys.Enter, "DuelistLevel/0/0"); Press(Keys.Escape, "DuelistLevel/0/0");
        Press(Keys.Enter, "CreateQuestion/1/0"); Press(Keys.Enter, "MainMenu/0/0");
        // Main Menu: every row, press wrap at both ends, each destination and its focus-keeping return.
        for (int row = 1; row < TitleFlow.MainMenuRows; row++) Press(Keys.Down, $"MainMenu/0/{row}");
        Press(Keys.Down, "MainMenu/0/0"); Press(Keys.Up, "MainMenu/0/7");
        // Save Data leaves through both exits of the prompt, back on its own row.
        Press(Keys.Enter, "SystemSavePrompt/0/7"); Press(Keys.Escape, "MainMenu/0/7");
        // "Save completed." ignores confirm and closes with back.
        Press(Keys.Enter, "SystemSavePrompt/0/7"); Press(Keys.Enter, "SystemSaveDone/0/7"); Press(Keys.Enter, "SystemSaveDone/0/7"); Press(Keys.Escape, "MainMenu/0/7");
        for (int row = TitleFlow.MainMenuRows - 2; row >= 0; row--)
        {
            Press(Keys.Up, $"MainMenu/0/{row}");
            var route = MainMenuDestinations.All[row].Route;
            if (route == MainMenuRoute.Options) { Options(Press); continue; }
            if (route == MainMenuRoute.DeckMenu) { DeckMenu(Press, Hold, row); continue; }
            if (route == MainMenuRoute.DatabaseMenu) { DatabaseMenu(Press, Hold, row); continue; }
            Press(Keys.Enter, $"MainMenuUnavailable/0/{row}"); Press(Keys.Escape, $"MainMenu/0/{row}");
            Press(Keys.Enter, $"MainMenuUnavailable/0/{row}"); Press(Keys.Enter, $"MainMenu/0/{row}");
        }
        // Holding repeats and stops at the end row; only the first press may wrap.
        Hold(Keys.Down, HoldDraws, "MainMenu/0/7");
        Hold(Keys.Up, HoldDraws, "MainMenu/0/0");
        Press(Keys.Down, "MainMenu/0/1");
        // End-game question both ways.
        Press(Keys.Escape, "QuitQuestion/1/1"); Press(Keys.Enter, "MainMenu/0/1");
        Press(Keys.Escape, "QuitQuestion/1/1"); Press(Keys.Left, "QuitQuestion/0/1"); Press(Keys.Enter, "title");
        // New Game with Back answers, then Create Yes and the save prompt.
        Press(Keys.F1, "start:1"); Press(Keys.Up, "start:0"); Press(Keys.Enter, "NameEntry/0/0");
        Press(Keys.F1, "RulesQuestion/1/0"); Press(Keys.Escape, "DuelistLevel/0/0"); Press(Keys.Enter, "CreateQuestion/1/0");
        Press(Keys.Left, "CreateQuestion/0/0"); Press(Keys.Enter, "SystemSavePrompt/1/0"); Press(Keys.Left, "SystemSavePrompt/0/0");
        Press(Keys.Enter, "SystemSaveDone/0/0"); Press(Keys.Escape, "MainMenu/0/0");
        Press(Keys.Escape, "QuitQuestion/1/0"); Press(Keys.Left, "QuitQuestion/0/0"); Press(Keys.Enter, "title");
        // A saved profile changes the default focus; Continue loads through the storage check to the Main Menu.
        Press(Keys.F1, "start:1"); Press(Keys.Up, "start:0"); Press(Keys.Escape, "title"); Press(Keys.F1, "start:1");
        Press(Keys.Enter, "StorageCheck/0/0", "MainMenu/0/0");
        Press(Keys.Escape, "QuitQuestion/1/0"); Press(Keys.Left, "QuitQuestion/0/0"); Press(Keys.Enter, "title");
        return [.. steps];
    }

    /// <summary>Options from the defaults: clamping, wrap, the cancel question both ways, accept with the save question both ways, the kept value
    /// and an unchanged accept.</summary>
    private static void Options(Action<Keys, string, string?> press)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        Press(Keys.Enter, "Options/L/0/0");
        Press(Keys.Escape, "Options/Q0/0/0"); Press(Keys.Right, "Options/Q1/0/0"); Press(Keys.Enter, "Options/L/0/0");
        Press(Keys.Right, "Options/L/0/1"); Press(Keys.Right, "Options/L/0/2"); Press(Keys.Right, "Options/L/0/2");
        Press(Keys.Left, "Options/L/0/1"); Press(Keys.Left, "Options/L/0/0"); Press(Keys.Left, "Options/L/0/0");
        Press(Keys.Down, "Options/L/1/15"); Press(Keys.Left, "Options/L/1/14"); Press(Keys.Right, "Options/L/1/15"); Press(Keys.Right, "Options/L/1/15");
        for (int row = 2; row < OptionsCatalog.Rows.Count; row++)
            Press(Keys.Down, $"Options/L/{row}/{(row == OptionsCatalog.DuelistLevelRow ? 1 : OptionsCatalog.Rows[row].Default)}");
        Press(Keys.Down, "Options/L/0/0"); Press(Keys.Up, "Options/L/12/1");
        Press(Keys.Escape, "Options/Q0/12/1"); Press(Keys.Escape, "Options/Q0/12/1"); Press(Keys.Right, "Options/Q1/12/1"); Press(Keys.Enter, "Options/L/12/1");
        Press(Keys.Escape, "Options/Q0/12/1"); Press(Keys.Enter, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/0"); Press(Keys.Right, "Options/L/0/1"); Press(Keys.Enter, "OptionsSaveQuestion/0/6");
        Press(Keys.Right, "OptionsSaveQuestion/1/6"); Press(Keys.Enter, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/1"); Press(Keys.Left, "Options/L/0/0"); Press(Keys.Enter, "OptionsSaveQuestion/0/6");
        Press(Keys.Escape, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/0"); Press(Keys.Enter, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/0"); Press(Keys.Right, "Options/L/0/1"); Press(Keys.Enter, "OptionsSaveQuestion/0/6");
        Press(Keys.Enter, "SystemSavePrompt/1/6"); Press(Keys.Left, "SystemSavePrompt/0/6");
        Press(Keys.Enter, "SystemSaveDone/0/6"); Press(Keys.Escape, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/1"); Press(Keys.Left, "Options/L/0/0"); Press(Keys.Enter, "OptionsSaveQuestion/0/6"); Press(Keys.Right, "OptionsSaveQuestion/1/6");
        Press(Keys.Enter, "MainMenu/0/6");
    }

    /// <summary>Deck Menu over the dimmed Main Menu: every row, press wrap both ways, ignored buttons, each destination's PC notice with both exits
    /// keeping focus, back to the menu row, focus reset on reopening, and hold-to-clamp both ways.</summary>
    private static void DeckMenu(Action<Keys, string, string?> press, Action<Keys, int, string> hold, int menuRow)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        string Open(int focus) => $"DeckMenu/L/{focus}/{menuRow}";
        Press(Keys.Enter, Open(0));
        Press(Keys.Down, Open(1)); Press(Keys.Down, Open(2)); Press(Keys.Down, Open(0)); Press(Keys.Up, Open(2));
        foreach (var ignored in new[] { Keys.Left, Keys.Right, Keys.Tab, Keys.Space, Keys.Q, Keys.E, Keys.F1, Keys.F2 }) Press(ignored, Open(2));
        for (int focus = DeckMenuDestinations.All.Count - 1; focus >= 0; focus--)
        {
            if (focus < DeckMenuDestinations.All.Count - 1) Press(Keys.Up, Open(focus));
            Press(Keys.Enter, $"DeckMenu/N/{focus}/{menuRow}"); Press(Keys.Escape, Open(focus));
            Press(Keys.Enter, $"DeckMenu/N/{focus}/{menuRow}"); Press(Keys.Enter, Open(focus));
        }
        Press(Keys.Down, Open(1)); Press(Keys.Escape, $"MainMenu/0/{menuRow}");
        Press(Keys.Enter, Open(0));
        hold(Keys.Down, HoldDraws, Open(2));
        hold(Keys.Up, HoldDraws, Open(0));
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
    }

    /// <summary>Database submenu over the dimmed Main Menu: press wrap both ways, ignored buttons, every row's PC notice with both exits keeping
    /// focus, back to the menu row, focus reset on reopening, and hold-to-clamp both ways.</summary>
    private static void DatabaseMenu(Action<Keys, string, string?> press, Action<Keys, int, string> hold, int menuRow)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        int last = DatabaseMenuDestinations.All.Count - 1;
        string Open(int focus) => $"DatabaseMenu/L/{focus}/{menuRow}";
        Press(Keys.Enter, Open(0));
        Press(Keys.Up, Open(last)); Press(Keys.Down, Open(0));
        foreach (var ignored in new[] { Keys.Left, Keys.Right, Keys.Tab, Keys.Space, Keys.Q, Keys.E, Keys.F1, Keys.F2 }) Press(ignored, Open(0));
        for (int focus = 0; focus <= last; focus++)
        {
            if (focus > 0) Press(Keys.Down, Open(focus));
            Press(Keys.Enter, $"DatabaseMenu/N/{focus}/{menuRow}"); Press(Keys.Escape, Open(focus));
            Press(Keys.Enter, $"DatabaseMenu/N/{focus}/{menuRow}"); Press(Keys.Enter, Open(focus));
        }
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
        Press(Keys.Enter, Open(0));
        hold(Keys.Down, HoldDraws, Open(last));
        hold(Keys.Up, HoldDraws, Open(0));
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
    }

    /// <summary>The input frame for a scripted step: the key held on its own frame, through the default bindings.</summary>
    public static InputAction Held(Keys? key) => key is { } down ? KeyBindings.Default.Held(k => k == down, false) : InputAction.None;
}
