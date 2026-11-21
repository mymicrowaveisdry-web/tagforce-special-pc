using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>The matched Normal Summon walk (native order33-walk, Koumori Dragon to the centre zone on turn 1): the measured presentation
/// timeline, the turn start and the field details it showed.</summary>
public sealed class NormalSummonPresentationTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 59.94);
    private static TimeSpan V(double vblanks) => TimeSpan.FromSeconds(vblanks / DuelFieldPresentation.VblankHz);
    private static InputFrame Press(InputAction a) => new(a, a);
    private static CardView Card(int instance, uint id, BattlePosition? position = null) => new(instance, id, "Card " + id, "", 1500, 1200, position, false);
    private static readonly CardView[] Hand = [Card(1, 4010), Card(2, 4013), Card(3, 4037), Card(4, 4009), Card(5, 4042)];

    private static PlayerView Self(IReadOnlyList<CardView> hand, CardView?[] monsters) =>
        new(PlayerId.One, 8000, 37, 0, hand, monsters, new CardView?[5], new CardView?[2], [], []);
    private static readonly PlayerView Opponent = new(PlayerId.Two, 8000, 37, 0,
        [.. Enumerable.Repeat(new CardView(null, null, "Hidden card", "", null, null, null, true), 5)], new CardView?[5], new CardView?[5], new CardView?[2], [], []);

    private static DuelView MainPhase(long revision, params ChoiceView[] choices) =>
        new(revision, PlayerId.One, PlayerId.One, 1, DuelPhase.Main1, Self(Hand, new CardView?[5]), Opponent, "", [.. choices, new ChoiceView(99, "End turn", Kind: ActionKind.EndTurn)], null);

    private static (DuelView Before, DuelView After) Summon()
    {
        var before = MainPhase(2, new ChoiceView(7, "Summon Card 4037 [3]", 3, ActionKind.Summon, Zone: 2));
        var monsters = new CardView?[5];
        monsters[2] = Card(3, 4037, BattlePosition.FaceUpAttack);
        return (before, before with { Revision = 3, Self = Self([Hand[0], Hand[1], Hand[3], Hand[4]], monsters), Choices = [] });
    }

    [Fact]
    public void NormalSummonFollowsTheMeasuredTimeline()
    {
        var (before, after) = Summon();
        var presentation = new DuelFieldPresentation(TimeSpan.Zero, enabled: true);
        var menu = new CardMenuModal(new(DuelFieldRows.OwnBottom, FieldObjectKind.Hand, true, 2, 0, Hand[2]),
            [new("Summon", new SubmitCommand(7), MenuVerb.Summon)], 0);
        var t0 = TimeSpan.FromSeconds(10);
        presentation.Accept(before, after, 7, t0, menu);
        DuelFieldFrame At(double v) => presentation.Frame(after, t0 + V(v));

        Assert.NotNull(At(25).ClosingMenu);
        Assert.Null(At(27).ClosingMenu);
        Assert.False(At(31).PointerHidden);
        Assert.True(At(33).PointerHidden);
        Assert.Equal((new Vector3(0, 215, 430), false), (At(47).SummonWorld!.Value.Translation, At(47).Flying));
        Assert.Equal((new Vector3(0, 215, 430), true), (At(49).SummonWorld!.Value.Translation, At(49).Flying));
        var mid = At(51).SummonWorld!.Value.Translation; // native (0, 104.74, 269.86)
        Assert.Equal(104.74f, mid.Y, 0.1f);
        Assert.Equal(269.86f, mid.Z, 0.1f);
        Assert.Equal(DuelFieldPresentation.MidProgress, At(51).HandProgress);
        Assert.Equal((null, 0), (At(53).SummonWorld, At(53).GlowFrame));
        Assert.Equal(11, At(75).GlowFrame);
        Assert.Null(At(77).GlowFrame);
        Assert.True(presentation.Busy(after, t0 + V(87)));
        Assert.False(presentation.Busy(after, t0 + V(89)));
        Assert.Equal(default, At(89));
    }

    [Fact]
    public void WithoutAnimationsTheSummonIsNotPresented()
    {
        var (before, after) = Summon();
        var presentation = new DuelFieldPresentation(TimeSpan.Zero, enabled: false);
        presentation.Accept(before, after, 7, TimeSpan.Zero);
        Assert.Equal(default, presentation.Frame(after, V(30)));
        Assert.False(presentation.Busy(after, V(30)));
    }

    [Fact]
    public void TheTurnStartsInMainPhaseOneOnTheCentreSpellTrapZoneAndDownReachesTheMiddleHandCard()
    {
        var standby = MainPhase(1, new ChoiceView(4, "Phase: Main1", Kind: ActionKind.Phase, Phase: DuelPhase.Main1),
            new ChoiceView(5, "End turn", Kind: ActionKind.EndTurn)) with { Phase = DuelPhase.Standby };
        var input = new DuelFieldInput();
        Assert.Equal(new DuelSubmit(1, 4), input.Handle(standby, default, Frame));
        var main = MainPhase(2, new ChoiceView(7, "Summon Card 4037 [3]", 3, ActionKind.Summon, Zone: 2));
        input.Handle(main, default, Frame);
        Assert.Equal((FieldObjectKind.SpellTrap, 2), (input.Focus.Current.Kind, input.Focus.Current.Index));
        input.Handle(main, Press(InputAction.Down), Frame);
        Assert.Equal((FieldObjectKind.Hand, 4037u), (input.Focus.Current.Kind, input.Focus.Current.Card!.CardId!.Value));
    }

    [Fact]
    public void AfterTheSummonTheLeftPanelKeepsTheSummonedCardUntilTheNextPress()
    {
        var (before, after) = Summon();
        var input = new DuelFieldInput();
        input.Handle(before, default, Frame);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnBottom).First(o => o.Card?.InstanceId == 3));
        input.Handle(before, Press(InputAction.Confirm), Frame);
        Assert.Equal(new DuelSubmit(2, 7), input.Handle(before, Press(InputAction.Confirm), Frame));
        input.Handle(after, default, Frame);
        Assert.Equal(4037u, input.HeldInspection?.CardId);
        input.Handle(after, Press(InputAction.Right), Frame);
        Assert.Null(input.HeldInspection);
    }

    [Fact]
    public void AFocusedHandCardShowsItsCommandIconsAndTheHudShowsHelp()
    {
        var main = MainPhase(2, new ChoiceView(7, "Summon Card 4037 [3]", 3, ActionKind.Summon, Zone: 2),
            new ChoiceView(8, "Set Card 4037 [3]", 3, ActionKind.SetMonster, Zone: 2));
        var input = new DuelFieldInput();
        input.Handle(main, default, Frame);
        input.Focus.FocusOn(input.Focus.Row(DuelFieldRows.OwnBottom).First(o => o.Card?.InstanceId == 3));
        var draws = DuelScene.Compose(new(main, 0, -1, "", 0, Input: input), DuelFieldProfile.LoadEmbedded()).Draws.ToList();
        var icons = draws.OfType<DuelFieldSprites>().Where(s => s.Piece == "menu.icon" && s.Part == DuelPart.Focus).ToList();
        Assert.Equal(2, icons.Count);
        Assert.Equal(14, icons[1].Vertices[0][0] - icons[0].Vertices[0][0]);
        Assert.Contains(draws.OfType<DuelRichText>(), t => t.Offset == DuelHud.HelpOffset);
    }
}
