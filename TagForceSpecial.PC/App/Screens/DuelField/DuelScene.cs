using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Per-duel presentation: the backdrop piece ("default", "tf4", "tf6"), the HUD names and the face icon stems of both duelists.</summary>
public sealed record DuelFieldStyle(string Backdrop = "default", string PlayerName = "", string OpponentName = "",
    string PlayerFace = "fw0_PLAYER_0", string? OpponentFace = null);

/// <summary>One frame's presentation input: the authorized view plus the screen's own focus state. It holds no engine reference.</summary>
public sealed record DuelSceneInput(DuelView View, int Choice, int InspectedCard, string Notice, double Seconds, DuelFieldStyle? Style = null,
    Func<uint, DuelCardFacts?>? Facts = null, DuelFieldInput? Input = null);

/// <summary>Composes the duel field from its parts in a fixed draw order.</summary>
public static class DuelScene
{
    /// <summary>Renders the measured field into its layer (when original assets are loaded) and the remaining sprite parts over it.</summary>
    public static void Draw(SpriteRenderer renderer, DuelFieldVisuals? field, DuelSceneInput input)
    {
        var list = Compose(input, field?.Profile);
        if (field is not null)
        {
            field.Render(list.FieldDraws);
            renderer.Layer(field.Layer);
        }
        list.Replay(renderer, field);
    }

    /// <summary>Without <paramref name="field"/> (no validated original assets) the PC test visualization is composed instead.</summary>
    public static DuelDrawList Compose(DuelSceneInput input, DuelFieldProfile? field = null) =>
        field is null ? ComposeTestVisualization(input) : ComposeOriginal(input, field);

    private static DuelDrawList ComposeOriginal(DuelSceneInput input, DuelFieldProfile field)
    {
        var list = new DuelDrawList();
        var view = input.View;
        // Show Details is an opaque full-screen page; measured: its closing frame is blank.
        if (input.Input?.Modal is DetailsModal { Detail: { } page })
        {
            if (!page.Closing) list.Add(new DuelCardDetail(DuelPart.Inspector, page));
            return list;
        }
        var focus = Focused(input);
        var inspected = input.Input?.Modal is DetailsModal details ? details.Card
            : focus is { Card: { Hidden: false } card } ? card : input.Input is null ? CardInspector.Target(view, input.Choice, input.InspectedCard) : null;
        DuelBoardRenderer.DrawOriginal(list, field, (input.Style ?? new()).Backdrop);
        PhaseBar.DrawOriginal(list, field, view.Phase, input.Input?.Modal is PhaseModal);
        list.FieldPiece(DuelPart.Board, field, "depth.clear");
        DuelBoardRenderer.DrawPilesOriginal(list, field, view);
        int? lifted = focus is { Own: true, Kind: FieldObjectKind.Hand, Card.InstanceId: { } id } ? id : null;
        var placed = CardSpriteRenderer.DrawFieldOriginal(list, view, lifted);
        var attack = input.Input?.Modal as TargetModal;
        // GE order (attack-target capture): target cursor meshes, pointer, Battle Phase readouts, Levels, sword, trail and arrow head.
        if (attack is not null && focus is { } aimed) AttackTargetOverlay.DrawCursors(list, field, new(view, placed), aimed);
        if (focus is { } target) InputFocusOverlay.DrawOriginal(list, field, new(view, placed), target, input.Input?.Modal is not (PhaseModal or ResponseModal),
            attack is not null);
        BattleReadouts.Draw(list, field, view);
        CardSpriteRenderer.DrawLevels(list, field, placed, input.Facts);
        if (attack is not null && focus is { } end && AttackTargetOverlay.AttackerCentre(view, attack.Choices) is { } start)
            AttackTargetOverlay.DrawTrail(list, field, start, InputFocusOverlay.Place(new(view, placed), end).Centre with { Y = 0 },
                AttackTrail.Updates(attack.Elapsed));
        DuelHud.DrawOriginal(list, field, view, input.Style);
        if (!AttackInspector.TryDraw(list, view, attack, focus, input.Facts))
            CardInspector.DrawOriginal(list, inspected, input.Facts);
        if (input.Input is { } session) PromptPresenter.DrawModal(list, view, session, input.Notice);
        else PromptPresenter.Draw(list, view, input.Choice, input.Notice);
        DuelEffectLayer.Draw(list, view);
        return list;
    }

    private static DuelDrawList ComposeTestVisualization(DuelSceneInput input)
    {
        var list = new DuelDrawList();
        var view = input.View;
        var inspected = CardInspector.Target(view, input.Choice, input.InspectedCard);
        DuelBoardRenderer.DrawBackdrop(list);
        DuelHud.DrawBar(list);
        PhaseBar.Draw(list, view);
        DuelHud.DrawLifePoints(list, view);
        DuelBoardRenderer.DrawSurface(list);
        DrawSlots(list, DuelSceneLayout.RowSlots(view), inspected, input.Seconds);
        DuelBoardRenderer.DrawPiles(list, view);
        DrawSlots(list, DuelSceneLayout.SideSlots(view), inspected, input.Seconds);
        DuelBoardRenderer.DrawZoneLabels(list);
        CardInspector.Draw(list, inspected);
        PromptPresenter.Draw(list, view, input.Choice, input.Notice);
        DuelEffectLayer.Draw(list, view);
        return list;
    }

    /// <summary>The focused object: the input session's (an attack target while one is being chosen), else the object holding the
    /// selected choice's card.</summary>
    public static FieldObject? Focused(DuelSceneInput input)
    {
        var rows = DuelFieldRows.Build(input.View);
        if (input.Input is { } session)
        {
            if (session.Modal is TargetModal { } t && DuelFieldCommands.AttackTarget(t.Choices[t.Index]) is { } zone)
                return rows[DuelFieldRows.OpponentMonster].First(o => o.Kind == FieldObjectKind.Monster && o.Index == zone);
            return session.Focus.Ready ? session.Focus.Current : null;
        }
        if (SelectedCard(input.View, input.Choice) is not { } id) return null;
        foreach (var row in rows)
            foreach (var o in row)
                if (o.Card?.InstanceId == id) return o;
        return null;
    }

    /// <summary>The card the selected legal choice refers to, if any.</summary>
    public static int? SelectedCard(DuelView view, int choice) =>
        view.Choices.Count > 0 ? view.Choices[Math.Clamp(choice, 0, view.Choices.Count - 1)].FocusInstanceId : null;

    private static void DrawSlots(DuelDrawList list, IEnumerable<DuelSlot> slots, Core.Views.CardView? inspected, double seconds)
    {
        foreach (var slot in slots)
        {
            CardSpriteRenderer.Draw(list, slot.Card, slot.Rect);
            InputFocusOverlay.Draw(list, slot.Card, slot.Rect, inspected, seconds);
        }
    }
}
