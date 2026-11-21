using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>The measured Duel Field input contract (guide 13.2) on authorized views.</summary>
public sealed class DuelFieldInputTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / 59.94));

    private static CardView Card(int id, uint card = 100) => new(id, card, "Card " + id, "", 1000, 1000, null, false);
    private static CardView Hidden() => new(null, null, "Hidden card", "", null, null, null, true);

    /// <summary>The traced anchor: five cards in hand, a monster on the centre slot, Aki with three cards and a monster.</summary>
    private static DuelView View(long revision = 1, IReadOnlyList<ChoiceView>? choices = null, DuelPhase phase = DuelPhase.Main1) =>
        new(revision, PlayerId.One, PlayerId.One, 2, phase,
            new(PlayerId.One, 8000, 36, 15, [Card(1), Card(2), Card(3), Card(4), Card(5)],
                [null, null, Card(6) with { Position = BattlePosition.FaceUpAttack }, null, null], new CardView?[5], new CardView?[2], [], []),
            new(PlayerId.Two, 8000, 37, 15, [Hidden(), Hidden(), Hidden()],
                [null, null, Card(7) with { Position = BattlePosition.FaceUpAttack }, null, null], new CardView?[5], new CardView?[2], [], []),
            "", choices ?? [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn)], null);

    private static InputFrame Press(InputAction action) => new(action, action);
    private static InputFrame Hold(InputAction action) => new(InputAction.None, action);

    private static DuelFieldInput Started(DuelView view)
    {
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        return input;
    }

    [Fact]
    public void HandRowWrapsThroughExtraDeckAndDeck()
    {
        var view = View();
        var input = Started(view);
        Assert.Equal((FieldObjectKind.Hand, 4), (input.Focus.Current.Kind, input.Focus.Current.Index));
        var seen = new List<FieldObjectKind>();
        for (int i = 0; i < 7; i++) { input.Handle(view, Press(InputAction.Left), Frame); seen.Add(input.Focus.Current.Kind); }
        Assert.Equal([FieldObjectKind.Hand, FieldObjectKind.Hand, FieldObjectKind.Hand, FieldObjectKind.Hand, FieldObjectKind.Extra,
            FieldObjectKind.Deck, FieldObjectKind.Hand], seen);
    }

    [Fact]
    public void UpKeepsTheColumnAndCyclesThroughSixRows()
    {
        var view = View();
        var input = Started(view);
        var path = new List<(int Row, FieldObjectKind Kind, float X)>();
        for (int i = 0; i < 6; i++) { input.Handle(view, Press(InputAction.Up), Frame); var c = input.Focus.Current; path.Add((c.Row, c.Kind, c.X)); }
        Assert.Equal((DuelFieldRows.OwnSpellTrap, FieldObjectKind.SpellTrap, 105f), path[0]);
        Assert.Equal((DuelFieldRows.OwnMonster, FieldObjectKind.Monster, 105f), path[1]);
        Assert.Equal((DuelFieldRows.OpponentMonster, FieldObjectKind.Monster, 105f), path[2]);
        Assert.Equal((DuelFieldRows.OpponentSpellTrap, FieldObjectKind.SpellTrap, 105f), path[3]);
        Assert.Equal((DuelFieldRows.OpponentTop, FieldObjectKind.Hand, 70f), path[4]);
        Assert.Equal((DuelFieldRows.OwnBottom, FieldObjectKind.Hand, 65f), path[5]);
    }

    [Fact]
    public void MonsterRowIncludesFieldSpellGraveyardAndBanished()
    {
        var kinds = DuelFieldRows.Build(View())[DuelFieldRows.OwnMonster].Select(o => o.Kind).ToArray();
        Assert.Equal([FieldObjectKind.FieldSpell, FieldObjectKind.Monster, FieldObjectKind.Monster, FieldObjectKind.Monster,
            FieldObjectKind.Monster, FieldObjectKind.Monster, FieldObjectKind.Graveyard, FieldObjectKind.Banished], kinds);
    }

    [Fact]
    public void SummonOverSeveralZonesTakesTheCentreAndShowDetailsComesLast()
    {
        ChoiceView[] choices = [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle),
            new(1, "Summon Card 5 [1]", 5, ActionKind.Summon, Zone: 0), new(2, "Summon Card 5 [3]", 5, ActionKind.Summon, Zone: 2),
            new(3, "Set Card 5 [1]", 5, ActionKind.SetMonster, Zone: 0)];
        var view = View(choices: choices);
        var input = Started(view);
        input.Handle(view, Press(InputAction.Confirm), Frame);
        var menu = Assert.IsType<CardMenuModal>(input.Modal);
        Assert.Equal(["Summon", "Set", DuelFieldCommands.ShowDetails], menu.Items.Select(i => i.Label));
        Assert.Equal(new DuelSubmit(1, 2), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void ShowDetailsHostsTheAlbumPageAndBackDrawsOneBlankFrame()
    {
        var view = View();
        var input = Started(view);
        input.DetailFactory = card => new(card.CardId!.Value, new(0, 0, 3), _ => 100);
        var before = input.Focus.Current;
        input.Handle(view, Press(InputAction.Confirm), Frame);
        input.Handle(view, Press(InputAction.Confirm), Frame); // the only verb: Show Details
        var page = Assert.IsType<DetailsModal>(input.Modal).Detail;
        Assert.NotNull(page);
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Equal(TagForceSpecial.PC.App.Screens.Album.CardAlbumDetailTab.Genre, page!.Tab);
        Assert.Null(input.Handle(view, Press(InputAction.Back), Frame));
        Assert.True(page.Closing);
        var blank = DuelScene.Compose(new(view, 0, -1, "", 0, null, null, input), DuelFieldProfile.LoadEmbedded());
        Assert.Empty(blank.Draws);
        input.Handle(view, default, Frame);
        Assert.Null(input.Modal);
        Assert.Equal(before, input.Focus.Current);
    }

    [Fact]
    public void FeedbackUsesTheOriginalMessageWindow()
    {
        var view = View();
        var input = Started(view);
        var field = DuelFieldProfile.LoadEmbedded();
        IReadOnlyList<DuelDraw> Draws() => DuelScene.Compose(new(view, 0, -1, "", 0, Input: input), field).Draws;
        Assert.DoesNotContain(Draws().OfType<DuelRichText>(), t => t.Source.Length == 0);
        input.Rejected("That choice has expired");
        var draws = Draws();
        var text = Assert.Single(draws.OfType<DuelRichText>(), t => t.Source.Length == 0);
        Assert.Equal(("That choice has expired", 217.8f, 50.42f, 11.72f), (text.Argument, text.X, text.Y, text.LinePitch));
        Assert.Contains(draws.OfType<DuelFieldSprites>(), s => s.Piece == "menu.window");
    }

    [Fact]
    public void CrossOnTheFieldOpensThePhaseSelectorThatWrapsAndEnters()    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Back), Frame);
        var selector = Assert.IsType<PhaseModal>(input.Modal);
        Assert.Equal([DuelPhase.Main1, DuelPhase.Battle, DuelPhase.End], selector.Options.Select(o => o.Phase));
        input.Handle(view, Press(InputAction.Left), Frame);
        Assert.Equal(DuelPhase.End, ((PhaseModal)input.Modal!).Options[((PhaseModal)input.Modal!).Index].Phase);
        input.Handle(view, Press(InputAction.Left), Frame);
        Assert.Equal(new DuelSubmit(1, 0), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void ClosingPressNeverReachesTheField()
    {
        var view = View();
        var input = Started(view);
        var before = input.Focus.Current;
        input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.IsType<CardMenuModal>(input.Modal);
        Assert.Null(input.Handle(view, Press(InputAction.Back), Frame));
        Assert.Null(input.Modal); // × closed the menu; it did not also open the phase selector
        Assert.Equal(before, input.Focus.Current);
    }

    [Fact]
    public void RejectedSubmissionClosesTheWindowAndKeepsFocus()
    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Back), Frame);
        input.Handle(view, Press(InputAction.Right), Frame);
        var submit = input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.Equal(1, submit!.Value.Revision);
        var before = input.Focus.Current;
        input.Rejected("That choice has expired");
        Assert.Null(input.Modal);
        Assert.Equal(DuelFeedbackKind.Rejected, input.Feedback.Kind);
        Assert.Equal(before, input.Focus.Current);
    }

    [Fact]
    public void HeldDirectionRepeatsAfterMeasuredDelayAndInterval()
    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Left), Frame);
        int moves = 1, frame = 1;
        var positions = new List<int>();
        var last = input.Focus.Current;
        for (; frame <= 56 + 16 * 2; frame++)
        {
            input.Handle(view, Hold(InputAction.Left), Frame);
            if (!input.Focus.Current.Equals(last)) { moves++; positions.Add(frame); last = input.Focus.Current; }
        }
        Assert.Equal(4, moves);
        Assert.All(positions.Zip(new[] { 56, 72, 88 }), p => Assert.InRange(p.First, p.Second - 1, p.Second + 1));
    }

    [Fact]
    public void MenuRepeatsStopAtTheLastRowButAPressWraps()
    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Menu), Frame);
        input.Handle(view, Press(InputAction.Down), Frame);
        for (int i = 0; i < 200; i++) input.Handle(view, Hold(InputAction.Down), Frame);
        Assert.Equal(3, ((GlobalMenuModal)input.Modal!).Index);
        input.Handle(view, Press(InputAction.Down), Frame);
        Assert.Equal(0, ((GlobalMenuModal)input.Modal!).Index);
    }

    [Fact]
    public void ChainPromptDeclinesWithCross()
    {
        ChoiceView[] choices = [new(0, "Respond zone 1", null, ActionKind.ChainResponse, Zone: 0), new(1, "Pass response", null, ActionKind.ChainPass)];
        var view = View(choices: choices);
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        Assert.IsType<ResponseModal>(input.Modal);
        Assert.Equal(new DuelSubmit(1, 1), input.Handle(view, Press(InputAction.Back), Frame));
    }

    [Fact]
    public void BackFromResponseListReturnsToThePromptAndTheNextBackPasses()
    {
        var view = View(choices: [new(4, "Respond zone 1", null, ActionKind.ChainResponse, Zone: 0),
            new(7, "Respond zone 2", null, ActionKind.ChainResponse, Zone: 1), new(9, "Pass", null, ActionKind.ChainPass)]);
        var input = Started(view);
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.IsType<ListModal>(input.Modal);

        Assert.Null(input.Handle(view, Press(InputAction.Back | InputAction.Confirm), Frame));
        Assert.IsType<ResponseModal>(input.Modal);
        Assert.Equal(new DuelSubmit(1, 9), input.Handle(view, Press(InputAction.Back), Frame));
    }

    [Theory]
    [InlineData(ActionKind.ChooseTarget, "Choose target")]
    [InlineData(ActionKind.ChooseZone, "Choose zone")]
    [InlineData(ActionKind.ChoosePosition, "Choose position")]
    public void RequiredDecisionPagesKeepTheirTokensAcrossNavigation(ActionKind kind, string title)
    {
        var view = View(choices: [.. Enumerable.Range(0, 7).Select(i => new ChoiceView(100 + i, $"Option {i + 1}", Kind: kind))]);
        var input = Started(view);
        Assert.Null(input.Handle(view, Press(InputAction.Back | InputAction.Confirm), Frame));
        Assert.Equal(DuelFeedbackKind.Required, input.Feedback.Kind);

        input.Handle(view, Press(InputAction.Up), Frame); // Wrap to the final page.
        var list = DuelScene.Compose(new(view, 0, -1, "", 0, Input: input));
        Assert.Contains(list.Draws.OfType<DuelText>(), draw => draw.Text == title);
        Assert.Contains(list.Draws.OfType<DuelText>(), draw => draw.Text == "7/7");
        Assert.Equal(new[] { "Option 6", "Option 7" }, list.Draws.OfType<DuelWrapped>().Where(draw => draw.Part == DuelPart.Prompt).Select(draw => draw.Text));
        var original = DuelScene.Compose(new(view, 0, -1, "", 0, Input: input), DuelFieldProfile.LoadEmbedded());
        var menu = Assert.Single(original.Draws.OfType<DuelFieldMenu>());
        Assert.Equal(new[] { "Option 6", "Option 7" }, menu.Rows.Select(row => row.Label));
        Assert.Equal(new DuelSubmit(1, 106), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void RejectedRequiredChoiceReopensTheCurrentOptionsWithoutConsumingThePress()
    {
        var view = View(choices: [new(2, "Choose zone 1", Kind: ActionKind.ChooseZone)]);
        var input = Started(view);
        Assert.Equal(new DuelSubmit(1, 2), input.Handle(view, Press(InputAction.Confirm), Frame));
        input.Rejected("That choice has expired");

        var refreshed = view with { Revision = 2, Choices = [new(6, "Choose zone 3", Kind: ActionKind.ChooseZone)] };
        Assert.Null(input.Handle(refreshed, Press(InputAction.Confirm), Frame));
        Assert.IsType<ListModal>(input.Modal);
        Assert.Equal(DuelFeedbackKind.Rejected, input.Feedback.Kind);
        Assert.Equal(new DuelSubmit(2, 6), input.Handle(refreshed, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void EmptySlotReportsAbsentActionWithoutOpeningAMenu()
    {
        var view = View();
        var input = Started(view);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnMonster).First(slot => slot is { Kind: FieldObjectKind.Monster, Card: null }));
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.Null(input.Modal);
        Assert.Equal(DuelFeedbackKind.Absent, input.Feedback.Kind);
    }

    [Fact]
    public void AuthorizedFieldTargetsUseTheBoardWithoutRequestingHiddenFaces()
    {
        var view = View(choices: [new(4, "Target opponent zone 3", Kind: ActionKind.ChooseTarget)
            { FieldTarget = new(PlayerId.Two, FieldLocation.SpellTrapZone, 2) },
            new(8, "Target own zone 2", Kind: ActionKind.ChooseTarget)
            { FieldTarget = new(PlayerId.One, FieldLocation.MonsterZone, 1) }]);
        view = view with { Opponent = view.Opponent with { SpellTraps = [null, null, Hidden(), null, null] } };
        var input = Started(view);
        var scene = new DuelSceneInput(view, 0, -1, "", 0, Input: input);
        var field = DuelFieldProfile.LoadEmbedded();
        var selected = DuelScene.Focused(scene)!.Value;
        Assert.False(selected.Own);
        Assert.Equal((FieldObjectKind.SpellTrap, 2), (selected.Kind, selected.Index));
        var draws = DuelScene.Compose(scene, field).Draws;
        Assert.DoesNotContain(draws, draw => draw is DuelFieldMenu);
        Assert.Contains(draws.OfType<DuelCardPanel>(), draw => draw.CardId is null);
        Assert.Null(input.Handle(view, Press(InputAction.Right), Frame));
        Assert.Equal(new DuelSubmit(1, 8), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void OutcomeSupersedesAnOpenPrompt()
    {
        var view = View(choices: [new(2, "Choose zone 1", Kind: ActionKind.ChooseZone)]);
        var input = Started(view);
        var outcome = view with { Choices = [], Outcome = new(PlayerId.One, PlayerId.Two, DuelEndReason.LifePointsDepleted) };
        var list = DuelScene.Compose(new(outcome, 0, -1, "An old notice", 0, Input: input), DuelFieldProfile.LoadEmbedded());
        Assert.Contains(list.Draws.OfType<DuelFieldDraw>(), draw => draw.Part == DuelPart.Effects && draw.Piece == "outcome.win");
        Assert.DoesNotContain(list.Draws, draw => draw.Part is DuelPart.Prompt or DuelPart.Focus);
    }

    [Fact]
    public void UnsupportedCommandsSaySoInsteadOfActing()
    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Right), Frame); // Deck
        input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.Equal("Surrender", Assert.Single(((CardMenuModal)input.Modal!).Items).Label);
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.Equal(DuelFeedbackKind.Unsupported, input.Feedback.Kind);
    }

    [Fact]
    public void ANewRevisionDropsMenusBuiltFromOldChoices()
    {
        var view = View();
        var input = Started(view);
        input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.NotNull(input.Modal);
        input.Handle(View(revision: 2), default, Frame);
        Assert.Null(input.Modal);
    }

    [Fact]
    public void PointerRulesMatchTracedPositions()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var view = View();
        var rows = DuelFieldRows.Build(view);
        Vector Pointer(FieldObject o) { var (c, _) = InputFocusOverlay.Place(new(view, []), o); var p = InputFocusOverlay.Pointer(field, o, c); return new(p.X, p.Y); }
        Assert.Equal(new Vector(403, 193), Pointer(rows[DuelFieldRows.OwnBottom][5]));
        Assert.Equal(new Vector(363, 193), Pointer(rows[DuelFieldRows.OwnSpellTrap][4]));
        Assert.Equal(new Vector(454, 190), Pointer(rows[DuelFieldRows.OwnBottom][^1]));
        Assert.Equal(new Vector(337, 37), Pointer(rows[DuelFieldRows.OpponentTop][3]));
        Assert.Equal(new Vector(196, 103), Pointer(rows[DuelFieldRows.OpponentMonster][1]));
    }

    private readonly record struct Vector(float X, float Y);
}
