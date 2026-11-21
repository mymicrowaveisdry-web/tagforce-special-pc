using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>The original decision screens (Order 3.2(d), Gusto Gulldo's Deck summon): the card list in selection mode and the battle
/// position screen.</summary>
public sealed class DecisionScreenTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 59.94);
    private static InputFrame Press(InputAction a) => new(a, a);
    private static CardView Card(int instance, uint id) => new(instance, id, "Card " + id, "", 600, 1400, null, false);

    private static DuelView View(params ChoiceView[] choices) => new(1, PlayerId.One, PlayerId.One, 3, DuelPhase.Main1,
        new(PlayerId.One, 7500, 30, 15, [], new CardView?[5], new CardView?[5], new CardView?[2], [], []),
        new(PlayerId.Two, 8000, 30, 15, [], new CardView?[5], new CardView?[5], new CardView?[2], [], []), "", choices, null);

    private static DuelFieldInput Started(DuelView view)
    {
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        return input;
    }

    [Fact]
    public void CardChoicesOpenTheSelectionListWhereCircleMarksThenOkSubmits()
    {
        var view = View([.. Enumerable.Range(0, 3).Select(i => new ChoiceView(40 + i, "Choose", 200 + i, ActionKind.ChooseCard)
            { Subject = Card(200 + i, (uint)(9000 + i)), SubjectSource = DecisionCardSource.Deck })]);
        var input = Started(view);
        var list = Assert.IsType<CardListModal>(input.Modal);
        Assert.NotNull(list.Options);
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Null(input.Handle(view, Press(InputAction.Back), Frame)); // × does nothing (mandatory)
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame)); // ○ marks and moves to OK
        Assert.Equal((1, true), (((CardListModal)input.Modal!).Marked!.Value, ((CardListModal)input.Modal!).OnClose));
        Assert.Equal(new DuelSubmit(1, 41), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void OkWithoutAMarkDoesNothing()
    {
        var view = View(new ChoiceView(40, "Choose", 200, ActionKind.ChooseCard) { Subject = Card(200, 9000) });
        var input = Started(view);
        input.Handle(view, Press(InputAction.Down), Frame);
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.IsType<CardListModal>(input.Modal);
    }

    [Fact]
    public void AttackAndDefenseChoicesOpenThePositionScreenWithAttackFirst()
    {
        var card = Card(200, 9000);
        var view = View(new ChoiceView(51, "FaceUpDefense", Kind: ActionKind.ChoosePosition, Position: BattlePosition.FaceUpDefense) { Subject = card },
            new ChoiceView(50, "FaceUpAttack", Kind: ActionKind.ChoosePosition, Position: BattlePosition.FaceUpAttack) { Subject = card });
        var input = Started(view);
        var position = Assert.IsType<PositionModal>(input.Modal);
        Assert.Equal(BattlePosition.FaceUpAttack, position.Choices[0].Position);
        var draws = DuelScene.Compose(new(view, 0, -1, "", 0, Input: input), DuelFieldProfile.LoadEmbedded()).Draws.OfType<DuelFieldDraw>().ToList();
        Assert.Equal(2, draws.Count(d => d.Piece.StartsWith("position.face", StringComparison.Ordinal) && d.Placement.Texture == CardSpriteRenderer.FaceKey(9000)));
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Equal(new DuelSubmit(1, 51), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    private static (DuelView View, DuelFieldInput Input) Tribute(params ChoiceView[] tributes)
    {
        var monsters = new CardView?[5];
        monsters[1] = Card(5, 9164) with { Position = BattlePosition.FaceUpAttack };
        monsters[2] = Card(6, 9382) with { Position = BattlePosition.FaceUpAttack };
        var view = View([.. tributes, new ChoiceView(90, "End turn", Kind: ActionKind.EndTurn)]);
        view = view with { Self = view.Self with { Hand = [Card(1, 9167)], Monsters = monsters } };
        var input = Started(view);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnBottom).First(slot => slot.Card?.InstanceId == 1));
        input.Handle(view, Press(InputAction.Confirm), Frame); // card menu
        input.Handle(view, Press(InputAction.Confirm), Frame); // Summon
        return (view, input);
    }

    private static ChoiceView TributeChoice(int id, int mask) =>
        new(id, "Tribute Summon Card 9167 - use zone", 1, ActionKind.Summon, TargetZone: 2000 + mask, Zone: System.Numerics.BitOperations.TrailingZeroCount(mask));

    [Fact]
    public void TributeSummonAsksFirstThenSelectsTheMonsterOnTheField()
    {
        var (view, input) = Tribute(TributeChoice(40, 0b100), TributeChoice(41, 0b010));
        var prompt = Assert.IsType<TributeConfirmModal>(input.Modal);
        Assert.Equal(0, prompt.Index); // YES selected
        var draw = Assert.Single(DuelScene.Compose(new(view, 0, -1, "", 0, Input: input), DuelFieldProfile.LoadEmbedded()).Draws.OfType<DuelChainPrompt>());
        Assert.Equal(("1", 1188230, 85f), (draw.CardName, draw.Layout.MessageOffset, draw.Layout.Top));
        input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.Equal(1, Assert.IsType<TributeModal>(input.Modal).Zone); // leftmost eligible monster
        Assert.Equal(1, input.Focus.Current.Index);
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Equal((FieldObjectKind.Monster, 2), (input.Focus.Current.Kind, input.Focus.Current.Index));
        Assert.Equal(new DuelSubmit(1, 40), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void NoClosesTheTributePromptWithoutSubmitting()
    {
        var (view, input) = Tribute(TributeChoice(40, 0b100));
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.Null(input.Modal);
    }

    [Fact]
    public void TwoTributesSubmitOnceBothAreMarked()
    {
        var (view, input) = Tribute(TributeChoice(40, 0b110));
        Assert.Equal(2, TributeSelection.Count(Assert.IsType<TributeConfirmModal>(input.Modal).Choices));
        input.Handle(view, Press(InputAction.Confirm), Frame);
        Assert.Null(input.Handle(view, Press(InputAction.Confirm), Frame));
        Assert.Equal(0b010, Assert.IsType<TributeModal>(input.Modal).Marked);
        input.Handle(view, Press(InputAction.Right), Frame);
        Assert.Equal(new DuelSubmit(1, 40), input.Handle(view, Press(InputAction.Confirm), Frame));
    }

    [Fact]
    public void OtherPositionSetsKeepTheList()
    {
        var view = View(new ChoiceView(1, "A", Kind: ActionKind.ChoosePosition, Position: BattlePosition.FaceUpAttack),
            new ChoiceView(2, "B", Kind: ActionKind.ChoosePosition, Position: BattlePosition.FaceDownDefense));
        Assert.IsType<ListModal>(Started(view).Modal);
    }
}
