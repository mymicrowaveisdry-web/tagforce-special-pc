using TagForceSpecial.PC.App.Screens.Album;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private const int SmokeCycles = 5, HoldDraws = 90;
    private const string AnyState = "*";
    // Every observed Continue/New Game branch, in PC keys: Enter = confirm, Escape = back, F1 = Start.
    // Built on first use, after setup: Deck Edit is only reachable when the data root holds a card cache.
    private (Keys? Key, string Expected)[]? presentationSteps;
    private (Keys? Key, string Expected)[] PresentationSteps => presentationSteps ??= BuildPresentationSteps(setup?.Game is not null,
        options.RecipeReferenceRoot is not null || setup?.Game?.RecipeRoot is not null, setup?.Game is not null && (options.AlbumReferenceRoot is not null ||
            File.Exists(Path.Combine(setup.Storage.PathFor(CardAlbumScreen.ReferenceFolder), CardAlbumBinder.FileName))));
    private readonly Dictionary<int, string> presentationHashes = [];
    private byte[]? presentationPixels;
    private int? presentationTextureCount;
    private readonly List<object> presentationTrace = [];
    private InputFrame presentationInput;
    private int presentationInputFrame = -1;

    private static (Keys?, string)[] BuildPresentationSteps(bool deckEdit, bool recipes, bool album)
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
            if (MainMenuDestinations.All[row].Route == MainMenuRoute.Options) { OptionsRoute(Press); continue; }
            if (MainMenuDestinations.All[row].Route == MainMenuRoute.DeckMenu) { DeckMenuRoute(Press, Hold, row, deckEdit, recipes); continue; }
            if (MainMenuDestinations.All[row].Route == MainMenuRoute.FreeDuel) { FreeDuelRoute(Press, row); continue; }
            if (MainMenuDestinations.All[row].Route == MainMenuRoute.DatabaseMenu) { DatabaseMenuRoute(Press, Hold, row, album ? frames => { for (int i = 0; i < frames; i++) steps.Add((null, AnyState)); } : null); continue; }
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
        Press(Keys.F1, "start:0"); Press(Keys.Enter, "NameEntry/0/0");
        Press(Keys.F1, "RulesQuestion/1/0"); Press(Keys.Escape, "DuelistLevel/0/0"); Press(Keys.Enter, "CreateQuestion/1/0");
        Press(Keys.Left, "CreateQuestion/0/0"); Press(Keys.Enter, "SystemSavePrompt/0/0");
        Press(Keys.Enter, "SystemSaveDone/0/0"); Press(Keys.Escape, "MainMenu/0/0");
        Press(Keys.Escape, "QuitQuestion/1/0"); Press(Keys.Left, "QuitQuestion/0/0"); Press(Keys.Enter, "title");
        // A saved profile changes the default focus; Continue loads through the storage check to the Main Menu.
        Press(Keys.F1, "start:1"); Press(Keys.Up, "start:0"); Press(Keys.Escape, "title"); Press(Keys.F1, "start:1");
        Press(Keys.Enter, "StorageCheck/0/0", "MainMenu/0/0");
        Press(Keys.Escape, "QuitQuestion/1/0"); Press(Keys.Left, "QuitQuestion/0/0"); Press(Keys.Enter, "title");
        return steps.ToArray();
    }

    /// <summary>
    /// Options from the defaults: clamping, wrap, the cancel question both ways, accept with the save question both
    /// ways, the kept value and an unchanged accept.
    /// </summary>
    private static void OptionsRoute(Action<Keys, string, string?> press)
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
        Press(Keys.Enter, "SystemSavePrompt/0/6"); Press(Keys.Enter, "SystemSaveDone/0/6"); Press(Keys.Escape, "MainMenu/0/6");
        Press(Keys.Enter, "Options/L/0/1"); Press(Keys.Left, "Options/L/0/0"); Press(Keys.Enter, "OptionsSaveQuestion/0/6"); Press(Keys.Right, "OptionsSaveQuestion/1/6");
        Press(Keys.Enter, "MainMenu/0/6");
    }

    /// <summary>Free Duel settings: wrap, PC notices on Tag Duel / Match / a time limit (focus kept), the rule-change path and Back to
    /// Rules, the music list's wrap and scroll, the confirmation and its Next notice, and the Back chain to the Main Menu on Free Duel.</summary>
    private static void FreeDuelRoute(Action<Keys, string, string?> press, int menuRow)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        static string F(string step, int focus, int top = 0, bool notice = false) => $"FreeDuel/{step}/{focus}/{top}{(notice ? "/N" : "")}";
        Press(Keys.Enter, F("Format", 0)); Press(Keys.Up, F("Format", 2)); Press(Keys.Down, F("Format", 0)); Press(Keys.Down, F("Format", 1));
        Press(Keys.Enter, F("Format", 1, notice: true)); Press(Keys.Down, F("Format", 1, notice: true)); Press(Keys.Escape, F("Format", 1));
        Press(Keys.Up, F("Format", 0)); Press(Keys.Enter, F("Ending", 0)); Press(Keys.Down, F("Ending", 1));
        Press(Keys.Enter, F("Ending", 1, notice: true)); Press(Keys.Enter, F("Ending", 1)); Press(Keys.Up, F("Ending", 0));
        Press(Keys.Enter, F("Rules", 0)); Press(Keys.Down, F("Rules", 1)); Press(Keys.Enter, F("LifePoints", 0)); Press(Keys.Enter, F("TimeLimit", 0));
        Press(Keys.Down, F("TimeLimit", 1)); Press(Keys.Enter, F("TimeLimit", 1, notice: true)); Press(Keys.Escape, F("TimeLimit", 1));
        Press(Keys.Escape, F("LifePoints", 0)); Press(Keys.Escape, F("Rules", 1)); Press(Keys.Up, F("Rules", 0)); Press(Keys.Enter, F("Music", 0));
        Press(Keys.Up, F("Music", 43, 38)); Press(Keys.Down, F("Music", 0));
        for (int row = 1; row <= 6; row++) Press(Keys.Down, F("Music", row, Math.Max(0, row - 5)));
        Press(Keys.Enter, F("Confirm", 0)); Press(Keys.Escape, F("Music", 6, 1)); Press(Keys.Enter, F("Confirm", 0));
        // VERSUS: Down cycle, Up, Right/Left to and from Start Duel, notices on Start Duel and □ on the player's deck, Back to the reset first step.
        static string V(string focus, bool notice = false) => $"FreeDuelVs/{focus}{(notice ? "/N" : "")}";
        Press(Keys.Enter, V("OpponentName"));
        // Character picker: opens on Aki (5D's, No.076 on top), Down, Right = next page, E/Q tabs with each tab's own cursor, Q wraps through GX,
        // Up wraps to GX's last row, Back cancels (still Aki), then a pick of Jack Atlas.
        static string K(int series, int focus, int top) => $"FreeDuelPick/{series}/{focus}/{top}";
        Press(Keys.Enter, K(3, 1, 1)); Press(Keys.Down, K(3, 2, 1)); Press(Keys.Right, K(3, 5, 5)); Press(Keys.E, K(4, 0, 0)); Press(Keys.Q, K(3, 5, 5));
        Press(Keys.Q, K(2, 0, 0)); Press(Keys.Up, K(2, 32, 29)); Press(Keys.Escape, V("OpponentName"));
        Press(Keys.Enter, K(3, 1, 1)); Press(Keys.Down, K(3, 2, 1)); Press(Keys.Enter, V("OpponentName"));
        Press(Keys.Down, V("OpponentDeck")); Press(Keys.Down, V("PlayerDeck")); Press(Keys.Down, V("Start"));
        Press(Keys.Down, V("OpponentName")); Press(Keys.Up, V("Start")); Press(Keys.Left, V("OpponentName")); Press(Keys.Right, V("Start"));
        Press(Keys.Enter, V("Start", true)); Press(Keys.Escape, V("Start")); Press(Keys.Up, V("PlayerDeck"));
        Press(Keys.Space, V("PlayerDeck", true)); Press(Keys.Enter, V("PlayerDeck")); Press(Keys.Escape, F("Format", 0));
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
    }

    /// <summary>Database submenu over the dimmed Main Menu: press wrap both ways, ignored buttons, every row's PC notice with both exits
    /// keeping focus, back to the menu row, focus reset on reopening, and hold-to-clamp both ways.</summary>
    private static void DatabaseMenuRoute(Action<Keys, string, string?> press, Action<Keys, int, string> hold, int menuRow, Action<int>? albumWait)
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
            if (focus == 1 && albumWait is not null) { CardAlbumRoute(press, albumWait, menuRow); continue; }
            Press(Keys.Enter, $"DatabaseMenu/N/{focus}/{menuRow}"); Press(Keys.Escape, Open(focus));
            Press(Keys.Enter, $"DatabaseMenu/N/{focus}/{menuRow}"); Press(Keys.Enter, Open(focus));
        }
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
        Press(Keys.Enter, Open(0));
        hold(Keys.Down, HoldDraws, Open(last));
        hold(Keys.Up, HoldDraws, Open(0));
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
    }

    /// <summary>Card Album: pocket moves, the page edge, the bar, a forward and a backward page turn, Switch to the bar, and Back to the
    /// Database submenu with Card Album focused. Turns ignore input while they play, so the walk waits them out.</summary>
    private static void CardAlbumRoute(Action<Keys, string, string?> press, Action<int> wait, int menuRow)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        static string A(int view, string focus, bool right, int pocket, int bar = 0) => $"CardAlbum/{view}/{focus}/{(right ? "R" : "L")}/{pocket}/{bar}";
        const int TurnWait = CardAlbumSession.TurnFrames * 3 + 16; // draws run at about twice the game frame rate; margin for slow frames
        // Mid-turn poses depend on where the press lands in the game-frame clock, so only the settled view is asserted (F2 is ignored).
        void Turn(Keys key, string settled) { press(key, AnyState, AnyState); wait(TurnWait); Press(Keys.F2, settled); }
        Press(Keys.Enter, A(0, "Pocket", true, 0));
        // Card detail on the first card: Confirm opens it, Right/Left cycle the three tabs (wrapping), Back returns to the same pocket.
        foreach (var (key, tab) in new[] { (Keys.Enter, 0), (Keys.Right, 1), (Keys.Right, 2), (Keys.Right, 0), (Keys.Left, 2) }) Press(key, A(0, "Pocket", true, 0) + "/D" + tab);
        Press(Keys.Escape, A(0, "Pocket", true, 0)); Press(Keys.Right, A(0, "Pocket", true, 1)); Press(Keys.Right, A(0, "Pocket", true, 2));
        Press(Keys.Right, A(0, "Edge", true, 2)); Press(Keys.Left, A(0, "Pocket", true, 2));
        Press(Keys.Down, A(0, "Pocket", true, 5)); Press(Keys.Down, A(0, "Pocket", true, 8));
        Press(Keys.Down, A(0, "Bar", true, 8, 3)); Press(Keys.Right, A(0, "Bar", true, 8, 4)); Press(Keys.Up, A(0, "Pocket", true, 8, 4));
        Turn(Keys.E, A(1, "Pocket", true, 8, 4));
        Press(Keys.Left, A(1, "Pocket", true, 7, 4)); Press(Keys.Left, A(1, "Pocket", true, 6, 4)); Press(Keys.Left, A(1, "Pocket", false, 8, 4));
        Turn(Keys.Q, A(0, "Pocket", true, 8, 4));
        Press(Keys.Space, A(0, "Bar", true, 8, 0));
        // Page Jump: opens on "*", wraps up to the last row, closes with Back.
        Press(Keys.Tab, A(0, "Bar", true, 8, 0) + "/J00"); Press(Keys.Up, A(0, "Bar", true, 8, 0) + "/J40");
        Press(Keys.Right, A(0, "Bar", true, 8, 0) + "/J41"); Press(Keys.Escape, A(0, "Bar", true, 8, 0));
        // Confirming "A" closes the overlay, plays the arrival and lands on the first "A" card (captured: view 1, left page, pocket 2).
        Press(Keys.Tab, A(0, "Bar", true, 8, 0) + "/J00"); Press(Keys.Right, A(0, "Bar", true, 8, 0) + "/J01");
        Turn(Keys.Enter, A(1, "Pocket", false, 2, 0));
        Press(Keys.Enter, A(1, "Pocket", false, 2, 0) + "/D0"); Press(Keys.Escape, A(1, "Pocket", false, 2, 0));
        Press(Keys.Escape, $"DatabaseMenu/L/1/{menuRow}");
    }

    /// <summary>Deck Menu over the dimmed Main Menu: every row, press wrap both ways, ignored buttons, each destination's PC notice with
    /// both exits keeping focus, back to the menu row, focus reset on reopening, and hold-to-clamp both ways.</summary>
    private static void DeckMenuRoute(Action<Keys, string, string?> press, Action<Keys, int, string> hold, int menuRow, bool deckEdit, bool recipes)
    {
        void Press(Keys key, string expected) => press(key, expected, null);
        string Open(int focus) => $"DeckMenu/L/{focus}/{menuRow}";
        Press(Keys.Enter, Open(0));
        Press(Keys.Down, Open(1)); Press(Keys.Down, Open(2)); Press(Keys.Down, Open(0)); Press(Keys.Up, Open(2));
        foreach (var ignored in new[] { Keys.Left, Keys.Right, Keys.Tab, Keys.Space, Keys.Q, Keys.E, Keys.F1, Keys.F2 }) Press(ignored, Open(2));
        for (int focus = DeckMenuDestinations.All.Count - 1; focus >= 0; focus--)
        {
            if (focus < DeckMenuDestinations.All.Count - 1) Press(Keys.Up, Open(focus));
            if (focus == 0 && deckEdit) { DeckEditRoute(Press, menuRow); continue; }
            if (focus == 2 && recipes) { RecipeRoute(Press, menuRow); continue; }
            Press(Keys.Enter, $"DeckMenu/N/{focus}/{menuRow}"); Press(Keys.Escape, Open(focus));
            Press(Keys.Enter, $"DeckMenu/N/{focus}/{menuRow}"); Press(Keys.Enter, Open(focus));
        }
        Press(Keys.Down, Open(1)); Press(Keys.Escape, $"MainMenu/0/{menuRow}");
        Press(Keys.Enter, Open(0));
        hold(Keys.Down, HoldDraws, Open(2));
        hold(Keys.Up, HoldDraws, Open(0));
        Press(Keys.Escape, $"MainMenu/0/{menuRow}");
    }

    /// <summary>Deck Edit core screen from the Deck Menu: cursor and wrap, every tab, a move both ways, a cancelled move, the exit question
    /// both ways and the save question answered NO, back on the submenu. Counts depend on the built-in deck, so states are matched by prefix.</summary>
    private static void RecipeRoute(Action<Keys, string> press, int menuRow)
    {
        press(Keys.Enter, "Recipe/User/0/None/Id/False");
        press(Keys.Down, "Recipe/User/1/None/Id/False");
        press(Keys.Enter, "Recipe/User/1/Manage/Id/False");
        press(Keys.Escape, "Recipe/User/1/None/Id/False");
        press(Keys.Tab, "Recipe/User/1/Sort/Id/False");
        press(Keys.Down, "Recipe/User/1/Sort/Id/False");
        press(Keys.Enter, "Recipe/User/0/None/Name/False");
        press(Keys.Tab, "Recipe/User/0/Sort/Name/False");
        press(Keys.Right, "Recipe/User/0/Sort/Name/False");
        press(Keys.Down, "Recipe/User/0/Sort/Name/False");
        press(Keys.Enter, "Recipe/User/0/None/Name/True");
        press(Keys.Space, "Recipe/Character/0/None/Name/True");
        press(Keys.Enter, "Recipe/Character/0/None/Name/True");
        press(Keys.Down, "Recipe/Character/1/None/Name/True");
        press(Keys.Down, "Recipe/Character/2/None/Name/True");
        press(Keys.Up, "Recipe/Character/1/None/Name/True");
        press(Keys.Space, "Recipe/User/0/None/Name/True");
        press(Keys.Escape, $"DeckMenu/L/2/{menuRow}");
    }

    /// <summary>Deck Edit menu state for smoke labels: "-" closed, else top popup:focus, R on Run, N notice, C count notice.</summary>
    private static string MenuState(DeckEditMenus menus) => menus.RunCount is not null ? "C" : menus.Top is not { } top ? "-"
        : $"{top.Id}:{top.Focus}{(top.OnRun ? "R" : "")}{(menus.Notice is null ? "" : "N")}";

    /// <summary>Deck Edit Menu walk: Search > Sort preview and Order, back out restoring the sort; Narrow Search > Card Frame toggle, Run,
    /// count notice OK; Reset Narrow Search Results; a PC notice (Card Details) closed with back. Leaves the list as it started.</summary>
    private static void DeckEditMenuRoute(Action<Keys, string> press)
    {
        string M(string state) => $"DeckEdit/Trunk/0/None/0/{state}/*";
        press(Keys.Tab, M("main:0"));
        press(Keys.Down, M("main:1")); press(Keys.Down, M("main:2")); press(Keys.Down, M("main:3"));
        press(Keys.Enter, M("search:0")); press(Keys.Enter, M("sort:0")); press(Keys.Down, M("sort:1"));
        press(Keys.Right, M("sort-order:0")); press(Keys.Escape, M("sort:1")); press(Keys.Escape, M("search:0"));
        press(Keys.Down, M("search:1")); press(Keys.Enter, M("narrow:0")); press(Keys.Enter, M("card-frame:0"));
        press(Keys.Down, M("card-frame:4")); press(Keys.Down, M("card-frame:8")); press(Keys.Right, M("card-frame:9"));
        press(Keys.Enter, M("card-frame:9")); press(Keys.Down, M("card-frame:9R")); press(Keys.Enter, M("C"));
        press(Keys.Enter, M("-"));
        press(Keys.Tab, M("main:0"));
        for (int row = 1; row <= 7; row++) press(Keys.Down, M($"main:{row}"));
        press(Keys.Enter, M("-"));
        press(Keys.Tab, M("main:0")); press(Keys.Enter, M("main:0N")); press(Keys.Escape, M("main:0")); press(Keys.Escape, M("-"));
    }

    private static void DeckEditRoute(Action<Keys, string> press, int menuRow)
    {
        // The built-in deck uses most supported cards, so cursor and wrap are exercised on the 40-card Deck tab.
        press(Keys.Enter, "DeckEdit/Trunk/0/None/0/*");
        DeckEditMenuRoute(press);
        press(Keys.Space, "DeckEdit/Deck/0/None/0/*");
        press(Keys.Down, "DeckEdit/Deck/1/None/0/*"); press(Keys.Down, "DeckEdit/Deck/2/None/0/*"); press(Keys.Up, "DeckEdit/Deck/1/None/0/*");
        press(Keys.Up, "DeckEdit/Deck/0/None/0/*"); press(Keys.Up, "DeckEdit/Deck/*"); press(Keys.Down, "DeckEdit/Deck/0/None/0/*");
        for (int i = 0; i < 8; i++) press(Keys.Down, $"DeckEdit/Deck/{i + 1}/None/0/*");
        press(Keys.Space, "DeckEdit/Extra/0/None/0/*");
        press(Keys.Space, "DeckEdit/Side/0/None/0/*"); press(Keys.Space, "DeckEdit/Trunk/0/None/0/*");
        press(Keys.Enter, "DeckEdit/Trunk/0/Move/0/*"); press(Keys.Escape, "DeckEdit/Trunk/0/None/0/*");
        press(Keys.Enter, "DeckEdit/Trunk/0/Move/0/*"); press(Keys.Enter, "DeckEdit/Trunk/0/None/0/*");
        press(Keys.Space, "DeckEdit/Deck/0/None/0/*"); press(Keys.Enter, "DeckEdit/Deck/0/Move/0/*"); press(Keys.Enter, "DeckEdit/Deck/0/None/0/*");
        press(Keys.Escape, "DeckEdit/Deck/0/ExitQuestion/0/*"); press(Keys.Right, "DeckEdit/Deck/0/ExitQuestion/1/*"); press(Keys.Enter, "DeckEdit/Deck/0/None/0/*");
        press(Keys.Escape, "DeckEdit/Deck/0/ExitQuestion/0/*"); press(Keys.Enter, "DeckEdit/Deck/0/SaveQuestion/0/*");
        press(Keys.Right, "DeckEdit/Deck/0/SaveQuestion/1/*"); press(Keys.Enter, $"DeckMenu/L/0/{menuRow}");
    }

    private InputFrame PresentationSmokeInput()
    {
        var key = PresentationSteps[frames % PresentationSteps.Length].Key;
        // A new cycle starts from the empty-profile route again; the smoke never writes a profile.
        if (frames % PresentationSteps.Length == 0) smokeProfile = false;
        var polled = input.Poll(key is { } value ? new KeyboardState(value) : new KeyboardState(), new GamePadState(), true);
        // Fixed-step catch-up may update more than once before drawing. Retain the actual edge in the draw-frame trace.
        presentationInput = presentationInputFrame == frames
            ? new(presentationInput.Pressed | polled.Pressed, polled.Held) : polled;
        presentationInputFrame = frames;
        return polled;
    }

    private bool HoldSmokeStorage() => PresentationSteps[frames % PresentationSteps.Length].Expected.StartsWith("StorageCheck", StringComparison.Ordinal);

    private string PresentationLabel() => activeScreen switch
    {
        ScreenId.Title => "title",
        ScreenId.TitleStartMenu => "start:" + titleState.Choice,
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.Options, Options: { } session } =>
            $"Options/{(session.CancelQuestion ? "Q" + session.CancelFocus : "L")}/{session.Focus}/{session.Values[session.Focus]}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.RecipeViewer, Recipes: { } recipes } =>
            $"Recipe/{recipes.Kind}/{recipes.Cursor}/{recipes.Modal}/{recipes.Sort}/{recipes.Inverse}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.DeckEdit, DeckEdit: { } edit } =>
            $"DeckEdit/{edit.Tab}/{edit.Cursor}/{edit.Prompt}/{edit.PromptFocus}/{MenuState(edit.Menus)}/{edit.Count(DeckEditTab.Deck)}/{edit.Count(DeckEditTab.Trunk)}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.FreeDuelSettings, FreeDuel: { Picker: { } pick } } =>
            $"FreeDuelPick/{pick.Series}/{pick.Focus}/{pick.Top}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.FreeDuelSettings, FreeDuel: { Versus: { } vs } } =>
            $"FreeDuelVs/{vs.Focus}{(vs.NoticeOpen ? "/N" : "")}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.FreeDuelSettings, FreeDuel: { Settings: var fd } } =>
            $"FreeDuel/{fd.Step}/{fd.Focus}/{fd.Top}{(fd.NoticeOpen ? "/N" : "")}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.CardAlbum, CardAlbum: { } album } =>
            $"CardAlbum/{album.View}/{album.Focus}/{(album.RightPage ? "R" : "L")}/{album.Pocket}/{album.BarButton}{(album.JumpOpen ? $"/J{album.JumpRow}{album.JumpColumn}" : "")}{(album.Detail is { Closing: false } d ? $"/D{(int)d.Tab}" : "")}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.DeckMenu or TitleFlowStep.DatabaseMenu, Submenu: { } sub } flow =>
            $"{flow.Step}/{(sub.NoticeOpen ? "N" : "L")}/{sub.Focus}/{flow.MenuFocus}",
        ScreenId.TitleFlow when titleFlow is { } flow => $"{flow.Step}/{flow.ButtonFocus}/{flow.MenuFocus}",
        _ => activeScreen.ToString()
    };

    private void CheckPresentationSmokeFrame()
    {
        int step = frames % PresentationSteps.Length;
        string expected = PresentationSteps[step].Expected, label = PresentationLabel();
        bool asserted = expected != AnyState;
        bool matches = expected.EndsWith('*') ? label.StartsWith(expected[..^1], StringComparison.Ordinal) : label == expected;
        if (asserted && !matches)
            throw new InvalidOperationException($"Presentation route/focus mismatch at frame {frames}, step {step}: expected {expected}, got {label}.");
        if (duel is not null || smokeCommands != 0 || router!.ModalDepth != 0)
            throw new InvalidOperationException("Presentation navigation changed gameplay or modal ownership.");
        if (gum.RootCount != 13 || gum.AttachedRootCount != 13 || originalTitle?.Layers.Count != 11)
            throw new InvalidOperationException("Presentation root/layer count changed.");
        presentationTextureCount ??= gum.CachedTextureCount;
        if (gum.CachedTextureCount != presentationTextureCount)
            throw new InvalidOperationException("Gum texture cache grew during navigation.");
        presentationPixels ??= new byte[480 * 272 * 4];
        canvas.GetData(presentationPixels);
        string hash = Convert.ToHexStringLower(SHA256.HashData(presentationPixels));
        if (asserted)
        {
            if (presentationHashes.TryGetValue(step, out var previous) && previous != hash)
            {
                // With a capture root, keep the differing frame beside the first one for diagnosis.
                if (options.FlowCaptureRoot is { } diagnostics) CaptureFlowFrame(diagnostics, $"{expected}-changed-{frames}");
                throw new InvalidOperationException($"Presentation capture changed at frame {frames}, step {step} ({expected}).");
            }
            presentationHashes[step] = hash;
        }
        if (frames < PresentationSteps.Length)
        {
            presentationTrace.Add(new { frame = frames, pressed = presentationInput.Pressed.ToString(), held = presentationInput.Held.ToString(),
                screen = router.CurrentScreenId, state = label, rgbaSha256 = hash, gameplayCommands = smokeCommands,
                recipe = titleFlow?.Recipes?.Focused is { } r ? new { kind = r.Kind.ToString(), slot = r.Slot, name = r.Name,
                    main = r.Main.Length, extra = r.Extra.Length, side = r.Side.Length, firstMain = r.Main.FirstOrDefault(),
                    recordSha256 = r.RecordSha256, verification = "unverified" } : null,
                returnedRecipe = lastRecipeSelection });
            if (asserted && options.FlowCaptureRoot is { } root) CaptureFlowFrame(root, label);
        }
        if (frames + 1 == SmokeCycles * PresentationSteps.Length)
        {
            if (options.MainMenuEntryFrame is null && presentationHashes.Values.Distinct().Count() < 28)
                throw new InvalidOperationException("Presentation did not render the distinct screens and focus states.");
            if (options.PresentationTracePath is { } path)
            {
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                System.Text.Json.JsonSerializer.Serialize(output, new { cycles = SmokeCycles, stepsPerCycle = PresentationSteps.Length, frames = frames + 1,
                    input = presentationTrace, sourceTextures = screenAssets.TextureCount, composedLayers = originalTitle!.Layers.Count,
                    originalFont = flowVisuals?.OriginalFontAvailable == true, originalSaveImport = "not-implemented;native-profile-simulated",
                    titleTiming = "frozen-measured-frames" }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            Console.WriteLine($"PRESENTATION_SMOKE cycles={SmokeCycles} steps={PresentationSteps.Length} roots={gum.RootCount} source-textures={screenAssets.TextureCount} layers={originalTitle!.Layers.Count} distinct-frames={presentationHashes.Values.Distinct().Count()} font={flowVisuals?.OriginalFontAvailable == true} commands=0");
            Exit();
        }
    }

    // Normal-speed route in a real window with synthetic keys, covering fades, the entry sequence and the live
    // background. Answers No to data creation, so it writes no profile.
    private static readonly (double At, Keys Key)[] RealtimeKeys =
    [
        (1, Keys.F1), (2, Keys.Enter), (4, Keys.F1), (5, Keys.Escape), (6, Keys.Enter), (7, Keys.Enter),
        // Options: open it, change a value, accept, answer No to the save question, and come back to the replayed menu.
        (10.5, Keys.Up), (10.8, Keys.Up), (11.2, Keys.Enter), (13.4, Keys.Right), (13.8, Keys.Enter), (15.0, Keys.Right), (15.4, Keys.Enter),
        // Deck Menu: open over the dimmed menu, move, open a notice and close it, then close the submenu.
        (18.4, Keys.Up), (18.7, Keys.Up), (19.0, Keys.Enter), (20.5, Keys.Down), (21.0, Keys.Enter), (22.0, Keys.Escape), (22.6, Keys.Escape),
        (23.4, Keys.Escape), (24.2, Keys.Left), (24.6, Keys.Enter)
    ];
    private const double RealtimeEnd = 28, RealtimeCaptureStep = .1;
    private readonly List<object> realtimeTrace = [];
    private double nextRealtimeCapture = 1.5;

    private InputFrame RealtimeWalkInput(GameTime time)
    {
        double now = time.TotalGameTime.TotalSeconds;
        foreach (var (at, key) in RealtimeKeys)
            if (now >= at && now < at + .05) return input.Poll(new KeyboardState(key), new GamePadState(), true);
        return input.Poll(new KeyboardState(), new GamePadState(), true);
    }

    private void CheckRealtimeWalkFrame(GameTime time)
    {
        double now = time.TotalGameTime.TotalSeconds;
        if (now < nextRealtimeCapture) return;
        nextRealtimeCapture = now + RealtimeCaptureStep;
        string label = PresentationLabel();
        string name = $"{realtimeTrace.Count:000}-{now:00.00}-{label.Replace('/', '-').Replace(':', '-')}.png";
        CaptureFlowFrame(options.RealtimeWalkRoot!, name[..^4]);
        realtimeTrace.Add(new { seconds = now, state = label, fade = screenFade.Phase.ToString(), fadeAlpha = screenFade.Alpha, menuFrame = titleFlow?.MenuFrame });
        if (now < RealtimeEnd) return;
        File.WriteAllText(Path.Combine(options.RealtimeWalkRoot!, "trace.json"), System.Text.Json.JsonSerializer.Serialize(realtimeTrace, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"REALTIME_WALK captures={realtimeTrace.Count} final={label}");
        Exit();
    }

    private void CaptureFlowFrame(string root, string label)
    {
        for (string? at = root; at is not null; at = Path.GetDirectoryName(at))
            if (Directory.Exists(Path.Combine(at, ".git")) || File.Exists(Path.Combine(at, ".git")))
                throw new InvalidDataException("Derived original frames must stay outside Git worktrees.");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, label.Replace('/', '-').Replace(':', '-') + ".png");
        if (File.Exists(file)) return;
        using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
        canvas.SaveAsPng(output, 480, 272);
    }
}
