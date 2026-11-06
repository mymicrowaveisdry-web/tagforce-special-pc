using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public enum TitleFlowStep
{
    StorageCheck, NoData, CancelLoadNoData,
    NameEntry, NameKeyboard,
    RulesQuestion, ConfidenceQuestion, DuelistLevel, CreateQuestion, SystemSavePrompt, SystemSaveDone,
    MainMenu, MainMenuUnavailable, QuitQuestion, ExitToTitle,
    Options, OptionsSaveQuestion,
    DeckMenu, DeckEdit
}

public enum DuelistLevelChoice { FirstTimer, BeenThere, BringItOn }

/// <summary>Where a firmware-style save prompt was opened from; it decides the default answer and where the flow returns.</summary>
public enum SaveSource { NewGame, MainMenu, Options }

/// <summary>Application services the flow needs; the flow itself holds no storage, GPU or gameplay state.</summary>
public interface ITitleFlowHost
{
    bool ProfilePresent { get; }
    bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options);
    void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options);
    /// <summary>Main Menu Save Data and the Options save question: rewrites the profile with the session's name, level and options.</summary>
    void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options);
    /// <summary>Opens Deck Edit over the PC's selected deck, or null when no card catalog is available (the row then shows its notice).</summary>
    DeckEditSession? OpenDeckEdit() => null;
    /// <summary>Saves the session's deck as a new native revision (Deck Edit's YES to "Save all contents acquired so far?").</summary>
    void SaveDeckEdit(DeckEditSession session) { }
}

/// <summary>Observed Continue/New Game routes (config/reference/title-flow.json) and Main Menu/Options/Deck Menu routes (main-menu.json,
/// options.json, deck-menu.json). PC confirm is PSP Circle in the original and PSP Cross here; PC back is the reverse.
/// Tag Force 6 data transfer is deliberately unsupported: New Game opens name entry directly.</summary>
public sealed class TitleFlow(ITitleFlowHost host, string defaultName, bool instantTransitions = false, DeckMenuProfile? deckMenuProfile = null)
{
    private readonly DeckMenuProfile deckMenuProfile = deckMenuProfile ?? DeckMenuProfile.LoadEmbedded();
    private TimeSpan? sinceSubmenuClosed;
    private DeckEditSession? pendingDeckEdit;
    private bool deckMenuSettled;
    public const int NameLimit = 8;
    /// <summary>Measured from the first panel appearing to black. The original shows two panels here; the PC shows only the first.</summary>
    public static readonly TimeSpan StorageCheckDuration = TimeSpan.FromSeconds(2.37);
    public const int MainMenuRows = 8;
    public static readonly TimeSpan MenuRepeatDelay = TimeSpan.FromSeconds(RepeatDelayVblanks / VblankHz);
    public static readonly TimeSpan MenuRepeatInterval = TimeSpan.FromSeconds(RepeatIntervalVblanks / VblankHz);
    private const double VblankHz = 59.94, RepeatDelayVblanks = 22, RepeatIntervalVblanks = 6.4;
    private readonly MenuHoldRepeat menuRepeat = new(MenuRepeatDelay, MenuRepeatInterval);
    private int heldDirection;
    private SaveSource saveSource;
    private bool pendingKeepsFocus, pendingReplaysEntry;
    private TimeSpan elapsed;
    private TimeSpan menuClock = instantTransitions ? ReadyClock : TimeSpan.Zero;
    private static TimeSpan ReadyClock => TimeSpan.FromSeconds(MainMenuEntry.ReadyFrame / MainMenuEntry.GameFramesPerSecond + 1);
    public TitleFlowStep Step { get; private set; } = TitleFlowStep.MainMenu;
    /// <summary>Set when a step should change behind a fade to black. The flow ignores input until the host commits it.</summary>
    public TitleFlowStep? PendingStep { get; private set; }
    /// <summary>Game frames the host waits before fading: dialogs hold briefly after confirming, screen exits do not.</summary>
    public int PendingLeadFrames { get; private set; }
    /// <summary>Game frames since this Main Menu or Options visit began; drives its entry sequence, background and input lock.</summary>
    public int MenuFrame => (int)Math.Floor(menuClock.TotalSeconds * MainMenuEntry.GameFramesPerSecond);
    public bool MenuReady => MenuFrame >= MainMenuEntry.ReadyFrame;
    /// <summary>0 = first button (YES/OK), 1 = second (NO).</summary>
    public int ButtonFocus { get; private set; }
    public int MenuFocus { get; private set; }
    public string Name { get; private set; } = Clip(defaultName);
    public DuelistLevelChoice Level { get; private set; }
    /// <summary>Options values held for this session: accepted changes stay here until a save writes them, as observed.</summary>
    public PlayerOptions SessionOptions { get; private set; } = PlayerOptions.Defaults();
    public OptionsSession? Options { get; private set; }
    /// <summary>The submenu drawn over the dimmed Main Menu while <see cref="TitleFlowStep.DeckMenu"/> is active.</summary>
    public DeckMenuSession? DeckMenu { get; private set; }
    public DeckMenuProfile DeckMenuProfile => deckMenuProfile;
    /// <summary>The Deck Edit core screen while <see cref="TitleFlowStep.DeckEdit"/> is active.</summary>
    public DeckEditSession? DeckEdit { get; private set; }
    /// <summary>Measured: the Main Menu is dimmed, without focus, for as long as the submenu is drawn, including its close frames.</summary>
    public bool MainMenuDimmed => Step == TitleFlowStep.DeckMenu;
    /// <summary>Game frames since the Main Menu focus reappeared after the submenu closed; measured, its pulse restarts there.
    /// Null otherwise, when the pulse follows the menu's own background clock.</summary>
    public int? MenuFocusRestartFrame => sinceSubmenuClosed is { } t ? (int)Math.Floor(t.TotalSeconds * MainMenuEntry.GameFramesPerSecond) : null;
    public int ButtonCount => Step switch
    {
        TitleFlowStep.NoData or TitleFlowStep.DuelistLevel or TitleFlowStep.SystemSaveDone or TitleFlowStep.MainMenuUnavailable => 1,
        TitleFlowStep.CancelLoadNoData or TitleFlowStep.RulesQuestion or TitleFlowStep.ConfidenceQuestion or TitleFlowStep.CreateQuestion or
            TitleFlowStep.SystemSavePrompt or TitleFlowStep.QuitQuestion or TitleFlowStep.OptionsSaveQuestion => 2,
        _ => 0
    };
    public bool Editing => Step == TitleFlowStep.NameKeyboard;
    public MainMenuDestination FocusedDestination => MainMenuDestinations.All[MenuFocus];

    public void BeginContinue() { MenuFocus = 0; Enter(TitleFlowStep.StorageCheck); }
    /// <summary>A new game starts from the default name, level and options, whatever an earlier Continue loaded.</summary>
    public void BeginNewGame()
    {
        MenuFocus = 0; Name = Clip(defaultName); Level = DuelistLevelChoice.FirstTimer; SessionOptions = PlayerOptions.Defaults();
        Enter(TitleFlowStep.NameEntry);
    }
    public void BeginMainMenu() => Enter(TitleFlowStep.MainMenu);

    public void CommitPending()
    {
        if (PendingStep is not { } next) return;
        PendingStep = null;
        Enter(next, pendingKeepsFocus, pendingReplaysEntry);
    }

    private void EnterFaded(TitleFlowStep next, bool keepMenuFocus = false, bool replayEntry = false, int lead = ScreenFade.DialogLeadFrames)
    {
        if (instantTransitions) { Enter(next, keepMenuFocus, replayEntry); return; }
        PendingStep = next; pendingKeepsFocus = keepMenuFocus; pendingReplaysEntry = replayEntry; PendingLeadFrames = lead;
    }

    /// <summary>Leaving Options or its dialogs always replays the menu's entry sequence on the Options row, as observed.</summary>
    private void ReturnFromOptions(int lead) => EnterFaded(TitleFlowStep.MainMenu, keepMenuFocus: true, replayEntry: true, lead: lead);

    public void Update(TimeSpan delta)
    {
        menuClock += delta;
        if (sinceSubmenuClosed is { } since) sinceSubmenuClosed = since + delta;
        if (Step == TitleFlowStep.DeckMenu)
        {
            DeckMenu!.Update(delta);
            if (DeckMenu.Closed) ReturnFromDeckMenu();
            return;
        }
        if (Step == TitleFlowStep.MainMenu && heldDirection != 0)
        {
            // Measured: repeats walk toward the end row and stop there; only a fresh press wraps.
            for (int due = menuRepeat.Advance(delta); due > 0; due--)
                MenuFocus = Math.Clamp(MenuFocus + heldDirection, 0, MainMenuRows - 1);
            return;
        }
        if (Step != TitleFlowStep.StorageCheck) return;
        elapsed += delta;
        if (elapsed < StorageCheckDuration) return;
        // The original check cannot be cancelled and always resolves to the save state.
        if (host.ProfilePresent && host.TryLoadProfile(out string name, out var level, out var options))
        {
            Name = Clip(name); Level = level; SessionOptions = options;
            Enter(TitleFlowStep.MainMenu);
        }
        else Enter(TitleFlowStep.NoData);
    }

    public void Handle(PspInputFrame input)
    {
        if (PendingStep is not null) return;
        if (Step == TitleFlowStep.NameKeyboard)
        {
            if (input.IsPressed(PspAction.Start) || input.IsPressed(PspAction.Cross)) { Name = Clip(Name.Trim()); Enter(TitleFlowStep.NameEntry); }
            else if (input.IsPressed(PspAction.Circle)) Backspace();
            return;
        }
        if (Step == TitleFlowStep.NameEntry)
        {
            if (input.IsPressed(PspAction.Start)) { if (Name.Length > 0) Enter(TitleFlowStep.RulesQuestion); }
            else if (input.IsPressed(PspAction.Cross)) Enter(TitleFlowStep.NameKeyboard);
            else if (input.IsPressed(PspAction.Circle)) EnterFaded(TitleFlowStep.ExitToTitle);
            return;
        }
        if (Step == TitleFlowStep.MainMenu) { HandleMainMenu(input); return; }
        if (Step == TitleFlowStep.Options) { HandleOptions(input); return; }
        if (Step == TitleFlowStep.DeckMenu) { HandleDeckMenu(input); return; }
        if (Step == TitleFlowStep.DeckEdit) { HandleDeckEdit(input); return; }
        if (ButtonCount == 2 && (input.IsPressed(PspAction.Left) || input.IsPressed(PspAction.Right))) ButtonFocus = 1 - ButtonFocus;
        if (input.IsPressed(PspAction.Cross)) Confirm();
        else if (input.IsPressed(PspAction.Circle)) Back();
    }

    private void HandleMainMenu(PspInputFrame input)
    {
        // Observed: presses during the entry sequence are dropped, not queued.
        if (!MenuReady) return;
        // A direction only repeats if its own press started on this menu, so a key held across a screen change cannot scroll.
        int held = (input.Held & (PspAction.Up | PspAction.Down)) switch { PspAction.Up => -1, PspAction.Down => 1, _ => 0 };
        if (held != heldDirection) heldDirection = 0;
        int pressed = input.IsPressed(PspAction.Up) ? -1 : input.IsPressed(PspAction.Down) ? 1 : 0;
        if (pressed != 0 && pressed == held)
        {
            MenuFocus = (MenuFocus + pressed + MainMenuRows) % MainMenuRows;
            heldDirection = pressed;
            menuRepeat.Restart();
        }
        if (input.IsPressed(PspAction.Circle)) { Enter(TitleFlowStep.QuitQuestion); return; }
        if (!input.IsPressed(PspAction.Cross)) return;
        switch (FocusedDestination.Route)
        {
            case MainMenuRoute.NativeSave: saveSource = SaveSource.MainMenu; Enter(TitleFlowStep.SystemSavePrompt); break;
            // Measured: the menu fades out at once, with no dialog lead.
            case MainMenuRoute.Options: EnterFaded(TitleFlowStep.Options, keepMenuFocus: true, lead: 0); break;
            // Observed: the submenu opens over the live, dimmed menu with no fade.
            case MainMenuRoute.DeckMenu: Enter(TitleFlowStep.DeckMenu, keepMenuFocus: true); break;
            default: Enter(TitleFlowStep.MainMenuUnavailable); break;
        }
    }

    private void HandleDeckMenu(PspInputFrame input)
    {
        // Observed: back hides the submenu's focus, then returns to the Main Menu on the Deck Menu row after the close frames.
        var outcome = DeckMenu!.Handle(input);
        if (outcome == DeckMenuOutcome.Closing && instantTransitions) ReturnFromDeckMenu();
        // Deck Edit is recreated; its row opens the screen when the host can build a session, and otherwise keeps the PC notice.
        if (outcome == DeckMenuOutcome.Notice && DeckMenu.Focused.Id == "deck-edit" && host.OpenDeckEdit() is { } session)
        {
            DeckMenu.CloseNotice();
            pendingDeckEdit = session;
            EnterFaded(TitleFlowStep.DeckEdit, keepMenuFocus: true, lead: 0);
        }
    }

    private void HandleDeckEdit(PspInputFrame input)
    {
        var outcome = DeckEdit!.Handle(input);
        if (outcome == DeckEditOutcome.None) return;
        if (outcome == DeckEditOutcome.LeaveAndSave) host.SaveDeckEdit(DeckEdit);
        // Observed: after the save question the Deck Menu submenu is back, Deck Edit focused.
        deckMenuSettled = true;
        EnterFaded(TitleFlowStep.DeckMenu, keepMenuFocus: true, lead: 0);
    }

    private void ReturnFromDeckMenu()
    {
        Enter(TitleFlowStep.MainMenu, keepMenuFocus: true);
        sinceSubmenuClosed = TimeSpan.Zero;
    }

    private void HandleOptions(PspInputFrame input)
    {
        // Observed: Options ignores input until its own entry sequence finishes, like the menu.
        if (!MenuReady || Options is null) return;
        switch (Options.Handle(input))
        {
            case OptionsOutcome.Accepted:
                bool changed = Options.Changed;
                AcceptOptions(Options.Values);
                // Observed: unchanged values return directly; changed ones fade to the save question over the bare background.
                if (changed) EnterFaded(TitleFlowStep.OptionsSaveQuestion, keepMenuFocus: true, lead: 0);
                else ReturnFromOptions(lead: 0);
                break;
            case OptionsOutcome.Discarded: ReturnFromOptions(ScreenFade.DialogLeadFrames); break;
        }
    }

    private void AcceptOptions(IReadOnlyList<int> values)
    {
        SessionOptions = new PlayerOptions(PlayerOptions.CurrentSchema, values.Take(OptionsCatalog.DuelistLevelRow).ToArray());
        Level = (DuelistLevelChoice)values[OptionsCatalog.DuelistLevelRow];
    }

    public void TypeCharacter(char value)
    {
        if (!Editing) return;
        if (value == '\b') { Backspace(); return; }
        if (!char.IsControl(value) && Name.Length < NameLimit) Name += value;
    }

    private void Backspace() { if (Name.Length > 0) Name = Name[..^1]; }

    private void Confirm()
    {
        switch (Step)
        {
            case TitleFlowStep.NoData: Enter(TitleFlowStep.CancelLoadNoData); break;
            case TitleFlowStep.CancelLoadNoData:
                if (ButtonFocus == 0) EnterFaded(TitleFlowStep.ExitToTitle); else Enter(TitleFlowStep.NoData);
                break;
            case TitleFlowStep.RulesQuestion:
                if (ButtonFocus == 0) Enter(TitleFlowStep.ConfidenceQuestion);
                else { Level = DuelistLevelChoice.FirstTimer; Enter(TitleFlowStep.DuelistLevel); }
                break;
            case TitleFlowStep.ConfidenceQuestion:
                Level = ButtonFocus == 0 ? DuelistLevelChoice.BringItOn : DuelistLevelChoice.BeenThere;
                Enter(TitleFlowStep.DuelistLevel); break;
            case TitleFlowStep.DuelistLevel: Enter(TitleFlowStep.CreateQuestion); break;
            // No enters the Main Menu without writing data, exactly as observed.
            case TitleFlowStep.CreateQuestion:
                if (ButtonFocus == 0) { saveSource = SaveSource.NewGame; Enter(TitleFlowStep.SystemSavePrompt); }
                else EnterFaded(TitleFlowStep.MainMenu);
                break;
            case TitleFlowStep.SystemSavePrompt:
                if (ButtonFocus == 0)
                {
                    if (saveSource == SaveSource.NewGame) host.CreateProfile(Name, Level, SessionOptions);
                    else host.SaveProfile(Name, Level, SessionOptions);
                    Enter(TitleFlowStep.SystemSaveDone);
                }
                else LeaveSavePrompt();
                break;
            // Observed: "Save completed." closes only with back; confirm does nothing.
            case TitleFlowStep.SystemSaveDone: break;
            case TitleFlowStep.QuitQuestion:
                if (ButtonFocus == 0) EnterFaded(TitleFlowStep.ExitToTitle); else Enter(TitleFlowStep.MainMenu, keepMenuFocus: true);
                break;
            case TitleFlowStep.MainMenuUnavailable: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            case TitleFlowStep.OptionsSaveQuestion:
                if (ButtonFocus == 0) { saveSource = SaveSource.Options; Enter(TitleFlowStep.SystemSavePrompt); }
                else ReturnFromOptions(ScreenFade.DialogLeadFrames);
                break;
        }
    }

    private void Back()
    {
        switch (Step)
        {
            // Observed: back resolves these questions as No; message boxes and Cancel load ignore it.
            case TitleFlowStep.RulesQuestion: Level = DuelistLevelChoice.FirstTimer; Enter(TitleFlowStep.DuelistLevel); break;
            case TitleFlowStep.ConfidenceQuestion: Level = DuelistLevelChoice.BeenThere; Enter(TitleFlowStep.DuelistLevel); break;
            case TitleFlowStep.CreateQuestion: EnterFaded(TitleFlowStep.MainMenu); break;
            case TitleFlowStep.SystemSavePrompt: LeaveSavePrompt(); break;
            case TitleFlowStep.SystemSaveDone: LeaveSaveDone(); break;
            case TitleFlowStep.QuitQuestion: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            case TitleFlowStep.MainMenuUnavailable: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            // Observed: back on the save question returns to the menu, keeping the accepted changes.
            case TitleFlowStep.OptionsSaveQuestion: ReturnFromOptions(ScreenFade.DialogLeadFrames); break;
        }
    }

    /// <summary>The New Game route is entering the menu for the first time; the others return to the row they came from.</summary>
    private void LeaveSaveDone()
    {
        switch (saveSource)
        {
            case SaveSource.NewGame: EnterFaded(TitleFlowStep.MainMenu); break;
            case SaveSource.MainMenu: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            default: ReturnFromOptions(lead: 0); break;
        }
    }

    /// <summary>Not answering Yes: New Game goes back to its question; the menu and Options routes return to the menu without saving.
    /// The Options route's No is unobserved and follows its save question's No.</summary>
    private void LeaveSavePrompt()
    {
        switch (saveSource)
        {
            case SaveSource.NewGame: Enter(TitleFlowStep.CreateQuestion); break;
            case SaveSource.MainMenu: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            default: ReturnFromOptions(ScreenFade.DialogLeadFrames); break;
        }
    }

    private void Enter(TitleFlowStep next, bool keepMenuFocus = false, bool replayEntry = false)
    {
        Step = next; elapsed = TimeSpan.Zero; heldDirection = 0;
        // Observed default focus: Cancel load, the new-data save prompt and the Options save question offer Yes; other
        // questions default to No, and so does the firmware dialog when it would overwrite an existing save.
        ButtonFocus = next is TitleFlowStep.RulesQuestion or TitleFlowStep.ConfidenceQuestion or TitleFlowStep.CreateQuestion or TitleFlowStep.QuitQuestion
            || next == TitleFlowStep.SystemSavePrompt && host.ProfilePresent ? 1 : 0;
        if (next == TitleFlowStep.MainMenu && !keepMenuFocus) MenuFocus = 0;
        if ((next == TitleFlowStep.MainMenu && (!keepMenuFocus || replayEntry) || next == TitleFlowStep.Options) && !instantTransitions)
            menuClock = TimeSpan.Zero;
        if (next == TitleFlowStep.Options) Options = new OptionsSession(SessionOptions.WithLevel(Level));
        else if (next is not TitleFlowStep.OptionsSaveQuestion) Options = null;
        // Observed: every opening starts on Deck Edit with its fade-in; exact runs show the settled submenu.
        DeckMenu = next == TitleFlowStep.DeckMenu ? new DeckMenuSession(deckMenuProfile) : null;
        if (DeckMenu is not null && (instantTransitions || deckMenuSettled)) DeckMenu.SkipTo(deckMenuProfile.ReadyFrame);
        deckMenuSettled = false;
        DeckEdit = next == TitleFlowStep.DeckEdit ? pendingDeckEdit : null;
        pendingDeckEdit = null;
        sinceSubmenuClosed = null;
    }

    private static string Clip(string value) => value.Length <= NameLimit ? value : value[..NameLimit];
}
