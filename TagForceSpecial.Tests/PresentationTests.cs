using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.PC.DuelUI.Input;
using Microsoft.Xna.Framework.Input;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

public sealed class PresentationTests
{
    [Fact]
    public void PreviewUsesTheRealRulesWithoutPublishingStateOrEvents()
    {
        var e = Engine();
        var before = e.State;
        Assert.Equal(DuelError.None, e.CheckCommand(new ResolveDrawStep(PlayerId.One)));
        Assert.Equal(DuelError.NotActivePlayer, e.CheckCommand(new ResolveDrawStep(PlayerId.Two)));
        Assert.Same(before, e.State);
        Assert.Equal(0, e.State.Revision);
        Ok(e, new ResolveDrawStep(PlayerId.One));
        Assert.Equal(1, e.State.Revision);
    }

    [Fact]
    public void PreviewOfSummonAndChainResolutionCannotMoveCardsOrLifePoints()
    {
        var e = Engine(); EnterMain(e);
        var before = e.State;
        int card = e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId;
        Assert.Equal(DuelError.None, e.CheckCommand(new NormalSummon(PlayerId.One, card, 0)));
        Assert.Same(before, e.State);
        Assert.Null(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);

        var spellDeck = new uint[] { 4352, 4012, 4030, 4032, 4033 }.Concat(Enumerable.Repeat(4012u, 35));
        e = new(Catalog(), PlayablePreview.Rules, spellDeck, Enumerable.Repeat(4012u, 40),
            effectDefinitions: PlayablePreview.Definitions);
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        Ok(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId));
        Ok(e, new PassChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId));
        before = e.State;
        Assert.Equal(DuelError.None, e.CheckCommand(new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId)));
        Assert.Same(before, e.State);
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void HiddenHandsAndSetCardsHaveNoIdentityOrDescription()
    {
        var catalog = Catalog(); var e = Engine(); EnterMain(e);
        Ok(e, new SetMonster(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 0));
        var owner = new DuelController(e, catalog, PlayerId.One).View;
        var observer = new DuelController(e, catalog, PlayerId.Two).View;
        Assert.False(owner.Self.Monsters[0]!.Hidden);
        Assert.All(observer.Opponent.Hand, Hidden);
        Hidden(observer.Opponent.Monsters[0]!);
        Assert.Empty(observer.Choices);
        Assert.All(observer.Self.Hand, c => Assert.NotNull(c.CardId));
        Assert.Equal(35, observer.Opponent.DeckCount);

        static void Hidden(CardView c)
        {
            Assert.True(c.Hidden); Assert.Null(c.CardId); Assert.Null(c.InstanceId);
            Assert.Null(c.Attack); Assert.Null(c.Defense); Assert.Empty(c.Description);
            Assert.Equal("Hidden card", c.Name);
        }
    }

    [Fact]
    public void SetSpellIdentityIsHiddenUntilItBecomesPublic()
    {
        var e = new DuelEngine(Catalog(), PlayablePreview.Rules,
            Enumerable.Repeat(4352u, 40), Enumerable.Repeat(4012u, 40));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 2));
        var view = new DuelController(e, Catalog(), PlayerId.Two).View;
        Assert.True(view.Opponent.SpellTraps[2]!.Hidden);
        Assert.Null(view.Opponent.SpellTraps[2]!.CardId);
    }

    [Fact]
    public void ChoicesAreLegalAndRevisionBoundAndOldViewsStayImmutable()
    {
        var e = Engine(); var controller = new DuelController(e, Catalog());
        var old = controller.View;
        Assert.Single(old.Choices);
        Assert.False(controller.Choose(old.Revision, -1));
        Assert.True(controller.Choose(old.Revision, 0));
        long revision = controller.View.Revision;
        Assert.False(controller.Choose(old.Revision, 0));
        Assert.Equal(revision, controller.View.Revision);
        Assert.Equal(DuelPhase.Draw, old.Phase);
        Assert.Equal(DuelPhase.Standby, controller.View.Phase);
        Assert.Throws<NotSupportedException>(() => ((IList<ChoiceView>)old.Choices).Clear());
    }

    [Fact]
    public void ExternalEngineAdvanceInvalidatesPreviouslyOfferedChoice()
    {
        var e = Engine(); var controller = new DuelController(e, Catalog());
        Ok(e, new ResolveDrawStep(PlayerId.One));
        Assert.False(controller.Choose(0, 0));
        Assert.Equal(1, e.State.Revision);
    }

    [Fact]
    public void ControllerExposesOnlyTheDecisionOwnersAuthorizedChoices()
    {
        var e = new DuelEngine(Catalog(), PlayablePreview.Rules,
            Enumerable.Repeat(4012u, 40), Enumerable.Repeat(4012u, 40),
            decisionSequences: [new("select", [DecisionStep.Position([BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense])], false)]);
        EnterMain(e); Ok(e, new BeginDecisionSequence(PlayerId.One, "select"));
        var owner = new DuelController(e, Catalog());
        Assert.Equal(2, owner.View.Choices.Count);
        Assert.Empty(new DuelController(e, Catalog(), PlayerId.Two).View.Choices);
        Assert.True(owner.Choose(owner.View.Revision, 1));
        Assert.Null(e.State.PendingDecision);
    }

    [Fact]
    public void RealPreviewControllerCompletesDeterministicallyAndTerminalChoicesAreEmpty()
    {
        var a = Play(); var b = Play();
        Assert.Equal(a, b);

        static (long Revision, DuelOutcome Outcome) Play()
        {
            var catalog = Catalog();
            var controller = new DuelController(PlayablePreview.CreateEngine(catalog,
                PlayablePreview.ExampleDeck, PlayablePreview.ExampleDeck), catalog);
            for (int step = 0; step < 1000 && controller.View.Outcome is null; step++)
            {
                var v = controller.View;
                Assert.True(v.Choices.Count > 0 ? controller.Choose(v.Revision, 0) : controller.StepOpponent());
                Assert.All(controller.View.Opponent.Hand, c => Assert.Null(c.CardId));
            }
            Assert.NotNull(controller.View.Outcome);
            Assert.Empty(controller.View.Choices);
            Assert.False(controller.StepOpponent());
            Assert.False(controller.Choose(controller.View.Revision, 0));
            return (controller.View.Revision, controller.View.Outcome!);
        }
    }

    [Theory]
    [InlineData(960, 544, false, 0, 0, 960, 544)]
    [InlineData(1920, 1080, false, 7, 0, 1905, 1080)]
    [InlineData(1000, 800, true, 20, 128, 960, 544)]
    [InlineData(240, 136, true, 0, 0, 240, 136)]
    [InlineData(0, 0, true, 0, 0, 0, 0)]
    public void ViewportPreservesTheCanvasWithLetterboxing(int width, int height, bool integer,
        int x, int y, int w, int h) => Assert.Equal(new CanvasRect(x, y, w, h), PspViewport.Fit(width, height, integer));

    [Fact]
    public void PointerMappingRejectsLetterboxesAndMapsBothCanvasEdges()
    {
        var target = PspViewport.Fit(1000, 800, true);
        Assert.Null(PspViewport.ToCanvas(0, 0, target));
        Assert.Equal(new CanvasPoint(0, 0), PspViewport.ToCanvas(20, 128, target));
        Assert.Equal(new CanvasPoint(479.5f, 271.5f), PspViewport.ToCanvas(979, 671, target));
        Assert.Null(PspViewport.ToCanvas(980, 672, target));
    }

    [Fact]
    public void InputRequiresNewPressAndSuppressesFocusRecoveryKeys()
    {
        var input = new GuiInput();
        var none = new KeyboardState();
        var enter = new KeyboardState(Keys.Enter);
        Assert.False(input.Poll(none, default, true).Confirm);
        Assert.True(input.Poll(enter, default, true).Confirm);
        Assert.False(input.Poll(enter, default, true).Confirm);
        input.Poll(none, default, false);
        Assert.False(input.Poll(enter, default, true).Confirm);
        input.Poll(none, default, true);
        Assert.True(input.Poll(enter, default, true).Confirm);
    }

    [Fact]
    public void GamepadUsesTheSameConfirmAndNavigationActions()
    {
        var input = new GuiInput();
        input.Poll(default, default, true);
        var pad = new GamePadState(default(GamePadThumbSticks), default(GamePadTriggers),
            new GamePadButtons(Buttons.A), new GamePadDPad(ButtonState.Pressed, ButtonState.Released,
                ButtonState.Released, ButtonState.Released));
        var action = input.Poll(default, pad, true);
        Assert.True(action.Confirm);
        Assert.True(action.Up);
        Assert.False(input.Poll(default, pad, true).Confirm);
    }

    [Fact]
    public void HostRejectsMissingOrUnknownArguments()
    {
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--cache"]));
        Assert.Throws<ArgumentException>(() => HostOptions.Parse(["--anything"]));
        Assert.True(HostOptions.Parse(["--pixel-exact", "--smoke"]).PixelExact);
    }

    private static DuelEngine Engine() => new(Catalog(), PlayablePreview.Rules,
        Enumerable.Repeat(4012u, 40), Enumerable.Repeat(4030u, 40));
}
