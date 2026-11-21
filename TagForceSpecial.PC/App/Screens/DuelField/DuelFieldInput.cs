using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public abstract record DuelModal;
public sealed record CardMenuModal(FieldObject Target, IReadOnlyList<MenuItem> Items, int Index) : DuelModal;
public sealed record GlobalMenuModal(int Index) : DuelModal;
public sealed record PhaseModal(IReadOnlyList<(DuelPhase Phase, int? ChoiceId)> Options, int Index) : DuelModal;
/// <summary>Chain-style prompt: YES (index 0) or NO (index 1), side by side.</summary>
public sealed record ResponseModal(int Index) : DuelModal;
/// <summary>PC list for decisions whose original screens were not measured; <paramref name="Cancelable"/> when × may back out.</summary>
public sealed record ListModal(IReadOnlyList<ChoiceView> Choices, int Index, bool Cancelable) : DuelModal;
/// <summary>Attack target selection; <paramref name="Elapsed"/> drives the trail animation (restarted when the target changes, inferred).</summary>
public sealed record TargetModal(IReadOnlyList<ChoiceView> Choices, int Index, TimeSpan Elapsed = default) : DuelModal;
/// <summary>Show Details: the Card Album's detail page for the card (<paramref name="Detail"/>, null when the host has no detail renderer).</summary>
public sealed record DetailsModal(CardView Card, App.Screens.Album.CardAlbumDetail? Detail = null) : DuelModal;

/// <summary>No feedback, an engine rejection (with its notice), or a command the PC does not support yet.</summary>
public enum DuelFeedbackKind { None, Rejected, Unsupported }
public readonly record struct DuelFeedback(DuelFeedbackKind Kind, string Text);
/// <summary>A legal choice to submit, bound to the view revision it was chosen from.</summary>
public readonly record struct DuelSubmit(long Revision, int ChoiceId);

/// <summary>Duel Field input (13.2): measured focus movement, ○ object menus, × close or phase selector, △ global menu, Chain prompt,
/// and held-direction repeat. Every submission carries the view revision; a stale one is rejected by the controller. A press that
/// closes a modal is consumed and never reaches the field underneath.</summary>
public sealed class DuelFieldInput
{
    /// <summary>Measured (vblanks at 59.94 Hz): first repeat 56 after the first move, then every 16; field rows wrap while repeating,
    /// menus stop at their end rows.</summary>
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromSeconds(56 / 59.94), RepeatInterval = TimeSpan.FromSeconds(16 / 59.94);
    private readonly MenuHoldRepeat repeat = new(RepeatDelay, RepeatInterval);
    private InputAction heldDirection;
    private long revision = -1;
    private DuelPhase? phase;

    public DuelFieldFocus Focus { get; } = new();
    public DuelModal? Modal { get; private set; }
    public DuelFeedback Feedback { get; private set; }

    /// <summary>Builds the detail page for a visible card (the host's counts and content heights); null shows no page.</summary>
    public Func<CardView, App.Screens.Album.CardAlbumDetail?>? DetailFactory { get; set; }
    private int detailCloseUpdates;

    /// <summary>Processes one frame. Returns a choice to submit through <c>DuelController.Choose</c>, or null.</summary>
    public DuelSubmit? Handle(DuelView view, InputFrame frame, TimeSpan delta)
    {
        if (view.Revision != revision) return Refresh(view);
        if (view.Outcome is not null) return null;
        var (dx, dy, repeated) = Directions(frame, delta);
        bool confirm = frame.IsPressed(InputAction.Confirm), back = frame.IsPressed(InputAction.Back), menu = frame.IsPressed(InputAction.Menu);
        if (confirm || back || menu || dx != 0 || dy != 0) Feedback = default;
        return Modal switch
        {
            null => Field(view, dx, dy, confirm, back, menu),
            CardMenuModal m => CardMenu(view, m, dy, repeated, confirm, back),
            GlobalMenuModal g => GlobalMenu(view, g, dy, repeated, confirm, back),
            PhaseModal p => PhaseSelector(view, p, dx, confirm, back),
            ResponseModal r => Response(view, r, dx, confirm, back),
            ListModal l => List(view, l, dy, repeated, confirm, back),
            TargetModal t => Target(view, t with { Elapsed = t.Elapsed + delta }, dx, confirm, back),
            DetailsModal d => Details(d, frame),
            _ => null
        };
    }

    /// <summary>The controller refused a submission (stale revision or rule): close menus without changing state and say why.</summary>
    public void Rejected(string notice) { Modal = null; Feedback = new(DuelFeedbackKind.Rejected, notice); }

    /// <summary>A new view: rebuild the focus, drop menus built from old choices and open whatever the new choices call for.</summary>
    private DuelSubmit? Refresh(DuelView view)
    {
        revision = view.Revision;
        Focus.Sync(view);
        Modal = null;
        if (phase != view.Phase && view.Phase == DuelPhase.Battle && view.ActivePlayer == view.Viewer)
            FocusFirstAttacker(view); // measured: entering Battle Phase moves the cursor onto the player's monster
        phase = view.Phase;
        if (view.Outcome is not null) return null;
        switch (DuelFieldCommands.Mode(view))
        {
            case DuelInputMode.AutoDraw: return new DuelSubmit(view.Revision, view.Choices[0].Id);
            case DuelInputMode.Response: Modal = new ResponseModal(0); break;
            case DuelInputMode.DecisionList: Modal = new ListModal(view.Choices, 0, false); break;
            case DuelInputMode.Waiting when view.Availability == ActionAvailability.Unsupported:
                Feedback = new(DuelFeedbackKind.Unsupported, "This decision is not supported by the PC duel.");
                break;
        }
        return null;
    }

    private void FocusFirstAttacker(DuelView view)
    {
        var row = Focus.Row(DuelFieldRows.OwnMonster);
        var attacker = row.FirstOrDefault(o => o.Card?.InstanceId is { } id && view.Choices.Any(c => c.Kind == ActionKind.Attack && c.FocusInstanceId == id));
        if (attacker.Card is not null) Focus.FocusOn(attacker);
    }

    private DuelSubmit? Field(DuelView view, int dx, int dy, bool confirm, bool back, bool menu)
    {
        if (menu) { Modal = new GlobalMenuModal(0); return null; }
        if (back) { OpenPhaseSelector(view); return null; }
        if (confirm)
        {
            var items = DuelFieldCommands.ObjectMenu(view, Focus.Current);
            if (items.Count > 0) Modal = new CardMenuModal(Focus.Current, items, 0); // no commands: nothing happens (measured)
            return null;
        }
        if (dx != 0 || dy != 0) Focus.Move(dx, dy);
        return null;
    }

    private void OpenPhaseSelector(DuelView view)
    {
        var options = DuelFieldCommands.PhaseOptions(view);
        if (options.Count > 1 || options.Any(o => o.ChoiceId is not null))
            Modal = new PhaseModal(options, options.ToList().FindIndex(o => o.Phase == view.Phase));
    }

    private DuelSubmit? CardMenu(DuelView view, CardMenuModal m, int dy, bool repeated, bool confirm, bool back)
    {
        if (back) return Close(true);
        if (dy != 0) { Modal = m with { Index = Step(m.Index, -dy, m.Items.Count, wrap: !repeated) }; return null; }
        if (!confirm) return null;
        switch (m.Items[m.Index].Command)
        {
            case SubmitCommand s: Modal = null; return new DuelSubmit(view.Revision, s.ChoiceId);
            case TargetCommand t: return StartTargets(view, t.Choices);
            case ListCommand l: Modal = new ListModal(l.Choices, 0, true); return null;
            case DetailsCommand d: OpenDetails(d.Card); return null;
            case UnsupportedCommand u: Modal = null; Feedback = new(DuelFeedbackKind.Unsupported, u.Notice); return null;
        }
        return null;
    }

    private DuelSubmit? GlobalMenu(DuelView view, GlobalMenuModal g, int dy, bool repeated, bool confirm, bool back)
    {
        if (back) return Close(true);
        if (dy != 0) { Modal = g with { Index = Step(g.Index, -dy, DuelFieldCommands.GlobalMenu.Count, wrap: !repeated) }; return null; }
        if (!confirm) return null;
        Modal = null;
        switch (g.Index)
        {
            case 0 when Focus.Current.Card is { Hidden: false } card: OpenDetails(card); break;
            case 1: OpenPhaseSelector(view); break;
            case 2: Feedback = new(DuelFeedbackKind.Unsupported, "Duel options are not available in the PC duel yet."); break;
            case 3: Feedback = new(DuelFeedbackKind.Unsupported, "Duel help is not available in the PC duel yet."); break;
        }
        return null;
    }

    private DuelSubmit? PhaseSelector(DuelView view, PhaseModal p, int dx, bool confirm, bool back)
    {
        if (back) return Close(true);
        if (dx != 0) { Modal = p with { Index = Step(p.Index, dx, p.Options.Count, wrap: true) }; return null; }
        if (!confirm) return null;
        Modal = null;
        return p.Options[p.Index].ChoiceId is { } id ? new DuelSubmit(view.Revision, id) : null;
    }

    private DuelSubmit? Response(DuelView view, ResponseModal r, int dx, bool confirm, bool back)
    {
        var (responses, pass) = DuelFieldCommands.Responses(view);
        if (dx != 0) { Modal = r with { Index = Step(r.Index, dx, 2, wrap: true) }; return null; }
        // × declines like NO (measured: × closes the Chain prompt).
        if (back || confirm && r.Index == 1) { Modal = null; return pass is null ? null : new DuelSubmit(view.Revision, pass.Id); }
        if (!confirm) return null;
        if (responses.Count == 1) { Modal = null; return new DuelSubmit(view.Revision, responses[0].Id); }
        Modal = new ListModal(responses, 0, true);
        return null;
    }

    private DuelSubmit? List(DuelView view, ListModal l, int dy, bool repeated, bool confirm, bool back)
    {
        if (back && l.Cancelable) return Close(true);
        if (dy != 0) { Modal = l with { Index = Step(l.Index, -dy, l.Choices.Count, wrap: !repeated) }; return null; }
        if (!confirm) return null;
        Modal = null;
        return new DuelSubmit(view.Revision, l.Choices[l.Index].Id);
    }

    /// <summary>Attack targets: measured, a single monster target still opens the target state (cursor, sword, trail) and ○ confirms it.
    /// Inferred: Left/Right move among several targets; a direct attack alone submits at once (its screen was not captured).</summary>
    private DuelSubmit? StartTargets(DuelView view, IReadOnlyList<ChoiceView> choices)
    {
        Modal = null;
        if (choices.Count == 1 && DuelFieldCommands.AttackTarget(choices[0]) is null) return new DuelSubmit(view.Revision, choices[0].Id);
        var ordered = choices.OrderBy(c => DuelFieldCommands.AttackTarget(c) ?? -1).ToArray();
        Modal = new TargetModal(ordered, 0);
        return null;
    }

    private DuelSubmit? Target(DuelView view, TargetModal t, int dx, bool confirm, bool back)
    {
        if (back) return Close(true);
        Modal = t;
        if (dx != 0) { Modal = t with { Index = Step(t.Index, dx, t.Choices.Count, wrap: true), Elapsed = TimeSpan.Zero }; return null; }
        if (!confirm) return null;
        Modal = null;
        return new DuelSubmit(view.Revision, t.Choices[t.Index].Id);
    }

    private void OpenDetails(CardView card) { Modal = new DetailsModal(card, DetailFactory?.Invoke(card)); detailCloseUpdates = 0; }

    /// <summary>The album detail page takes all input (Left/Right tabs, held Up/Down scroll); measured: Back shows one blank frame, then the
    /// field returns with the cursor where it was. Without a page, ○ or × closes.</summary>
    private DuelSubmit? Details(DetailsModal modal, InputFrame frame)
    {
        if (modal.Detail is not { } detail)
            return Close(frame.IsPressed(InputAction.Confirm) || frame.IsPressed(InputAction.Back));
        // As the album: the Back update already counts, so exactly one blank frame is drawn.
        detail.Handle(frame);
        if (detail.Closing && ++detailCloseUpdates >= 2) Modal = null;
        return null;
    }

    private DuelSubmit? Close(bool pressed)
    {
        if (pressed) Modal = null;
        return null;
    }

    private static int Step(int index, int delta, int count, bool wrap)
    {
        if (count == 0) return 0;
        int next = index + delta;
        return wrap ? (next % count + count) % count : Math.Clamp(next, 0, count - 1);
    }

    /// <summary>One move per new press; while the same direction stays held, measured repeats. Up is +1 row, Right is +1.</summary>
    private (int Dx, int Dy, bool Repeated) Directions(InputFrame frame, TimeSpan delta)
    {
        InputAction direction = Pressed(frame);
        if (direction != InputAction.None) { heldDirection = direction; repeat.Restart(); return (Dx(direction), Dy(direction), false); }
        if (heldDirection == InputAction.None || (frame.Held & heldDirection) == 0) { heldDirection = InputAction.None; return (0, 0, false); }
        int due = repeat.Advance(delta);
        return due == 0 ? (0, 0, false) : (Dx(heldDirection) * due, Dy(heldDirection) * due, true);
    }

    private static InputAction Pressed(InputFrame frame) =>
        frame.IsPressed(InputAction.Up) ? InputAction.Up : frame.IsPressed(InputAction.Down) ? InputAction.Down
        : frame.IsPressed(InputAction.Left) ? InputAction.Left : frame.IsPressed(InputAction.Right) ? InputAction.Right : InputAction.None;

    private static int Dx(InputAction a) => a == InputAction.Left ? -1 : a == InputAction.Right ? 1 : 0;
    private static int Dy(InputAction a) => a == InputAction.Up ? 1 : a == InputAction.Down ? -1 : 0;
}
