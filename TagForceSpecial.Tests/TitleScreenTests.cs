using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

public class TitleScreenTests
{
    [Theory]
    [InlineData(InputAction.Start)]
    [InlineData(InputAction.Confirm)]
    public void StartAndDesktopConfirmReachStartChoicesWithoutLoadingGame(InputAction action)
    {
        using var router = Create(out var title, out var choices, out var calls);
        router.GoTo(title.ScreenId);
        router.Tick(new(action, action), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Equal(choices.ScreenId, router.CurrentScreenId);
        Assert.Empty(calls);
    }

    [Fact]
    public void UnsupportedTitleInputDoesNothingAndCircleCancelsChoices()
    {
        using var router = Create(out var title, out var choices, out var calls);
        router.GoTo(title.ScreenId);
        router.Tick(new(InputAction.Switch | InputAction.Menu | InputAction.Up | InputAction.Back | InputAction.Select, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(title.ScreenId, router.CurrentScreenId);
        router.GoTo(choices.ScreenId);
        router.Tick(new(InputAction.Back | InputAction.Confirm, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(title.ScreenId, router.CurrentScreenId);
        Assert.Empty(calls);
    }

    [Fact]
    public void ExistingSaveChoicesDefaultToContinueAndNewGameUsesItsOwnApplicationIntent()
    {
        using var router = Create(out _, out var choices, out var calls);
        router.GoTo(choices.ScreenId);
        router.Tick(new(InputAction.Confirm, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal("continue", Assert.Single(calls));
        router.Tick(new(InputAction.Up, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        router.Tick(new(InputAction.Confirm, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(new[] { "continue", "new-game" }, calls);
    }

    [Fact]
    public void FrozenAndAdvancingPresentationClocksDoNotChangeNavigationOutcome()
    {
        foreach (var time in new[] { TimeSpan.Zero, TimeSpan.FromHours(2) })
        {
            using var router = Create(out var title, out var choices, out var calls);
            router.GoTo(title.ScreenId);
            router.Tick(new(InputAction.Start, InputAction.None), TimeSpan.FromSeconds(1), time);
            router.Tick(new(InputAction.Confirm, InputAction.None), TimeSpan.FromSeconds(1), time);
            Assert.Equal(choices.ScreenId, router.CurrentScreenId);
            Assert.Equal("continue", Assert.Single(calls));
        }
    }

    [Fact]
    public void NoSaveDefaultsToNewGameButContinueRemainsSelectable()
    {
        var calls = new List<string>(); TitlePresentationState state = default;
        using var router = new ScreenRouter(nav => new(new(), nav, () => new(false), _ => { }, () => null));
        router.Register(new TitleScreen("original.title-start-menu", true, s => state = s, () => { },
            () => calls.Add("continue-no-data"), () => calls.Add("new-game"), savePresent: false));
        router.GoTo("original.title-start-menu");
        Assert.Equal(0, state.Choice);
        router.Tick(new(InputAction.Down, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        router.Tick(new(InputAction.Confirm, InputAction.None), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(1, state.Choice);
        Assert.Equal("continue-no-data", Assert.Single(calls));
    }

    private static ScreenRouter Create(out TitleScreen title, out TitleScreen choices, out List<string> calls)
    {
        var intents = new List<string>(); calls = intents;
        var router = new ScreenRouter(nav => new(new(), nav, () => new(false), _ => { }, () => null));
        title = new("original.title", false, _ => { }, () => { }, () => intents.Add("continue"), () => intents.Add("new-game"));
        choices = new("original.title-start-menu", true, _ => { }, () => { }, () => intents.Add("continue"), () => intents.Add("new-game"));
        router.Register(title); router.Register(choices);
        return router;
    }
}
