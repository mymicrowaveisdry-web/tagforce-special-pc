using Godot;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import.Resources;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Host;

/// <summary>Runs the game screens on a fixed 60 Hz update and owns the Godot presentation diagnostics.</summary>
internal sealed partial class GameRoot : Control
{
    private const int SettleFrames = 3, EnteredFrame = 1000;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1.0 / 60);
    private readonly HostOptions options;
    private readonly FixedStepClock clock = new();
    private readonly GodotInput input = new();
    private readonly PspCanvas canvas = new();
    private readonly Queue<(string File, Action Prepare, Func<Image> Grab)> captures = new();
    private MainMenuPresenter menu = null!;
    private TitleFlowPresenter flow = null!;
    private TitlePresenter title = null!;
    private OptionsPresenter optionsScreen = null!;
    private OverlayPresenter overlay = null!;
    private readonly Dictionary<SubmenuKind, SubmenuPresenter> submenus = [];
    private MainMenuLayers layers = null!;
    private AlbumPilot? album;
    private FieldPilot? field;
    private TitlePresentationSmoke? presentation;
    private TitleRoute? route;
    private TitleRouteView? routeView;
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
        if (options.PresentationSmoke && options.FlowCaptureRoot is not null && DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("--capture-flow requires a rendering display. A headless run can write the trace.");
        if (options.PresentationSmoke && !options.ReferenceFlow)
        {
            presentation = new TitlePresentationSmoke(options.PresentationTracePath, options.FlowCaptureRoot);
            canvas.IntegerScaling = true;
            canvas.AddScreen(presentation.View);
            return;
        }

        string root = options.DataRoot ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial");
        var store = new GameDataStore(root);
        var setup = store.Reload();
        if (!setup.Success)
            throw new InvalidOperationException(setup.Issue?.Message ?? "Import game data from the Workbench first.");
        input.Bindings = store.Preferences.Bindings();
        canvas.IntegerScaling = store.Preferences.IntegerScaling;
        if (!options.PresentationSmoke && options.DuelFieldReferenceRoot is null)
        {
            string name = System.Environment.UserName.Trim();
            route = new TitleRoute(new TitleProfileHost(new PlayerProfileStore(store.Storage)), name.Length == 0 ? "Duelist" : name);
            routeView = new TitleRouteView(route, input.Bindings);
            canvas.AddScreen(routeView);
            return;
        }
        if (store.Game?.Presentation is not { } registry)
            throw new InvalidOperationException("The import has no presentation package. Import again from the Workbench.");
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
        var flowFont = new PgfGlyphAtlas(font, GameFonts.MenuSha256);
        var flowFrames = new TitleFlowFrames(profile, layers, text, flowFont, TitleFlowPresenter.FontKey);
        foreach (string key in flowFrames.TextureKeys) if (profile.EbootTextures.All(e => e.Key != key)) textures.AddImported(key);
        flow = new TitleFlowPresenter(flowFrames, textures, flowFont) { Visible = false };
        canvas.AddScreen(flow);
        var titleLayers = new TitleLayers(TitleFrame.LoadEmbedded());
        foreach (string key in titleLayers.TextureKeys) textures.AddImported(key);
        title = new TitlePresenter(titleLayers, flowFrames, textures, flowFont) { Visible = false };
        canvas.AddScreen(title);
        var optionsLayers = new OptionsLayers(OptionsFrameProfile.LoadEmbedded(), profile, layers);
        foreach (string key in optionsLayers.TextureKeys) if (profile.EbootTextures.All(e => e.Key != key)) textures.AddImported(key);
        optionsScreen = new OptionsPresenter(optionsLayers, textures, text, new PgfGlyphAtlas(font, GameFonts.MenuSha256)) { Visible = false };
        canvas.AddScreen(optionsScreen);
        overlay = new OverlayPresenter { Visible = false };
        canvas.AddScreen(overlay);
        foreach (var kind in Enum.GetValues<SubmenuKind>())
        {
            var presenter = new SubmenuPresenter(kind, new SubmenuLayers(SubmenuProfile.LoadEmbedded(kind)), textures, text,
                new PgfGlyphAtlas(font, GameFonts.MenuSha256), "pgf.submenu." + kind) { Visible = false };
            submenus.Add(kind, presenter);
            canvas.AddScreen(presenter);
        }
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
        else if (what == NotificationPredelete)
        {
            presentation?.Dispose();
            album?.Dispose();
            field?.Dispose();
        }
    }

    public override void _Process(double delta)
    {
        if (presentation is { } scripted) { RunPresentation(scripted); return; }
        if (smoke) { RunSmoke(); return; }
        if (route is null || routeView is null) return;
        for (int step = clock.Advance(delta); step > 0; step--)
        {
            input.TextEntry = route.Screen == TitleScreenKind.Flow && route.Flow.Editing;
            UpdateTitle(route, routeView, input.Next(focused, canvas.TakePointer()));
        }
        routeView.QueueRedraw();
    }

    private void RunPresentation(TitlePresentationSmoke scripted)
    {
        try
        {
            if (scripted.Advance(canvas, Step)) GetTree().Quit(0);
        }
        catch (Exception error) when (PrivateStorage.Recoverable(error))
        {
            GD.PrintErr("PRESENTATION_SMOKE_FAILED " + error.Message);
            GetTree().Quit(1);
        }
    }

    private static void UpdateTitle(TitleRoute route, TitleRouteView view, InputFrame input)
    {
        bool saving = route.Screen == TitleScreenKind.Flow && route.Flow.Step == TitleFlowStep.SystemSavePrompt;
        try { route.Tick(view.ApplyPointer(input), Step); }
        catch (Exception error) when (saving && PrivateStorage.Recoverable(error))
        {
            GD.PrintErr($"Could not save the player profile: {error.Message}");
            view.SaveError = "Could not save. Try again or cancel.";
        }
        if (route.Flow.Step != TitleFlowStep.SystemSavePrompt) view.SaveError = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (!focused || route is not { Screen: TitleScreenKind.Flow, Fade.Active: false } || !route.Flow.Editing) return;
        if (@event is InputEventKey { Pressed: true } key) GodotInput.TypeName(key, route.Flow);
    }

    /// <summary>Exact frames as the MonoGame host's pixel-exact smoke fixes them: Main Menu loop frame 0, entry finished, that frame's pulse;
    /// the album's first view with the hand on right pocket 0; then forward-turn frames from the first view.</summary>
    private void PlanSmoke()
    {
        smoke = true;
        // Flow frames as the MonoGame smoke first reached them: default name, the quit question on its dump's own instant; the level frame
        // follows the confidence question answered with its second button (Been There).
        string name = System.Environment.UserName is { Length: > TitleFlow.NameLimit } longName ? longName[..TitleFlow.NameLimit] : System.Environment.UserName;
        const DuelistLevelChoice First = DuelistLevelChoice.FirstTimer;
        (string File, string Frame, int Button, int Menu, DuelistLevelChoice Level)[] flowStates =
        [
            ("StorageCheck-0-0", "storage", 0, 0, First), ("NoData-0-0", "no-data", 0, 0, First), ("CancelLoadNoData-0-0", "cancel-load.black", 0, 0, First),
            ("CancelLoadNoData-1-0", "cancel-load.black", 1, 0, First), ("NameEntry-0-0", "entry.name", 0, 0, First), ("RulesQuestion-0-0", "entry.rules", 0, 0, First),
            ("RulesQuestion-1-0", "entry.rules", 1, 0, First), ("ConfidenceQuestion-1-0", "entry.confidence", 1, 0, First),
            ("DuelistLevel-0-0", "entry.level", 0, 0, DuelistLevelChoice.BeenThere),
            ("CreateQuestion-0-0", "entry.create", 0, 0, First), ("CreateQuestion-1-0", "entry.create", 1, 0, First),
            ("QuitQuestion-0-0", TitleFlowFrames.QuitFrame, 0, 0, First), ("QuitQuestion-0-1", TitleFlowFrames.QuitFrame, 0, 1, First),
            ("QuitQuestion-1-0", TitleFlowFrames.QuitFrame, 1, 0, First), ("QuitQuestion-1-1", TitleFlowFrames.QuitFrame, 1, 1, First)
        ];
        void Only(params Control[] shown) { foreach (var s in new Control[] { menu, flow, title, optionsScreen, overlay }.Concat(submenus.Values)) s.Visible = shown.Contains(s); }
        captures.Enqueue(("title.png", () => { Only(title); title.Show(false); }, canvas.Capture));
        foreach (int choice in new[] { 0, 1 })
            captures.Enqueue(($"start-{choice}.png", () => { Only(title); title.Show(true, choice); }, canvas.Capture));
        foreach (var state in flowStates)
            captures.Enqueue(($"{state.File}.png", () => { Only(flow); flow.Show(state.Frame, state.Button, state.Menu, name, state.Level); }, canvas.Capture));
        // System-style screens: the keyboard with the default name; the save prompt and completion from each place the route opens them, all before
        // a profile exists.
        captures.Enqueue(("NameKeyboard-0-0.png", () => { Only(overlay); overlay.Show(TitleFlowOverlay.Lines(TitleFlowStep.NameKeyboard, name, 0, false)); }, canvas.Capture));
        foreach (int row in new[] { 0, 6, 7 })
            foreach (var step in new[] { TitleFlowStep.SystemSavePrompt, TitleFlowStep.SystemSaveDone })
                captures.Enqueue(($"{step}-0-{row}.png", () => { Only(overlay); overlay.Show(TitleFlowOverlay.Lines(step, name, 0, false)); }, canvas.Capture));
        for (int row = 0; row < MainMenuLayers.Rows; row++)
        {
            int r = row;
            captures.Enqueue(($"MainMenu-0-{r}.png", () => { Only(menu); menu.ShowNotice(null); menu.Show(r, 0, EnteredFrame, layers.Pulse(0)); }, canvas.Capture));
        }
        // Rows the PC does not recreate show a PC notice over the focused menu.
        foreach (int row in new[] { 0, 2, 3 })
        {
            int r = row;
            captures.Enqueue(($"MainMenuUnavailable-0-{r}.png", () =>
            {
                Only(menu); menu.Show(r, 0, EnteredFrame, layers.Pulse(0)); menu.ShowNotice(MainMenuDestinations.All[r]);
            }, canvas.Capture));
        }
        // Submenu lists in exact runs: the submenu at its ready frame over the Main Menu dimmed (no focus) on its own row.
        foreach (var (kind, menuRow, label) in new[] { (SubmenuKind.Deck, 4, "DeckMenu"), (SubmenuKind.Database, 5, "DatabaseMenu") })
        {
            var profile = SubmenuProfile.LoadEmbedded(kind);
            for (int focus = 0; focus < profile.Rows.Length; focus++)
            {
                int f = focus;
                var submenu = submenus[kind];
                captures.Enqueue(($"{label}-L-{f}-{menuRow}.png", () =>
                {
                    Only(menu, submenu);
                    menu.ShowNotice(null); menu.Show(menuRow, 0, EnteredFrame, layers.Pulse(0), profile.MenuDim);
                    submenu.ShowNotice(null); submenu.Show(f, profile.ReadyFrame, focusVisible: true);
                }, canvas.Capture));
            }
        }
        // Submenu rows the PC does not recreate: the PC notice over the focused submenu.
        foreach (var (kind, menuRow, label, rows) in new[] { (SubmenuKind.Deck, 4, "DeckMenu", new[] { 1 }), (SubmenuKind.Database, 5, "DatabaseMenu", new[] { 0, 2, 3, 4, 5, 6 }) })
        {
            var profile = SubmenuProfile.LoadEmbedded(kind);
            foreach (int focus in rows)
            {
                int f = focus;
                var submenu = submenus[kind];
                captures.Enqueue(($"{label}-N-{f}-{menuRow}.png", () =>
                {
                    Only(menu, submenu);
                    menu.ShowNotice(null); menu.Show(menuRow, 0, EnteredFrame, layers.Pulse(0), profile.MenuDim);
                    submenu.Show(f, profile.ReadyFrame, focusVisible: true); submenu.ShowNotice(Submenus.Destinations(kind)[f]);
                }, canvas.Capture));
            }
        }
        PlanOptions(Only);
        if (album is not { } pilot) return;
        captures.Enqueue(("CardAlbum-0-Pocket-R-0-0.png", () => pilot.Show(0, true, 0), pilot.Layer.Capture));
        foreach (int frames in new[] { 4, 10, 16, 22 })
            captures.Enqueue(($"godot-CardAlbum-turnR-0-f{frames:00}.png", () => pilot.Show(0, true, 0, new CardAlbumTurn(1, 0, frames)), pilot.Layer.Capture));
    }

    /// <summary>Options frames as the MonoGame smoke first reached them from the defaults (Duelist Level Been There): Main Menu loop frame 0 with
    /// the entry finished, the band's pulse counted from that frame's entry.</summary>
    private void PlanOptions(Action<Control[]> only)
    {
        var motion = layers.Motion.Profile;
        int pulse = OptionsFrameProfile.LoadEmbedded().PulseAt((motion.LoopFrames - motion.EntryAnimationFrame) % motion.LoopFrames);
        int[] Values(params (int Row, int Value)[] changes)
        {
            int[] values = OptionsCatalog.Defaults(DuelistLevelChoice.BeenThere);
            foreach (var (row, value) in changes) values[row] = value;
            return values;
        }
        void List(string file, int focus, int[] values, int? cancel = null) => captures.Enqueue(($"{file}.png", () =>
        {
            only([optionsScreen]); optionsScreen.ShowList(values, focus, 0, EnteredFrame, pulse, cancel);
        }, canvas.Capture));
        List("Options-L-0-0", 0, Values());
        List("Options-Q0-0-0", 0, Values(), 0); List("Options-Q1-0-0", 0, Values(), 1);
        List("Options-L-0-1", 0, Values((0, 1))); List("Options-L-0-2", 0, Values((0, 2)));
        List("Options-L-1-15", 1, Values()); List("Options-L-1-14", 1, Values((1, 14)));
        for (int row = 2; row < OptionsCatalog.Rows.Count; row++) List($"Options-L-{row}-{Values()[row]}", row, Values());
        List("Options-Q0-12-1", 12, Values(), 0); List("Options-Q1-12-1", 12, Values(), 1);
        foreach (int focus in new[] { 0, 1 })
            captures.Enqueue(($"OptionsSaveQuestion-{focus}-6.png", () => { only([optionsScreen]); optionsScreen.ShowSaveQuestion(focus); }, canvas.Capture));
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
