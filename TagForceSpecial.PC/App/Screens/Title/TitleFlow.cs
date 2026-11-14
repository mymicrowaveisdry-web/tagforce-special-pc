using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public enum TitleFlowStep
{
    StorageCheck, NoData, CancelLoadNoData,
    NameEntry, NameKeyboard,
    RulesQuestion, ConfidenceQuestion, DuelistLevel, CreateQuestion, SystemSavePrompt, SystemSaveDone,
    MainMenu, MainMenuUnavailable, QuitQuestion, ExitToTitle,
    Options, OptionsSaveQuestion
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
}

/// <summary>Title, Main Menu and Options routes. PC confirm and back swap PSP Circle and Cross.</summary>
public sealed class TitleFlow(ITitleFlowHost host, string defaultName, bool instantTransitions = false)
{
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
    /// <summary>Options values for this session, kept until a save writes them.</summary>
    public PlayerOptions SessionOptions { get; private set; } = PlayerOptions.Defaults();
    public OptionsSession? Options { get; private set; }
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

    /// <summary>Leaving Options or its dialogs replays the menu's entry sequence on the Options row.</summary>
    private void ReturnFromOptions(int lead) => EnterFaded(TitleFlowStep.MainMenu, keepMenuFocus: true, replayEntry: true, lead: lead);

    public void Update(TimeSpan delta)
    {
        menuClock += delta;
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
        if (ButtonCount == 2 && (input.IsPressed(PspAction.Left) || input.IsPressed(PspAction.Right))) ButtonFocus = 1 - ButtonFocus;
        if (input.IsPressed(PspAction.Cross)) Confirm();
        else if (input.IsPressed(PspAction.Circle)) Back();
    }

    private void HandleMainMenu(PspInputFrame input)
    {
        // Presses during the entry sequence are dropped, not queued.
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
            default: Enter(TitleFlowStep.MainMenuUnavailable); break;
        }
    }

    private void HandleOptions(PspInputFrame input)
    {
        // Options ignores input until its entry sequence finishes.
        if (!MenuReady || Options is null) return;
        switch (Options.Handle(input))
        {
            case OptionsOutcome.Accepted:
                bool changed = Options.Changed;
                AcceptOptions(Options.Values);
                // Unchanged values return directly; changed ones fade to the save question.
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
            // No enters the Main Menu without writing data.
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
            // "Save completed." closes only with back.
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
            // Back answers these questions with No; message boxes and Cancel load ignore it.
            case TitleFlowStep.RulesQuestion: Level = DuelistLevelChoice.FirstTimer; Enter(TitleFlowStep.DuelistLevel); break;
            case TitleFlowStep.ConfidenceQuestion: Level = DuelistLevelChoice.BeenThere; Enter(TitleFlowStep.DuelistLevel); break;
            case TitleFlowStep.CreateQuestion: EnterFaded(TitleFlowStep.MainMenu); break;
            case TitleFlowStep.SystemSavePrompt: LeaveSavePrompt(); break;
            case TitleFlowStep.SystemSaveDone: LeaveSaveDone(); break;
            case TitleFlowStep.QuitQuestion: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            case TitleFlowStep.MainMenuUnavailable: Enter(TitleFlowStep.MainMenu, keepMenuFocus: true); break;
            // Back on the save question returns to the menu, keeping the accepted changes.
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

    /// <summary>
    /// Without Yes, New Game returns to its question and the menu and Options routes return to the menu unsaved.
    /// </summary>
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
        // Default focus per question: Yes for Cancel load, the new-data save prompt and the Options save question;
        // otherwise No.
        ButtonFocus = next is TitleFlowStep.RulesQuestion or TitleFlowStep.ConfidenceQuestion or TitleFlowStep.CreateQuestion or TitleFlowStep.QuitQuestion
            || next == TitleFlowStep.SystemSavePrompt && host.ProfilePresent ? 1 : 0;
        if (next == TitleFlowStep.MainMenu && !keepMenuFocus) MenuFocus = 0;
        if ((next == TitleFlowStep.MainMenu && (!keepMenuFocus || replayEntry) || next == TitleFlowStep.Options) && !instantTransitions)
            menuClock = TimeSpan.Zero;
        if (next == TitleFlowStep.Options) Options = new OptionsSession(SessionOptions.WithLevel(Level));
        else if (next is not TitleFlowStep.OptionsSaveQuestion) Options = null;
    }

    private static string Clip(string value) => value.Length <= NameLimit ? value : value[..NameLimit];
}
