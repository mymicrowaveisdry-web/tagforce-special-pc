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
public sealed record ListModal(IReadOnlyList<ChoiceView> Choices, int Index, bool Cancelable) : DuelModal
{
    internal DuelModal? Parent { get; init; }
}
/// <summary>Attack target selection; <paramref name="Elapsed"/> drives the trail animation (restarted when the target changes, inferred).</summary>
public sealed record TargetModal(IReadOnlyList<ChoiceView> Choices, int Index, TimeSpan Elapsed = default) : DuelModal;
/// <summary>Show Details: the Card Album's detail page for the card (<paramref name="Detail"/>, null when the host has no detail renderer).</summary>
public sealed record DetailsModal(CardView Card, App.Screens.Album.CardAlbumDetail? Detail = null) : DuelModal;

public enum DuelFeedbackKind { None, Rejected, Unsupported, Absent, Required }
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
    private bool refreshAfterRejection;

    public DuelFieldFocus Focus { get; } = new();
    public DuelModal? Modal { get; private set; }
    public DuelFeedback Feedback { get; private set; }
    /// <summary>The object menu the last submission was chosen from (the presentation keeps drawing it briefly); null otherwise.</summary>
    public CardMenuModal? SubmittedFrom { get; private set; }
    /// <summary>The card the left panel keeps showing on the field until the next press (the summoned card); null otherwise.</summary>
    public CardView? HeldInspection { get; private set; }

    /// <summary>Builds the detail page for a visible card (the host's counts and content heights); null shows no page.</summary>
    public Func<CardView, App.Screens.Album.CardAlbumDetail?>? DetailFactory { get; set; }
    private int detailCloseUpdates;

    /// <summary>Processes one frame. Returns a choice to submit through <c>DuelController.Choose</c>, or null.</summary>
    public DuelSubmit? Handle(DuelView view, InputFrame frame, TimeSpan delta)
    {
        if (view.Revision != revision || refreshAfterRejection) return Refresh(view);
        if (view.Outcome is not null) return null;
        var (dx, dy, repeated) = Directions(frame, delta);
        bool confirm = frame.IsPressed(InputAction.Confirm), back = frame.IsPressed(InputAction.Back), menu = frame.IsPressed(InputAction.Menu);
        if (confirm || back || menu || dx != 0 || dy != 0) { Feedback = default; HeldInspection = null; }
        return Modal switch
        {
            null => Field(view, dx, dy, confirm, back, menu),
            CardMenuModal m => CardMenu(view, m, dy, repeated, confirm, back),
            GlobalMenuModal g => GlobalMenu(view, g, dy, repeated, confirm, back),
            PhaseModal p => PhaseSelector(view, p, dx, confirm, back),
            ResponseModal r => Response(view, r, dx, confirm, back),
            ListModal l => List(view, l, DuelBoardDecision.OnField(l) && dx != 0 ? -dx : dy, repeated, confirm, back),
            TargetModal t => Target(view, t with { Elapsed = t.Elapsed + delta }, dx, confirm, back),
            DetailsModal d => Details(d, frame),
            CardListModal c => CardListInput(view, c, dx, dy, repeated, confirm, back, frame),
            PositionModal p => PositionInput(view, p, dx, confirm),
            TributeConfirmModal c => TributeConfirm(c, dx, confirm, back),
            TributeModal t => TributeInput(view, t, dx, confirm, back),
            _ => null
        };
    }

    /// <summary>The controller refused a submission (stale revision or rule): close menus without changing state and say why.</summary>
    public void Rejected(string notice)
    {
        Modal = null;
        refreshAfterRejection = true;
        Feedback = new(DuelFeedbackKind.Rejected, notice);
    }

    /// <summary>A new view: rebuild the focus, drop menus built from old choices and open whatever the new choices call for.</summary>
    private DuelSubmit? Refresh(DuelView view)
    {
        bool recovering = refreshAfterRejection;
        refreshAfterRejection = false;
        heldDirection = InputAction.None;
        repeat.Restart();
        revision = view.Revision;
        Focus.Sync(view);
        Modal = null;
        if (phase != view.Phase && view.Phase == DuelPhase.Battle && view.ActivePlayer == view.Viewer)
            FocusFirstAttacker(view); // measured: entering Battle Phase moves the cursor onto the player's monster
        // Measured (order34-closure): after an attack the cursor moves on to a monster that can still attack.
        else if (phase == DuelPhase.Battle && view.Phase == DuelPhase.Battle && view.ActivePlayer == view.Viewer &&
                 !view.Choices.Any(c => c.Kind == ActionKind.Attack && c.FocusInstanceId == Focus.Current.Card?.InstanceId))
            FocusFirstAttacker(view);
        // Measured (matched walk): the player's Main Phase 1 starts with the cursor on the centre Spell & Trap Zone.
        if (phase is DuelPhase.Draw or DuelPhase.Standby && view.Phase == DuelPhase.Main1 && view.ActivePlayer == view.Viewer && Focus.Current.Kind != FieldObjectKind.Deck)
            Focus.FocusOn(Focus.Row(DuelFieldRows.OwnSpellTrap).First(o => o.Kind == FieldObjectKind.SpellTrap && o.Index == 2));
        phase = view.Phase;
        if (view.Outcome is not null) return null;
        switch (DuelFieldCommands.Mode(view))
        {
            // Measured: the duel's first turn draws nothing; later Draw Phases wait on the Deck's one-row "Draw" menu (order34-closure).
            case DuelInputMode.AutoDraw when view.Turn == 1 && !recovering: return new DuelSubmit(view.Revision, view.Choices[0].Id);
            case DuelInputMode.AutoDraw: OpenDrawMenu(view); break;
            case DuelInputMode.AutoStandby when !recovering: return new DuelSubmit(view.Revision, DuelFieldCommands.StandbyExit(view)!.Id);
            case DuelInputMode.Response: Modal = new ResponseModal(0); break;
            // Measured original screens for a position choice and a card choice; other decisions keep the list.
            case DuelInputMode.DecisionList when PositionScreen.Applies(view.Choices): Modal = PositionScreen.Open(view.Choices); break;
            case DuelInputMode.DecisionList when CardList.Selects(view.Choices): Modal = CardList.Select(view.Choices); break;
            case DuelInputMode.DecisionList: Modal = new ListModal(view.Choices, 0, false); break;
            case DuelInputMode.Waiting when view.Availability == ActionAvailability.Unsupported:
                Feedback = new(DuelFeedbackKind.Unsupported, "This decision is not supported by the PC duel.");
                break;
        }
        return null;
    }

    /// <summary>The Draw Phase prompt: the cursor on the player's Deck with its "Draw" menu open (○ draws; × does nothing, inferred).</summary>
    private void OpenDrawMenu(DuelView view)
    {
        Focus.FocusOn(Focus.Row(DuelFieldRows.OwnBottom).First(o => o.Kind == FieldObjectKind.Deck));
        Modal = new CardMenuModal(Focus.Current, [new("Draw", new SubmitCommand(view.Choices[0].Id), MenuVerb.Draw)], 0);
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
            if (items.Count > 0) Modal = new CardMenuModal(Focus.Current, items, 0);
            else Feedback = new(DuelFeedbackKind.Absent, "No action for this selection.");
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
        if (back && m.Items is [{ Verb: MenuVerb.Draw }]) return null;
        if (back) return Close(true);
        if (dy != 0) { Modal = m with { Index = Step(m.Index, -dy, m.Items.Count, wrap: !repeated) }; return null; }
        if (!confirm) return null;
        switch (m.Items[m.Index].Command)
        {
            case SubmitCommand s:
                Modal = null; SubmittedFrom = m;
                // Measured (matched walk): after a Normal Summon the left panel keeps the summoned card until the cursor moves.
                HeldInspection = m.Items[m.Index].Verb == MenuVerb.Summon ? m.Target.Card : null;
                return new DuelSubmit(view.Revision, s.ChoiceId);
            case TargetCommand t: return StartTargets(view, t.Choices);
            case ListCommand l: Modal = new ListModal(l.Choices, 0, true) { Parent = m }; return null;
            case DetailsCommand d: OpenDetails(d.Card); return null;
            case CardListCommand c: Modal = new CardListModal(c.Own, c.Pile); return null;
            case TributeCommand t: Modal = new TributeConfirmModal(t.Choices, 0); return null;
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
            case 0: Feedback = new(DuelFeedbackKind.Absent, "No visible card to inspect."); break;
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
        // × declines like NO (measured: × closes the Chain prompt).
        if (back) return DeclineResponse(view, pass);
        if (dx != 0) { Modal = r with { Index = Step(r.Index, dx, 2, wrap: true) }; return null; }
        if (!confirm) return null;
        if (r.Index == 1) return DeclineResponse(view, pass);
        if (responses.Count == 0) { Feedback = new(DuelFeedbackKind.Absent, "No response is available."); return null; }
        if (responses.Count == 1) { Modal = null; return new DuelSubmit(view.Revision, responses[0].Id); }
        Modal = new ListModal(responses, 0, true) { Parent = r };
        return null;
    }

    private DuelSubmit? DeclineResponse(DuelView view, ChoiceView? pass)
    {
        if (pass is null) { Feedback = new(DuelFeedbackKind.Required, "A response is required."); return null; }
        Modal = null;
        return new DuelSubmit(view.Revision, pass.Id);
    }

    private DuelSubmit? List(DuelView view, ListModal l, int dy, bool repeated, bool confirm, bool back)
    {
        if (back)
        {
            if (l.Cancelable) Modal = l.Parent;
            else Feedback = new(DuelFeedbackKind.Required, "Choose an option to continue.");
            return null;
        }
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

    /// <summary>The card list (measured): directions per <see cref="CardList.Move"/>; ○ on Close and × close it with the field cursor where it
    /// was; ○ on a card does nothing. △ (sort menu) and □ (field check) open original screens the PC does not have yet.</summary>
    private DuelSubmit? CardListInput(DuelView view, CardListModal list, int dx, int dy, bool repeated, bool confirm, bool back, InputFrame frame)
    {
        if (list.Options is { } options)
        {
            // Selection mode (measured): ○ on a card marks it and moves to OK; ○ on OK submits the marked card. Inferred: × does nothing
            // (the captured choice was mandatory) and OK without a mark does nothing.
            if (confirm && list.OnClose)
            {
                if (list.Marked is not { } marked) return null;
                Modal = null;
                return new DuelSubmit(view.Revision, options[marked].Id);
            }
            if (confirm) { Modal = list with { Marked = list.Selected, OnClose = true }; return null; }
            Modal = CardList.Move(list, dx, dy, options.Count, repeated);
            return null;
        }
        if (back || confirm && list.OnClose) return Close(true);
        if (frame.IsPressed(InputAction.Menu) || frame.IsPressed(InputAction.Switch))
        {
            Feedback = new(DuelFeedbackKind.Unsupported, "Sorting and the field check are not available in the PC card list yet.");
            return null;
        }
        Modal = CardList.Move(list, dx, dy, CardList.Cards(view, list.Own, list.Pile).Count, repeated);
        return null;
    }

    /// <summary>Battle position screen: Left/Right switch (inferred), ○ submits (measured).</summary>
    private DuelSubmit? PositionInput(DuelView view, PositionModal modal, int dx, bool confirm)
    {
        if (confirm) { Modal = null; return new DuelSubmit(view.Revision, modal.Choices[modal.Index].Id); }
        if (dx != 0) Modal = modal with { Index = Step(modal.Index, dx, modal.Choices.Count, wrap: false) };
        return null;
    }

    /// <summary>Tribute prompt (see <see cref="TributeSelection"/>): YES opens the selection with the field cursor on the first eligible
    /// monster; NO and × close.</summary>
    private DuelSubmit? TributeConfirm(TributeConfirmModal modal, int dx, bool confirm, bool back)
    {
        if (back || confirm && modal.Index == 1) return Close(true);
        if (dx != 0) { Modal = modal with { Index = Step(modal.Index, dx, 2, wrap: true) }; return null; }
        if (confirm) FocusTribute(TributeSelection.Open(modal.Choices));
        return null;
    }

    private DuelSubmit? TributeInput(DuelView view, TributeModal modal, int dx, bool confirm, bool back)
    {
        if (back) return Close(true);
        if (dx != 0) { FocusTribute(TributeSelection.Move(modal, dx)); return null; }
        if (!confirm) return null;
        var (next, submit) = TributeSelection.Toggle(modal);
        if (submit is null) { FocusTribute(next); return null; }
        Modal = null;
        return new DuelSubmit(view.Revision, submit.Id);
    }

    private void FocusTribute(TributeModal modal)
    {
        Modal = modal;
        if (TributeSelection.Slot(Focus, modal.Zone) is { } slot) Focus.FocusOn(slot);
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
