using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.Tests;

public sealed class PresentationInfrastructureTests
{
    private static ScreenRouter Router() => new(nav => new(new(), nav, () => new(false), _ => { }, () => null));
    private static readonly PspInputFrame Confirm = new(PspAction.Cross, PspAction.Cross);

    [Fact]
    public void ModalOwnsInputAndSimulationAndRestoresParentFocus()
    {
        using var router = Router();
        var duel = new Probe("duel") { Focus = "choice-7" };
        var modal = new Probe("choice-dialog");
        router.Register(duel); router.Register(modal); router.GoTo("duel");
        router.PushModal("choice-dialog");
        duel.Focus = "disturbed";
        modal.Input = _ => router.PopModal();
        router.Tick(Confirm, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        Assert.Equal(0, duel.InputCount); Assert.Equal(0, duel.Updates);
        Assert.Equal("choice-7", duel.Focus); Assert.Equal("duel", router.CurrentScreenId);
        Assert.Equal(1, modal.Exits); Assert.Equal(0, router.ModalDepth);
        router.Tick(Confirm, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        Assert.Equal(1, duel.InputCount); Assert.Equal(1, duel.Updates);
    }

    [Fact]
    public void TransitionCannotDeliverConfirmOrSimulationToNewScreen()
    {
        using var router = Router();
        var title = new Probe("original.title"); var menu = new Probe("original.main-menu");
        router.Register(title); router.Register(menu); router.GoTo(title.ScreenId);
        title.Input = _ => router.GoTo(menu.ScreenId);
        router.Tick(Confirm, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        Assert.Equal(menu.ScreenId, router.CurrentScreenId);
        Assert.Equal(0, menu.InputCount); Assert.Equal(0, menu.Updates); Assert.Equal(1, title.Exits);
    }

    [Fact]
    public void HundredNavigationCyclesBalanceLifetimesAndKeepFixedTimeDrawsIdentical()
    {
        var router = Router();
        var title = new Probe("original.title"); var menu = new Probe("original.main-menu"); var options = new Probe("original.options");
        Probe[] screens = [title, menu, options];
        foreach (var screen in screens) router.Register(screen);
        router.GoTo(title.ScreenId);
        var captures = new Dictionary<string, string>();
        for (int cycle = 0; cycle < 100; cycle++)
        {
            foreach (var next in new[] { menu, options, menu, title })
            {
                router.GoTo(next.ScreenId);
                router.Draw(new(null!, TimeSpan.FromSeconds(2)));
                if (captures.TryGetValue(next.ScreenId, out var expected)) Assert.Equal(expected, next.LastDraw);
                captures[next.ScreenId] = next.LastDraw;
                Assert.Equal(1, screens.Count(s => s.Active));
                Assert.All(screens, s => Assert.Equal(s.Active ? 1 : 0, s.Enters - s.Exits));
            }
        }
        router.Dispose();
        Assert.All(screens, s => Assert.Equal(s.Enters, s.Exits));
    }

    [Fact]
    public void UnknownNavigationKeepsCurrentScreenAndDuplicateModalIsRejected()
    {
        using var router = Router(); var screen = new Probe("title");
        router.Register(screen); router.GoTo("title");
        Assert.Throws<KeyNotFoundException>(() => router.GoTo("missing"));
        Assert.Equal("title", router.CurrentScreenId); Assert.Equal(0, screen.Exits);
        Assert.Throws<InvalidOperationException>(() => router.PushModal("title"));
    }

    [Fact]
    public void KeyboardAndPadShareAnActionEdgeAndRefocusDoesNotConfirm()
    {
        var mapper = new PspInputMapper();
        mapper.Poll(new(), new(), true);
        Assert.True(mapper.Poll(new(Keys.Enter), new(), true).IsPressed(PspAction.Cross));
        var heldPad = new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A);
        Assert.False(mapper.Poll(new(Keys.Enter), heldPad, true).IsPressed(PspAction.Cross));
        mapper.Poll(new(), heldPad, false);
        Assert.Equal(PspAction.None, mapper.Poll(new(Keys.Enter), heldPad, true).Pressed);
        mapper.Poll(new(), new(), true);
        Assert.True(mapper.Poll(new(), heldPad, true).IsPressed(PspAction.Cross));
    }

    [Fact]
    public void SpriteAspectAndPendingMeasurementsAreEnforced()
    {
        var region = new SpriteRegion(new(10, 20, 100, 50), SpriteSizing.PreserveAspect);
        region.Validate(256, 256);
        Assert.Equal(new Rectangle(0, 25, 100, 50), region.Fit(new(0, 0, 100, 100)));
        Assert.Throws<InvalidDataException>(() => region.Validate(32, 32));
        Assert.Throws<InvalidDataException>(() => new SpriteRegion(new(0, 0, 10, 10), SpriteSizing.MeasuredStretch).Fit(new(0, 0, 50, 50)));
        var layouts = new OriginalLayoutRegistry();
        layouts.Register("menu", new("original.main-menu", new(0, 0, 480, 272), null));
        Assert.Throws<InvalidDataException>(() => layouts.Get("menu"));
        Assert.True(layouts.Get("menu", requireMeasured: false).Pending);
    }

    [Fact]
    public void PresentationEffectsUseSuppliedTimeAndCancelOnOwnerExit()
    {
        var animation = new AnimationScheduler(); float value = -1;
        animation.Schedule("title", "fade", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, 1, x => value = x);
        animation.Update(TimeSpan.FromSeconds(1)); Assert.Equal(.5f, value);
        animation.Update(TimeSpan.FromSeconds(1)); Assert.Equal(.5f, value);
        animation.CancelOwner("title"); animation.Update(TimeSpan.FromSeconds(2)); Assert.Equal(.5f, value);
        var silentAudio = new AudioCuePlayer(); silentAudio.Enqueue(new("title", "confirm")); silentAudio.Update();
        Assert.Equal(0, silentAudio.PendingCount);
        var heard = new List<AudioCue>(); var audio = new AudioCuePlayer(heard.Add);
        audio.Enqueue(new("title", "confirm")); audio.Enqueue(new("menu", "cursor")); audio.CancelOwner("title"); audio.Update();
        Assert.Equal(new AudioCue("menu", "cursor"), Assert.Single(heard));
    }

    private sealed class Probe(string id) : IGameScreen, IScreenFocus
    {
        public string ScreenId => id;
        public string? Focus { get; set; }
        public Action<PspInputFrame>? Input { get; set; }
        public int InputCount, Updates, Enters, Exits;
        public bool Active;
        public string LastDraw = "";
        public void OnEnter(ScreenContext context, ScreenArguments arguments) { Active = true; Enters++; }
        public void HandleInput(PspInputFrame input) { InputCount++; Input?.Invoke(input); }
        public void Update(TimeSpan simulationDelta, TimeSpan presentationTime) => Updates++;
        public void Draw(ScreenDrawContext draw) => LastDraw = $"{id}/{Focus}/{draw.PresentationTime.Ticks}";
        public void OnExit() { Active = false; Exits++; }
        public string? CaptureFocus() => Focus;
        public void RestoreFocus(string? focus) => Focus = focus;
    }
}
