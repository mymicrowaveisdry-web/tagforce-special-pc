using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>Native field cases (order34-closure): the Draw prompt, zone order with occupied zones, wide hands and the cursor after an
/// attack.</summary>
public sealed class FieldClosureTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 59.94);
    private static InputFrame Press(InputAction a) => new(a, a);
    private static CardView Card(int instance, uint id, BattlePosition? position = null) => new(instance, id, "Card " + id, "", 1500, 1200, position, false);

    private static DuelView View(long revision, int turn, DuelPhase phase, IReadOnlyList<CardView> hand, CardView?[] monsters, CardView?[] opponent,
        params ChoiceView[] choices) =>
        new(revision, PlayerId.One, PlayerId.One, turn, phase, new(PlayerId.One, 7900, 35, 0, hand, monsters, new CardView?[5], new CardView?[2], [], []),
            new(PlayerId.Two, 8000, 35, 0, [], opponent, new CardView?[5], new CardView?[2], [], []), "", choices, null);

    [Fact]
    public void LaterDrawPhasesWaitOnTheDecksDrawMenuAndDownThenReachesTheDrawnCard()
    {
        CardView[] hand = [Card(1, 4010), Card(2, 4013), Card(3, 4009), Card(4, 4042)];
        var draw = View(5, 3, DuelPhase.Draw, hand, new CardView?[5], new CardView?[5], new ChoiceView(9, "Draw / begin turn", Kind: ActionKind.Draw));
        var input = new DuelFieldInput();
        Assert.Null(input.Handle(draw, default, Frame));
        var menu = Assert.IsType<CardMenuModal>(input.Modal);
        Assert.Equal((FieldObjectKind.Deck, MenuVerb.Draw), (input.Focus.Current.Kind, menu.Items.Single().Verb));
        Assert.Null(input.Handle(draw, Press(InputAction.Back), Frame)); // mandatory: × does nothing (inferred)
        Assert.Equal(new DuelSubmit(5, 9), input.Handle(draw, Press(InputAction.Confirm), Frame));

        var main = View(7, 3, DuelPhase.Main1, [.. hand, Card(5, 4031)], new CardView?[5], new CardView?[5],
            new ChoiceView(10, "End turn", Kind: ActionKind.EndTurn));
        input.Handle(main, default, Frame);
        Assert.Equal(FieldObjectKind.Deck, input.Focus.Current.Kind); // the cursor stays on the Deck after drawing
        input.Handle(main, Press(InputAction.Down), Frame);
        Assert.Equal(4031u, input.Focus.Current.Card?.CardId);
    }

    [Fact]
    public void TheDuelsFirstTurnStillDrawsByItself()
    {
        var draw = View(0, 1, DuelPhase.Draw, [Card(1, 4010)], new CardView?[5], new CardView?[5], new ChoiceView(9, "Draw / begin turn", Kind: ActionKind.Draw));
        Assert.Equal(new DuelSubmit(0, 9), new DuelFieldInput().Handle(draw, default, Frame));
    }

    [Fact]
    public void SummonsFillTheCentreThenRightThenLeftOfCentre()
    {
        var card = Card(1, 4038);
        ChoiceView Zone(int z) => new(20 + z, $"Summon Card 4038 [{z + 1}]", 1, ActionKind.Summon, Zone: z);
        var monsters = new CardView?[5];
        monsters[2] = Card(9, 4031, BattlePosition.FaceUpAttack);
        var view = View(3, 5, DuelPhase.Main1, [card], monsters, new CardView?[5], Zone(0), Zone(1), Zone(3), Zone(4));
        var target = new FieldObject(DuelFieldRows.OwnBottom, FieldObjectKind.Hand, true, 0, 0, card);
        var summon = DuelFieldCommands.ObjectMenu(view, target).First(i => i.Verb == MenuVerb.Summon);
        Assert.Equal(new SubmitCommand(23), summon.Command);
        view = view with { Choices = [Zone(0), Zone(1), Zone(4)] };
        Assert.Equal(new SubmitCommand(21), DuelFieldCommands.ObjectMenu(view, target).First(i => i.Verb == MenuVerb.Summon).Command);
    }

    [Theory]
    [InlineData(5, -130f, 130f)]
    [InlineData(6, -163f, 162f)]
    [InlineData(7, -195f, 195f)]
    public void HandsUpToSevenCardsKeepTheFullPitch(int count, float first, float last)
    {
        Assert.Equal(first, DuelLayoutProfile.HandWorld(0, count, false).Translation.X);
        Assert.Equal(last, DuelLayoutProfile.HandWorld(count - 1, count, false).Translation.X);
    }

    [Fact]
    public void TheOpeningHandsAreDealtCardByCardAtTheMeasuredVblanks()
    {
        CardView[] hand = [Card(1, 4010), Card(2, 4013), Card(3, 4037), Card(4, 4009), Card(5, 4042)];
        var view = View(0, 1, DuelPhase.Draw, hand, new CardView?[5], new CardView?[5], new ChoiceView(9, "Draw / begin turn", Kind: ActionKind.Draw));
        var presentation = new DuelFieldPresentation(TimeSpan.Zero, enabled: true);
        (bool, int, int) At(double v)
        {
            var f = presentation.Frame(view, TimeSpan.FromSeconds((v + 0.5) / DuelFieldPresentation.VblankHz));
            return (f.BeforeDeal, f.BeforeDeal ? f.DealtSelf : -1, f.BeforeDeal ? f.DealtOpponent : -1);
        }
        Assert.Equal((true, 0, 0), At(263));
        Assert.Equal((true, 1, 0), At(264));
        Assert.Equal((true, 5, 0), At(280));
        Assert.Equal((true, 5, 1), At(284));
        Assert.Equal((true, 5, 4), At(298));
        Assert.Equal((false, -1, -1), At(300));
        var dealt = DuelFieldPresentation.BeforeOpeningDeal(view, 2, 0);
        Assert.Equal((2, 35 + 3), (dealt.Self.Hand.Count, dealt.Self.DeckCount));
    }

    [Fact]
    public void TheOutcomeWaitsForCircleBeforeTheResultScreen()
    {
        var presentation = new DuelFieldPresentation(TimeSpan.Zero, enabled: true);
        Assert.False(presentation.ResultReady(TimeSpan.FromSeconds(30)));
        presentation.OutcomeInput(false);
        Assert.False(presentation.ResultReady(TimeSpan.FromSeconds(31)));
        presentation.OutcomeInput(true);
        Assert.True(presentation.ResultReady(TimeSpan.FromSeconds(31)));
        Assert.True(new DuelFieldPresentation(TimeSpan.Zero, enabled: false).ResultReady(TimeSpan.Zero));
    }

    [Fact]
    public void AfterAnAttackTheCursorMovesToAMonsterThatCanStillAttack()
    {
        var monsters = new CardView?[5];
        monsters[2] = Card(1, 4031, BattlePosition.FaceUpAttack);
        monsters[3] = Card(2, 4038, BattlePosition.FaceUpAttack);
        var opponent = new CardView?[5];
        opponent[2] = Card(7, 9448, BattlePosition.FaceUpDefense);
        ChoiceView Attack(int id, int attacker) => new(id, "Attack", attacker, ActionKind.Attack, TargetZone: 2);
        var input = new DuelFieldInput();
        var before = View(10, 5, DuelPhase.Main1, [], monsters, opponent, new ChoiceView(1, "Phase: Battle", Kind: ActionKind.Phase, Phase: DuelPhase.Battle));
        input.Handle(before, default, Frame);
        var battle = View(11, 5, DuelPhase.Battle, [], monsters, opponent, Attack(30, 1), Attack(31, 2), new ChoiceView(32, "End turn", Kind: ActionKind.EndTurn));
        input.Handle(battle, default, Frame);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnMonster).First(o => o.Card?.InstanceId == 2));
        var after = battle with { Revision = 13, Opponent = battle.Opponent with { Monsters = new CardView?[5] },
            Choices = [new ChoiceView(40, "Attack directly", 1, ActionKind.Attack), new ChoiceView(41, "End turn", Kind: ActionKind.EndTurn)] };
        input.Handle(after, default, Frame);
        Assert.Equal(1, input.Focus.Current.Card?.InstanceId);
    }
}
