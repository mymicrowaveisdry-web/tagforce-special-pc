using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens;

/// <summary>One owner for screen lifecycle. Input transitions flush before update; modals freeze their parent.</summary>
public sealed class ScreenRouter : IScreenNavigation, IDisposable
{
    private sealed record Entry(IGameScreen Screen, string? Focus);
    private readonly Dictionary<string, IGameScreen> screens = new(StringComparer.Ordinal);
    private readonly List<Entry> stack = [];
    private readonly Queue<Action> pending = new();
    private readonly Func<IScreenNavigation, ScreenContext> createContext;
    private bool dispatching, disposed;
    public ScreenRouter(Func<IScreenNavigation, ScreenContext> createContext) => this.createContext = createContext;
    public string? CurrentScreenId => stack.LastOrDefault()?.Screen.ScreenId;
    public int ModalDepth => Math.Max(0, stack.Count - 1);
    public void Register(IGameScreen screen)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        screens.Add(screen.ScreenId, screen);
    }
    public void GoTo(string screenId, ScreenArguments? arguments = null) => Schedule(() =>
    {
        var next = Find(screenId);
        if (stack.Count == 1 && ReferenceEquals(stack[0].Screen, next)) return;
        ExitAll(); Enter(next, arguments);
    });
    public void PushModal(string screenId, ScreenArguments? arguments = null) => Schedule(() =>
    {
        var next = Find(screenId);
        if (stack.Count == 0) throw new InvalidOperationException("A modal needs an owning screen.");
        if (stack.Any(e => ReferenceEquals(e.Screen, next))) throw new InvalidOperationException("Screen already active.");
        var current = stack[^1];
        stack[^1] = current with { Focus = (current.Screen as IScreenFocus)?.CaptureFocus() };
        Enter(next, arguments);
    });
    public void PopModal() => Schedule(() =>
    {
        if (stack.Count < 2) throw new InvalidOperationException("No modal to close.");
        stack[^1].Screen.OnExit(); stack.RemoveAt(stack.Count - 1);
        (stack[^1].Screen as IScreenFocus)?.RestoreFocus(stack[^1].Focus);
    });
    private IGameScreen Find(string id) => screens.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException(id);
    private void Enter(IGameScreen screen, ScreenArguments? arguments)
    {
        screen.OnEnter(createContext(this), arguments ?? new());
        stack.Add(new(screen, null));
    }
    private void Schedule(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        pending.Enqueue(action);
        if (!dispatching) Flush();
    }
    private void Flush()
    {
        dispatching = true;
        try
        {
            int budget = 32;
            while (pending.TryDequeue(out var action))
            {
                if (--budget < 0) { pending.Clear(); throw new InvalidOperationException("Navigation cycle."); }
                action();
            }
        }
        finally { dispatching = false; }
    }
    public void Tick(PspInputFrame input, TimeSpan simulationDelta, TimeSpan presentationTime)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (simulationDelta < TimeSpan.Zero || presentationTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(simulationDelta));
        var owner = stack.LastOrDefault()?.Screen;
        dispatching = true;
        try { owner?.HandleInput(input); }
        finally { dispatching = false; }
        Flush();
        // A newly entered screen cannot consume the old screen's input or simulation delta.
        if (!ReferenceEquals(owner, stack.LastOrDefault()?.Screen)) return;
        dispatching = true;
        try { owner?.Update(simulationDelta, presentationTime); }
        finally { dispatching = false; }
        Flush();
    }
    public void Draw(ScreenDrawContext draw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        dispatching = true;
        try { foreach (var entry in stack) entry.Screen.Draw(draw); }
        finally { dispatching = false; }
        Flush();
    }
    private void ExitAll()
    {
        for (int i = stack.Count - 1; i >= 0; i--) stack[i].Screen.OnExit();
        stack.Clear();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; pending.Clear(); ExitAll();
        foreach (var screen in screens.Values.OfType<IDisposable>()) screen.Dispose();
        screens.Clear();
    }
}
