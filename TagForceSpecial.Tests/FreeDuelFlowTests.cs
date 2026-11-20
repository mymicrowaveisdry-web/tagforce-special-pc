using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Screens;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelFlowTests
{
    [Theory]
    [Trait("Category", "LocalReference")]
    [InlineData(8000, PlayerId.One)]
    [InlineData(4000, PlayerId.Two)]
    public void MainMenuSettingsReachATerminalSupportedDuel(int lifePoints, PlayerId firstPlayer)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var catalog = InstalledGame.FromCatalogDirectory(Path.Combine(root, "analysis/stage13/cache/english-2.1-v1"));
        var host = new Host(catalog);
        var title = new TitleFlow(host, "Tester", instantTransitions: true);

        Tap(title, InputAction.Down);
        Tap(title, InputAction.Confirm);
        Assert.Equal(TitleFlowStep.FreeDuelSettings, title.Step);
        Tap(title, InputAction.Confirm, InputAction.Confirm);
        if (lifePoints == 4000)
            Tap(title, InputAction.Down, InputAction.Confirm, InputAction.Confirm);
        Tap(title, InputAction.Confirm, InputAction.Confirm, InputAction.Confirm);
        Tap(title, InputAction.Right, InputAction.Confirm, InputAction.Confirm);
        title.Update(FreeDuelStartSession.RevealHold);
        Assert.Equal(FreeDuelStartStep.ChooseOrder, title.FreeDuel!.Start!.Step);
        if (firstPlayer == PlayerId.Two) Tap(title, InputAction.Down);

        Tap(title, InputAction.Confirm);
        Assert.Null(host.Duel);
        title.Update(FreeDuelStartSession.IntroHold);
        var duel = Assert.IsType<DuelController>(host.Duel);
        Assert.Equal(firstPlayer, duel.View.ActivePlayer);
        Assert.Equal(lifePoints, duel.View.Self.LifePoints);
        Assert.Equal(lifePoints, duel.View.Opponent.LifePoints);
        title.Update(FreeDuelStartSession.IntroHold);
        Assert.Same(duel, host.Duel);

        for (int i = 0; i < 6000 && duel.View.Outcome is null; i++)
            Assert.True(duel.View.Choices.Count > 0 ? duel.Choose(duel.View.Revision, 0) : duel.StepOpponent(), duel.Notice);
        Assert.NotNull(duel.View.Outcome);
        var replay = duel.ExportReplay(InitialOpponentDecks.ManifestId);
        Assert.Equal(firstPlayer, replay.StartingPlayer);
        Assert.Equal(lifePoints, replay.StartingLifePoints ?? 8000);

        var result = new FreeDuelResultSession();
        GuiButtons confirm = default(GuiButtons) with { Confirm = true };
        result.PointAt(1);
        Assert.Equal(FreeDuelResultAction.None, result.Handle(confirm));
        Assert.Equal(FreeDuelResultAction.Return, result.Handle(confirm));
        title.BeginMainMenu();
        Assert.Equal(TitleFlowStep.MainMenu, title.Step);
        Assert.Equal(0, title.MenuFocus);
    }

    private static void Tap(TitleFlow title, params InputAction[] actions)
    {
        foreach (var action in actions)
        {
            title.Handle(new(action, action));
            title.Handle(new(InputAction.None, InputAction.None));
        }
    }

    private sealed class Host(InstalledGame catalog) : ITitleFlowHost
    {
        public DuelController? Duel { get; private set; }
        public bool ProfilePresent => false;

        public FreeDuelSession OpenFreeDuel() => new(new(44), "Tester", "Deck", random: new OpponentPlaysRock());

        public bool StartFreeDuel(FreeDuelSession session, bool playerGoesFirst)
        {
            Duel = SupportedDuelFactory.Create(catalog, InitialOpponentDecks.Create(0), 37,
                playerGoesFirst ? PlayerId.One : PlayerId.Two, session.Settings.Settings!.StartingLifePoints);
            return true;
        }

        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options) =>
            throw new NotSupportedException();
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) => throw new NotSupportedException();
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) => throw new NotSupportedException();
    }

    private sealed class OpponentPlaysRock : Random
    {
        public override int Next(int maxValue) => (int)FreeDuelHand.Rock;
    }
}
