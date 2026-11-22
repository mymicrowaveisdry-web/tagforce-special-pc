using Godot;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import.Resources;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Host;

/// <summary>The Godot game: opens the validated import, then runs screens on the fixed 60 Hz update the MonoGame host used. Phase 4 pilots:
/// the Main Menu is entered directly; <c>--presentation-smoke --capture-flow</c> saves its eight exact focus frames, the Card Album's first
/// view and page-turn frames under the MonoGame host's file names (turn frames are Godot-only evidence) for comparison.</summary>
internal sealed partial class GameRoot : Control
{
    private const int SettleFrames = 3, EnteredFrame = 1000;
    private readonly HostOptions options;
    private readonly FixedStepClock clock = new();
    private readonly GodotInput input = new();
    private readonly PspCanvas canvas = new();
    private readonly Queue<(string File, Action Prepare, Func<Image> Grab)> captures = new();
    private MainMenuPresenter menu = null!;
    private MainMenuLayers layers = null!;
    private AlbumPilot? album;
    private FieldPilot? field;
    private int focus;
    private long menuSteps;
    private bool focused = true, smoke;
    private int smokeWait;

    public GameRoot(HostOptions options)
    {
        this.options = options;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(canvas);
    }

    /// <summary>Opens the import and builds the screens. Called before the node enters the tree so failures reach the caller
    /// (Godot only logs exceptions thrown from engine callbacks).</summary>
    public void Load()
    {
        string root = options.DataRoot ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial");
        var store = new GameDataStore(root);
        var setup = store.Reload();
        if (!setup.Success || store.Game?.Presentation is not { } registry)
            throw new InvalidOperationException(setup.Issue?.Message ?? "The import has no presentation package. Import again from the Workbench.");
        input.Bindings = store.Preferences.Bindings();
        canvas.IntegerScaling = store.Preferences.IntegerScaling;
        string fontPath = store.Storage.PathFor(GameFonts.MenuFile);
        if (!File.Exists(fontPath)) throw new InvalidOperationException("PSP fonts are missing. Import again from the Workbench.");
        byte[] font = File.ReadAllBytes(fontPath);
        var assets = new ScreenAssets(registry);
        var profile = FlowProfile.LoadEmbedded();
        layers = new MainMenuLayers(profile, MainMenuAnimation.LoadEmbedded());
        var textures = new FlowTextures(assets, assets.Bytes(TitleFlowReferenceResources.EbootKey));
        foreach (var embedded in profile.EbootTextures) textures.AddEmbedded(embedded);
        foreach (string key in layers.TextureKeys) if (profile.EbootTextures.All(e => e.Key != key)) textures.AddImported(key);
        var text = new FlowTextSources().Add("eboot", assets.Bytes(TitleFlowReferenceResources.EbootKey))
            .Add("title-menu", assets.Bytes(TitleFlowReferenceResources.TitleMenuKey));
        menu = new MainMenuPresenter(layers, textures, text, new PgfGlyphAtlas(font, GameFonts.MenuSha256));
        canvas.AddScreen(menu);
        album = AlbumPilot.TryCreate(store.Storage, assets, textures, text, store.Game.Catalog.Catalog, font);
        if (options.PresentationSmoke) PlanSmoke();
        if (options.DuelFieldReferenceRoot is { } fieldRoot) PlanField(fieldRoot, new FieldPilot(textures, assets));
    }

    /// <summary>Renders every exported field draw list in <paramref name="root"/> to <c>&lt;state&gt;.godot.png</c> beside it.</summary>
    private void PlanField(string root, FieldPilot pilot)
    {
        field = pilot;
        smoke = true;
        foreach (string list in Directory.EnumerateFiles(root, "*" + FieldPilot.ListSuffix).Order(StringComparer.Ordinal))
        {
            string name = System.IO.Path.GetFileName(list)[..^FieldPilot.ListSuffix.Length];
            captures.Enqueue((System.IO.Path.Combine(root, name + ".godot.png"), () => pilot.Show(list), pilot.Layer.Capture));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) focused = true;
        else if (what == NotificationApplicationFocusOut) focused = false;
        else if (what == NotificationPredelete) { album?.Dispose(); field?.Dispose(); }
    }

    public override void _Process(double delta)
    {
        if (smoke) { RunSmoke(); return; }
        for (int step = clock.Advance(delta); step > 0; step--) Update(input.Next(focused, canvas.TakePointer()));
        int entry = MainMenuEntry.FramesIn(TimeSpan.FromSeconds(menuSteps / 60.0));
        var motion = layers.Motion.Profile;
        int frame = (motion.EntryAnimationFrame + entry) % motion.LoopFrames;
        menu.Show(focus, frame, entry, layers.Pulse(frame));
    }

    private void Update(InputFrame frame)
    {
        menuSteps++;
        if (!MainMenuEntry.FocusVisible(MainMenuEntry.FramesIn(TimeSpan.FromSeconds(menuSteps / 60.0)))) return;
        frame = PointerTargets.Apply(frame, PointerTargets.Find(frame.Pointer.Position, menu.BarRects()), focus, row => focus = row);
        if (frame.IsPressed(InputAction.Up)) focus = Math.Max(0, focus - 1);
        if (frame.IsPressed(InputAction.Down)) focus = Math.Min(MainMenuLayers.Rows - 1, focus + 1);
    }

    /// <summary>Exact frames as the MonoGame host's pixel-exact smoke fixes them: Main Menu loop frame 0, entry finished, that frame's pulse;
    /// the album's first view with the hand on right pocket 0; then forward-turn frames from the first view.</summary>
    private void PlanSmoke()
    {
        smoke = true;
        for (int row = 0; row < MainMenuLayers.Rows; row++)
        {
            int r = row;
            captures.Enqueue(($"MainMenu-0-{r}.png", () => menu.Show(r, 0, EnteredFrame, layers.Pulse(0)), canvas.Capture));
        }
        if (album is not { } pilot) return;
        captures.Enqueue(("CardAlbum-0-Pocket-R-0-0.png", () => pilot.Show(0, true, 0), pilot.Layer.Capture));
        foreach (int frames in new[] { 4, 10, 16, 22 })
            captures.Enqueue(($"godot-CardAlbum-turnR-0-f{frames:00}.png", () => pilot.Show(0, true, 0, new CardAlbumTurn(1, 0, frames)), pilot.Layer.Capture));
    }

    private void RunSmoke()
    {
        if (!captures.TryPeek(out var next))
        {
            GD.Print($"PRESENTATION_SMOKE godot-pilot album={album is not null}");
            GetTree().Quit(0);
            return;
        }
        if (smokeWait == 0) next.Prepare();
        if (++smokeWait < SettleFrames) return;
        string file = System.IO.Path.IsPathRooted(next.File) ? next.File : System.IO.Path.Combine(
            options.FlowCaptureRoot ?? throw new InvalidOperationException("--presentation-smoke needs --capture-flow in the Godot pilot."), next.File);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        if (!File.Exists(file)) next.Grab().SavePng(file);
        smokeWait = 0;
        captures.Dequeue();
    }
}
