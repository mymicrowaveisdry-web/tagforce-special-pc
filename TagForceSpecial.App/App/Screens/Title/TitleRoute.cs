using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public enum TitleScreenKind { Title, StartMenu, Flow }

/// <summary>The title, its start menu and the <see cref="TitleFlow"/> behind them, with the black screen fade, as one host-neutral driver: the
/// MonoGame host's title screens and flow screen glue. Hosts feed one input frame and step per update and draw what <see cref="Screen"/> and
/// <see cref="Flow"/> say. Exact runs change screens at once so each state stays frame-fixed.</summary>
public sealed class TitleRoute(ITitleFlowHost host, string defaultName, bool exact = false)
{
    public TitleScreenKind Screen { get; private set; } = TitleScreenKind.Title;
    /// <summary>Start menu focus: 0 = New Game, 1 = Continue.</summary>
    public int StartChoice { get; private set; }
    public TitleFlow Flow { get; } = new(host, defaultName, exact);
    public ScreenFade Fade { get; } = new();
    public bool Exact => exact;

    /// <summary>Scripted runs: holds the storage check while true; otherwise it resolves on the next update instead of after its measured time.</summary>
    public Func<bool>? ScriptedStorage { get; set; }

    public void PointStart(int choice)
    {
        if (Screen == TitleScreenKind.StartMenu && !Fade.Active && choice is 0 or 1) StartChoice = choice;
    }

    /// <summary>One update: input is ignored while a fade runs (the change at black may move to another screen), then the screen's own step.</summary>
    public void Tick(InputFrame input, TimeSpan delta)
    {
        bool fading = Fade.Active;
        Fade.Tick(delta);
        if (fading) input = default;
        Handle(input);
        Update(delta);
    }

    /// <summary>Full-screen black drawn over everything: the fade, or the Main Menu/Options entry overlay.</summary>
    public int BlackAlpha
    {
        get
        {
            int alpha = Fade.Alpha;
            if (Screen == TitleScreenKind.Flow && Flow.Step is TitleFlowStep.MainMenu or TitleFlowStep.MainMenuUnavailable or TitleFlowStep.Options
                or TitleFlowStep.DeckMenu or TitleFlowStep.DatabaseMenu)
                alpha = Math.Max(alpha, MainMenuEntry.OverlayAlpha(Flow.MenuFrame));
            return alpha;
        }
    }

    private void Handle(InputFrame input)
    {
        switch (Screen)
        {
            case TitleScreenKind.Title:
                // Desktop A/Enter is the application confirm alias; F1/pad Start preserves Start.
                if (input.IsPressed(InputAction.Start) || input.IsPressed(InputAction.Confirm)) EnterStartMenu();
                break;
            case TitleScreenKind.StartMenu:
                if (input.IsPressed(InputAction.Back)) { Screen = TitleScreenKind.Title; return; }
                if (input.IsPressed(InputAction.Up) || input.IsPressed(InputAction.Down)) StartChoice = 1 - StartChoice;
                if (!input.IsPressed(InputAction.Confirm)) return;
                // Measured: Continue reaches a black storage screen with no fade-in; New Game fades into its first screen.
                if (StartChoice == 1) ChangeBehindFade(() => BeginFlow(Flow.BeginContinue), fadeIn: false);
                else ChangeBehindFade(() => BeginFlow(Flow.BeginNewGame), fadeIn: true);
                break;
            case TitleScreenKind.Flow:
                Flow.Handle(input);
                // Dialogs hold briefly after confirming. The title has its own fade-in; the menu's entry sequence includes one.
                if (Flow.PendingStep is { } pending)
                    ChangeBehindFade(() => { Flow.CommitPending(); LeaveFlowIfDone(); }, pending == TitleFlowStep.ExitToTitle, Flow.PendingLeadFrames);
                LeaveFlowIfDone();
                break;
        }
    }

    private void Update(TimeSpan delta)
    {
        if (Screen != TitleScreenKind.Flow) return;
        if (ScriptedStorage is { } hold)
        {
            // Hold the storage frame for its own capture step, then resolve it deterministically.
            if (hold()) return;
            if (Flow.Step == TitleFlowStep.StorageCheck) delta = TitleFlow.StorageCheckDuration;
        }
        Flow.Update(delta);
    }

    /// <summary>Focus starts on New Game at every entry unless a profile is saved; Continue stays selectable and reports no data.</summary>
    private void EnterStartMenu()
    {
        Screen = TitleScreenKind.StartMenu;
        StartChoice = host.ProfilePresent ? 1 : 0;
    }

    private void BeginFlow(Action begin)
    {
        begin();
        Screen = TitleScreenKind.Flow;
    }

    private void LeaveFlowIfDone()
    {
        if (Screen == TitleScreenKind.Flow && Flow.Step == TitleFlowStep.ExitToTitle) Screen = TitleScreenKind.Title;
    }

    private void ChangeBehindFade(Action action, bool fadeIn, int lead = 0)
    {
        if (exact) action();
        else if (!Fade.Active) Fade.Begin(action, fadeIn, lead);
    }

    /// <summary>The scripted presentation route's state label (title, start menu, Options, submenus, then step/button/menu focus).</summary>
    public string Label() => Screen switch
    {
        TitleScreenKind.Title => "title",
        TitleScreenKind.StartMenu => "start:" + StartChoice,
        _ => Flow switch
        {
            { Step: TitleFlowStep.Options, Options: { } session } =>
                $"Options/{(session.CancelQuestion ? "Q" + session.CancelFocus : "L")}/{session.Focus}/{session.Values[session.Focus]}",
            { Step: TitleFlowStep.DeckMenu or TitleFlowStep.DatabaseMenu, Submenu: { } sub } =>
                $"{Flow.Step}/{(sub.NoticeOpen ? "N" : "L")}/{sub.Focus}/{Flow.MenuFocus}",
            _ => $"{Flow.Step}/{Flow.ButtonFocus}/{Flow.MenuFocus}"
        }
    };
}
