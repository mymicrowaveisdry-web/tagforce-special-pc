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
    private (Keys? Key, string Expected)[] PresentationSteps => presentationSteps ??= BuildPresentationSteps(setup?.Cache is not null);
    private readonly Dictionary<int, string> presentationHashes = [];
    private byte[]? presentationPixels;
    private int? presentationTextureCount;
    private readonly List<object> presentationTrace = [];
    private PspInputFrame presentationInput;
    private int presentationInputFrame = -1;

    private static (Keys?, string)[] BuildPresentationSteps(bool deckEdit)
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
            if (MainMenuDestinations.All[row].Route == MainMenuRoute.DeckMenu) { DeckMenuRoute(Press, Hold, row, deckEdit); continue; }
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

    /// <summary>Deck Menu over the dimmed Main Menu: every row, press wrap both ways, ignored buttons, each destination's PC notice with
    /// both exits keeping focus, back to the menu row, focus reset on reopening, and hold-to-clamp both ways.</summary>
    private static void DeckMenuRoute(Action<Keys, string, string?> press, Action<Keys, int, string> hold, int menuRow, bool deckEdit)
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
    private static void DeckEditRoute(Action<Keys, string> press, int menuRow)
    {
        // The built-in deck uses most supported cards, so cursor and wrap are exercised on the 40-card Deck tab.
        press(Keys.Enter, "DeckEdit/Trunk/0/None/0/*");
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

    private PspInputFrame PresentationSmokeInput()
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
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.DeckEdit, DeckEdit: { } edit } =>
            $"DeckEdit/{edit.Tab}/{edit.Cursor}/{edit.Prompt}/{edit.PromptFocus}/{edit.Count(DeckEditTab.Deck)}/{edit.Count(DeckEditTab.Trunk)}",
        ScreenId.TitleFlow when titleFlow is { Step: TitleFlowStep.DeckMenu, DeckMenu: { } deck } flow =>
            $"DeckMenu/{(deck.NoticeOpen ? "N" : "L")}/{deck.Focus}/{flow.MenuFocus}",
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
        if (gum.RootCount != 9 || gum.AttachedRootCount != 9 || originalTitle?.Layers.Count != 11)
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
                throw new InvalidOperationException($"Presentation capture changed at frame {frames}, step {step} ({expected}).");
            presentationHashes[step] = hash;
        }
        if (frames < PresentationSteps.Length)
        {
            presentationTrace.Add(new { frame = frames, pressed = presentationInput.Pressed.ToString(), held = presentationInput.Held.ToString(),
                screen = router.CurrentScreenId, state = label, rgbaSha256 = hash, gameplayCommands = smokeCommands });
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

    private PspInputFrame RealtimeWalkInput(GameTime time)
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
