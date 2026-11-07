using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public readonly record struct TitlePresentationState(bool ChoicesVisible, int Choice);

/// <summary>Title prompt and choice route; saving and loading belong to the application adapter.</summary>
public sealed class TitleScreen(string screenId, bool choicesVisible, Action<TitlePresentationState> show,
    Action hide, Action continueRequested, Action newGameRequested, bool savePresent = true, Func<bool>? savePresentNow = null) : IGameScreen
{
    private ScreenContext context = null!;
    private int choice;
    public string ScreenId => screenId;
    public void OnEnter(ScreenContext context, ScreenArguments arguments)
    {
        this.context = context;
        // Focus starts on New Game at every entry. Continue stays selectable and reports no data.
        choice = (savePresentNow?.Invoke() ?? savePresent) ? 1 : 0;
        show(new(choicesVisible, choice));
    }
    public void HandleInput(PspInputFrame input)
    {
        if (!choicesVisible)
        {
            // Desktop A/Enter is the application confirm alias; F1/pad Start preserves Start.
            if (input.IsPressed(PspAction.Start) || input.IsPressed(PspAction.Cross))
                context.Navigation.GoTo("original.title-start-menu");
            return;
        }
        if (input.IsPressed(PspAction.Circle)) { context.Navigation.GoTo("original.title"); return; }
        if (input.IsPressed(PspAction.Up) || input.IsPressed(PspAction.Down))
        { choice = 1 - choice; show(new(true, choice)); }
        if (input.IsPressed(PspAction.Cross))
        {
            if (choice == 1) continueRequested(); else newGameRequested();
        }
    }
    public void Update(TimeSpan simulationDelta, TimeSpan presentationTime) { }
    public void Draw(ScreenDrawContext draw) { }
    public void OnExit() => hide();
}
