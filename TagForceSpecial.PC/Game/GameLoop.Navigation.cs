using Microsoft.Xna.Framework;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.PC.Screens;
using TagForceSpecial.PC.Setup;
using TagForceSpecial.PC.UI;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private ScreenRouter? router;
    private GumPresentation gum = null!;
    private OriginalScreenAssetResolver screenAssets = null!;
    private OriginalTitleVisuals? originalTitle;
    private TitlePresentationState titleState;
    private ScreenId utilityReturn = ScreenId.Title;
    private readonly AudioCuePlayer audioCues = new();
    private readonly AnimationScheduler animations = new();
    private readonly Dictionary<ScreenId, int> savedFocus = [];
    private ScreenId activeScreen;
    private FreeDuelResultSession? freeDuelResult;
    private GameTime navigationTime = new();
    private TimeSpan PresentationTime => options.PixelExact ? TimeSpan.Zero : navigationTime.TotalGameTime;
    private ScreenId screen
    {
        get => activeScreen;
        set
        {
            if (router is null) { activeScreen = value; return; }
            if (value == ScreenId.Title && originalTitle is null) value = ScreenId.PcTools;
            savedFocus[activeScreen] = menuIndex;
            if (value == ScreenId.Paused) router.PushModal("duel.pause");
            else if (value == ScreenId.Duel && activeScreen == ScreenId.Paused) router.PopModal();
            else router.GoTo(ScreenName(value));
        }
    }
    private static string ScreenName(ScreenId id) => id switch
    {
        ScreenId.Title => "original.title", ScreenId.PcTools => "pc.tools-menu",
        ScreenId.TitleStartMenu => "original.title-start-menu",
        ScreenId.TitleFlow => "original.title-flow",
        ScreenId.Preferences => "pc.preferences", ScreenId.DeckEditor => "pc.deck-editor",
        ScreenId.DeckSelection => "pc.quick-duel", ScreenId.SavedDecks => "pc.saved-decks",
        ScreenId.AssetDiagnostics => "pc.asset-diagnostics",
        ScreenId.Controls => "pc.controls", ScreenId.Duel => "duel.field",
        ScreenId.Result => "duel.result", ScreenId.Paused => "duel.pause",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    private void InitializeRouting()
    {
        gum = new(GraphicsDevice);
        // The importer's reader already validated the presentation package; absent data keeps the authored UI usable.
        var registry = setup?.Game?.Presentation;
        if (setup?.Game is not null && registry is null) Console.Error.WriteLine("TITLE_PRESENTATION_PENDING no validated presentation output");
        screenAssets = new(GraphicsDevice, registry);
        if (registry is not null)
        {
            try
            {
                originalTitle = new(GraphicsDevice, screenAssets); gum.BindTitleLayers(originalTitle);
                // The title and its flow need the same validated generation; never show a partial original route.
                if (!TryInitializeTitleFlow()) { originalTitle.Dispose(); originalTitle = null; }
            }
            catch (Exception e) when (e is InvalidDataException or KeyNotFoundException)
            { originalTitle?.Dispose(); originalTitle = null; setupMessage = "Original title assets need a validated title presentation import."; Console.Error.WriteLine("TITLE_PRESENTATION_PENDING " + e.Message); }
        }
        router = new(nav => new(screenAssets, nav, () => new(setup?.Preferences.IntegerScaling == true),
            audioCues.Enqueue, () => duel?.View));
        RegisterTitle(ScreenId.Title, false);
        RegisterTitle(ScreenId.TitleStartMenu, true);
        router.Register(new TitleFlowScreen(this));
        Register(ScreenId.PcTools, UpdatePcTools, () => gum.BindMenu("NativeToolsMenu",
            ["DECKS", "QUICK DUEL", "PREFERENCES", "CONTROLS", "EXIT"], menuIndex), "NativeToolsMenu",
            pointer: Rows(at => gum.HitIndex("NativeToolsMenu", "Row", 5, at)));
        Register(ScreenId.Preferences, b => RecoverSetup(() => UpdatePreferences(b)), DrawPreferences, "NativePreferences",
            pointer: Rows(at => gum.HitIndex("NativePreferences", "Row", 4, at)));
        Register(ScreenId.DeckEditor, b => RecoverSetup(() => UpdateEditor(b)), DrawEditor, pointer: EditorPointer);
        Register(ScreenId.SavedDecks, b => RecoverSetup(() => UpdateSavedDecks(b)), DrawSetupLists,
            pointer: Rows(at => TextRow(at, 12, 55, 22, editor?.SavedDecks.Count ?? 0, menuIndex / 8 * 8, 8)));
        Register(ScreenId.AssetDiagnostics, UpdateAssetDiagnostics, DrawAssetDiagnostics, pointer: f => PointerTargets.Clicks(f));
        Register(ScreenId.Controls, _ => { }, DrawControls, raw: UpdateControls);
        Register(ScreenId.DeckSelection, UpdateFreeDuel, DrawFreeDuel,
            pointer: Rows(at => TextRow(at, 40, 84, 23, InitialOpponentDecks.Names.Count, 0)));
        Register(ScreenId.Duel, _ => { }, DrawDuel, update: UpdateDuelPresentation, raw: UpdateDuelInput);
        Register(ScreenId.Result, UpdateDuelResult, DrawDuelResult, pointer: DuelResultPointer);
        Register(ScreenId.Paused, UpdatePause, DrawPause, pointer: Rows(at => TextRow(at, 40, 84, 23, 2, 0)));
        if (options.PresentationSmoke && originalTitle is null)
            throw new InvalidOperationException("Presentation smoke requires the validated Phase 3 title generation in --data-root.");
        if (options.TitleLayerCaptureRoot is { } layerRoot)
            (originalTitle ?? throw new InvalidOperationException("Title layer capture requires a validated title generation.")).ExportReviewLayers(layerRoot);
        if (options.DuelFieldReferenceRoot is { } fieldRoot)
        {
            var fieldVisuals = DuelField() ?? throw new InvalidOperationException("Duel field reference capture requires the imported field textures.");
            var game = setup?.Game ?? throw new InvalidOperationException("Duel field reference capture requires the imported card catalog.");
            DuelFieldReference.Capture(fieldVisuals, fieldRoot, FieldCardFacts());
            DuelFieldReference.CaptureInputWalk(GraphicsDevice, renderer, fieldVisuals, fieldRoot, FieldCardFacts());
            DuelFieldReference.CapturePanels(GraphicsDevice, renderer, fieldVisuals, fieldRoot, FieldCardFacts());
            DuelFieldReference.CaptureDetail(GraphicsDevice, renderer, fieldVisuals, fieldRoot, FieldCardFacts(), DuelCardDetail);
            DuelFieldReference.CaptureAttack(GraphicsDevice, renderer, fieldVisuals, fieldRoot, game.Catalog.Catalog);
            DuelFieldReference.CaptureAttack(GraphicsDevice, renderer, fieldVisuals, fieldRoot, game.Catalog.Catalog, 1, 3, "attack-off-centre.png");
            DuelFieldPresentationReference.Capture(GraphicsDevice, renderer, fieldVisuals, fieldRoot, game);
            DuelFieldReference.CaptureDecisions(GraphicsDevice, renderer, fieldVisuals, fieldRoot, FieldCardFacts());
            DuelFieldReference.CaptureCardList(GraphicsDevice, renderer, fieldVisuals, fieldRoot, game.Catalog.Catalog, FieldCardFacts());
            Console.WriteLine("DUEL_FIELD_REFERENCE " + DuelFieldReference.States.Count); Exit(); return;
        }
        var initial = activeScreen == ScreenId.Title && originalTitle is null ? ScreenId.PcTools : activeScreen;
        router.GoTo(ScreenName(initial));
    }
    private void TryActivateImportedTitle()
    {
        if (originalTitle is not null || setup is null) return;
        OriginalScreenAssetResolver? nextAssets = null;
        OriginalTitleVisuals? nextTitle = null;
        try
        {
            var registry = setup.Game?.Presentation;
            if (registry is null) return;
            nextAssets = new(GraphicsDevice, registry);
            nextTitle = new(GraphicsDevice, nextAssets);
            gum.BindTitleLayers(nextTitle);
            var previousAssets = screenAssets;
            screenAssets = nextAssets;
            if (!TryInitializeTitleFlow()) { screenAssets = previousAssets; nextTitle.Dispose(); nextAssets.Dispose(); return; }
            previousAssets.Dispose(); originalTitle = nextTitle;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e) || e is KeyNotFoundException)
        {
            Console.Error.WriteLine("TITLE_ACTIVATION_FAILED " + e.Message);
            nextTitle?.Dispose(); nextAssets?.Dispose(); setupMessage = "Game data is ready; original title artwork still needs a validated import.";
        }
    }
    private void RegisterTitle(ScreenId id, bool choices)
    {
        string name = choices ? "OriginalTitleStartMenu" : "OriginalTitle";
        router!.Register(new TitleScreen(ScreenName(id), choices,
            state => { activeScreen = id; titleState = state; gum.Show(name, true); if (choices) flowVisuals?.Render("start-menu", menuFocus: state.Choice); },
            () => gum.Show(name, false),
            // Measured: Continue reaches a black storage screen with no fade-in; New Game fades into its first screen.
            () => ChangeBehindFade(() => BeginFlow(flow => flow.BeginContinue()), fadeIn: false),
            () => ChangeBehindFade(() => BeginFlow(flow => flow.BeginNewGame()), fadeIn: true),
            savePresentNow: () => ProfilePresent, hitChoice: choices ? StartMenuRow : null));
    }
    /// <param name="pointer">Turns the mouse into key actions for this screen (see <see cref="PointerTargets"/>); null ignores it.</param>
    /// <param name="raw">Replaces the button handler with one that reads the whole frame (the Controls screen captures keys).</param>
    /// <summary>Start menu rows from the measured start-menu frame: each row's plain bar pieces offset to the row.</summary>
    private int? StartMenuRow(Rendering.CanvasPoint? at)
    {
        if (flowVisuals?.DialogFrame("start-menu").Menu is not { } menu) return null;
        float x0 = menu.Plain.Min(q => q.X0), x1 = menu.Plain.Max(q => q.X1), y0 = menu.Plain.Min(q => q.Y0), y1 = menu.Plain.Max(q => q.Y1);
        return PointerTargets.Find(at, menu.Rows.Select(r => new[] { x0, y0 + r.Y, x1, y1 + r.Y }).ToArray());
    }

    private void Register(ScreenId id, Action<GuiButtons> inputHandler, Action draw, string? gumName = null, Action? update = null,
        Func<InputFrame, InputFrame>? pointer = null, Action<InputFrame>? raw = null)
    {
        Action<InputFrame> handle = raw ?? (frame => inputHandler((pointer?.Invoke(frame) ?? frame).ToGuiButtons()));
        router!.Register(new HostScreen(ScreenName(id), handle, () => { renderer.End(); renderer.Begin(); draw(); }, update,
            () => { activeScreen = id; menuIndex = savedFocus.GetValueOrDefault(id); if (gumName is not null) gum.Show(gumName, true); },
            () => { if (gumName is not null) gum.Show(gumName, false); animations.CancelOwner(ScreenName(id)); audioCues.CancelOwner(ScreenName(id)); },
            () => menuIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            focus => { activeScreen = id; if (int.TryParse(focus, out int index)) menuIndex = index; }));
    }
    private sealed class HostScreen(string screenId, Action<InputFrame> input, Action draw, Action? update,
        Action enter, Action exit, Func<string?> focus, Action<string?> restore) : IGameScreen, IScreenFocus
    {
        public string ScreenId => screenId;
        public void OnEnter(ScreenContext context, ScreenArguments arguments) => enter();
        public void OnExit() => exit();
        public void HandleInput(InputFrame frame) => input(frame);
        public void Update(TimeSpan simulationDelta, TimeSpan presentationTime) => update?.Invoke();
        public void Draw(ScreenDrawContext context) => draw();
        public string? CaptureFocus() => focus();
        public void RestoreFocus(string? value) => restore(value);
    }
    private void RecoverSetup(Action action)
    {
        try { action(); }
        catch (Exception e) when (PrivateStorage.Recoverable(e))
        {
            if (editor is not null) editor.Notice = "The operation could not finish. Check storage and retry.";
            if (screen == ScreenId.Preferences) preferencesNotice = "Preferences were not saved. Check access and free space.";
            if (screen == ScreenId.SavedDecks) screen = ScreenId.DeckEditor;
        }
    }
    /// <summary>The PC utility hub, reached through F12. It is not an original screen.</summary>
    private void UpdatePcTools(GuiButtons buttons)
    {
        if (buttons.Back) { WorkbenchLauncher.Start(setup?.Storage.Root); Exit(); return; }
        MoveMenu(buttons, 5);
        if (!buttons.Confirm) return;
        // Navigation/application actions only. Gum never receives a DuelController.
        Action[] actions = [OpenEditor, () => screen = ScreenId.DeckSelection,
            () => screen = ScreenId.Preferences, () => screen = ScreenId.Controls, Exit];
        actions[menuIndex]();
    }
    private void UpdateFreeDuel(GuiButtons buttons)
    {
        if (buttons.Back) { screen = ScreenId.PcTools; return; }
        MoveMenu(buttons, InitialOpponentDecks.Names.Count);
        if (buttons.Confirm) StartDuel(menuIndex);
    }
    private DuelFieldInput? duelInput;
    private DuelController? duelInputOwner;
    private DuelFieldPresentation duelFieldPresentation = null!;
    /// <summary>The input session of the current duel; a new duel starts a new one.</summary>
    private DuelFieldInput DuelInput(DuelController controller)
    {
        if (!ReferenceEquals(duelInputOwner, controller))
        {
            duelInput = new() { DetailFactory = DuelCardDetail };
            duelFieldPresentation = new(PresentationTime, !options.PixelExact);
            duelInputOwner = controller;
        }
        return duelInput!;
    }
    private App.Screens.Album.CardAlbumDetailRenderer? duelDetail;
    /// <summary>The detail page of a visible card, with the album's counters: copies in the current deck (Main with Extra, Side) and the
    /// Trunk (owned copies of supported cards minus those).</summary>
    private App.Screens.Album.CardAlbumDetail? DuelCardDetail(Core.Views.CardView card)
    {
        if (duelDetail is not { } renderer || card.CardId is not { } id) return null;
        var draft = editor?.Draft;
        int main = draft is null ? 0 : draft.Count(id, Core.Decks.DeckSection.Main) + draft.Count(id, Core.Decks.DeckSection.Extra);
        int side = draft?.Count(id, Core.Decks.DeckSection.Side) ?? 0;
        int owned = Effects.ConfirmedCardPool.Contains(id) ? App.Screens.Title.DeckEditSession.OwnedCopies : 0;
        var scroll = renderer.Profile.Scroll;
        return new(id, new(main, side, Math.Max(0, owned - main - side)), tab => renderer.ContentHeight(id, tab), scroll.Visible, scroll.PixelsPerFrame);
    }
    /// <summary>Original controls (measured): ○ menus, × closes or opens the phase selector, △ global menu. START opens the PC pause
    /// menu until the original Help screen exists. Every choice is revision-bound; a refusal closes the window without state changes.</summary>
    private void UpdateDuelInput(InputFrame frame)
    {
        if (duel is null || duel.View.Outcome is not null) return;
        if (frame.IsPressed(InputAction.Start)) { duel.Paused = true; screen = ScreenId.Paused; return; }
        var input = DuelInput(duel);
        var before = duel.View;
        if (duelFieldPresentation.BeforeDeal(before, PresentationTime) && DuelField() is not null) return;
        if (input.Handle(before, frame, navigationTime.ElapsedGameTime) is { } submit)
        {
            if (duel.Choose(submit.Revision, submit.ChoiceId)) duelFieldPresentation.Accept(before, duel.View, submit.ChoiceId, PresentationTime);
            else input.Rejected(duel.Notice);
        }
    }
    private void UpdateDuelPresentation()
    {
        if (duel is null) return;
        _ = DuelInput(duel);
        if (duelFieldPresentation.BeforeDeal(duel.View, PresentationTime) && DuelField() is not null) return;
        if (duel.View.Outcome is null && IsActive && navigationTime.TotalGameTime.TotalSeconds >= opponentAt)
        {
            if (!duel.OpponentStopped && duel.StepOpponent()) choiceIndex = 0;
            opponentAt = navigationTime.TotalGameTime.TotalSeconds + .35;
        }
        if (duel.View.Outcome is not null && duelFieldPresentation.ResultReady(PresentationTime)) screen = ScreenId.Result;
    }
    private void UpdatePause(GuiButtons buttons)
    {
        MoveMenu(buttons, 2);
        if (buttons.Back || buttons.Confirm && menuIndex == 0)
        {
            if (duel is not null) duel.Paused = false;
            screen = ScreenId.Duel; opponentAt = navigationTime.TotalGameTime.TotalSeconds + .35;
        }
        else if (buttons.Confirm)
        {
            if (freeDuelResult is not null) screen = ScreenId.Result;
            else { duel = null; screen = ScreenId.PcTools; }
        }
    }
    private void UpdateDuelResult(GuiButtons buttons)
    {
        if (freeDuelResult is not { } result)
        {
            if (buttons.Back) screen = ScreenId.PcTools;
            else if (buttons.Confirm) { if (setup?.Game is not null) OpenEditor(); else screen = ScreenId.DeckSelection; }
            return;
        }

        var action = result.Handle(buttons);
        if (action == FreeDuelResultAction.None) return;
        if (action == FreeDuelResultAction.SaveAndReturn && !TrySaveFreeDuelProfile(result)) return;
        duel = null;
        freeDuelResult = null;
        if (action == FreeDuelResultAction.Rematch) BeginFlow(flow => flow.FreeDuel!.BeginStart());
        else BeginFlow(flow => flow.BeginMainMenu());
    }
    private bool TrySaveFreeDuelProfile(FreeDuelResultSession result)
    {
        try
        {
            titleFlow!.SaveCurrentProfile();
            return true;
        }
        catch (Exception error) when (PrivateStorage.Recoverable(error))
        {
            result.Notice = "Profile save failed. Retry or return without saving.";
            return false;
        }
    }
    private InputFrame DuelResultPointer(InputFrame frame)
    {
        if (freeDuelResult is not { } result) return PointerTargets.Clicks(frame);
        return PointerTargets.Apply(frame, FreeDuelResultScreen.HitRow(frame.Pointer.Position), result.Focus, result.PointAt);
    }
    private void DrawDuelResult()
    {
        if (freeDuelResult is { } result && duel is { } controller)
            FreeDuelResultScreen.Draw(renderer, controller.View, result);
        else DrawDuel();
    }
    private void DrawDuel()
    {
        if (duel is null) return;
        var input = DuelInput(duel);
        var notice = duelFieldPresentation.Notice(duel.View.Revision, duel.Notice, navigationTime.TotalGameTime);
        DuelScene.Draw(renderer, DuelField(), new(duel.View, choiceIndex, inspectIndex, notice, PresentationTime.TotalSeconds,
            Style: duelStyle, Facts: FieldCardFacts(), Input: input)
        {
            Presentation = duelFieldPresentation.Frame(duel.View, PresentationTime)
        });
    }
    /// <summary>Level and Tuner facts of public cards, from the imported catalog.</summary>
    private Func<uint, DuelCardFacts?>? FieldCardFacts() => setup?.Game?.Catalog.Catalog is { } catalog ? id => DuelCardFacts.From(catalog[id]) : null;
    private DuelFieldStyle? duelStyle;
    /// <summary>Names and faces of a Free Duel. Backdrops are known for three captured characters only (Aki TF6, Yuya TF4, Joey default);
    /// every other opponent uses the default backdrop until the original's selection is measured.</summary>
    private DuelFieldStyle? FreeDuelStyle(App.Screens.FreeDuel.FreeDuelSession session)
    {
        if (freeDuelRenderer is not { } versus) return null;
        var (name, bust) = versus.Versus.Opponent(session);
        string? face = Assets.Presentation.DuelFaceIcons.ByRecord.GetValueOrDefault(bust - 3);
        string backdrop = face switch { "fw3_aki01" => "tf6", "fw5_YUYA_00" => "tf4", _ => "default" };
        return new(backdrop, session.PlayerName, name, Assets.Presentation.DuelFaceIcons.ByRecord[Assets.Presentation.DuelFaceIcons.PlayerRecord], face);
    }
    /// <summary>Field text through the title flow's original card-text font.</summary>
    private sealed class FlowFieldText(TitleFlowVisuals visuals) : IDuelFieldTextSink
    {
        public void DrawUi(string text, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha) =>
            visuals.DrawText(text, x, y, scaleX, scaleY, color, shadowAlpha);
        public float MeasureUi(string text, float scaleX) => visuals.MeasureText(text, scaleX);
        public void Draw(string text, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha) =>
            visuals.DrawCardFontText(text, x, y, scaleX, scaleY, color, shadowAlpha);
        public float Measure(string text, float scaleX) => visuals.MeasureCardFontText(text, scaleX);
    }
    /// <summary>EBOOT textures the title flow decodes and pins (the duel status icons share its dialog atlas).</summary>
    private Microsoft.Xna.Framework.Graphics.Texture2D? EbootTexture(string key)
    {
        if (flowVisuals is null) return null;
        try { return flowVisuals.Texture(key); }
        catch (KeyNotFoundException) { return null; }
    }
    private DuelFieldVisuals? duelField;
    private OriginalScreenAssetResolver? duelFieldAssets;
    /// <summary>The measured field for the current validated generation, or null when its original textures are not imported.</summary>
    private DuelFieldVisuals? DuelField()
    {
        if (ReferenceEquals(duelFieldAssets, screenAssets)) return duelField;
        duelField?.Dispose(); duelField = null; duelFieldAssets = screenAssets;
        if (setup?.Game?.Presentation is null) return null;
        try
        {
            duelField = new(GraphicsDevice, screenAssets) { Text = flowVisuals is { } flow ? new FlowFieldText(flow) : null, External = EbootTexture };
            if (flowVisuals is { } visuals && setup.Game.Catalog.Catalog is { } catalog)
            {
                // The left card panel is the Card Album's (same measured geometry); its textures come from the same pinned imports.
                var album = App.Screens.Album.CardAlbumProfile.LoadEmbedded();
                visuals.UseAlbumAssets(screenAssets, album.AssetKeys(), Assets.Presentation.CardAlbumResources.ArchiveKey, Assets.Presentation.CardAlbumResources.CardInfoKey);
                var panel = new App.Screens.Album.CardInfoPanel(GraphicsDevice, visuals, album);
                duelField.Panel = (id, tint, back) => panel.Draw(id is { } card ? catalog[card] : null, id is null,
                    App.Screens.Title.MainMenuEntry.FramesIn(PresentationTime), tint, back);
                duelField.AttackPanel = a => AttackPanels.Draw(panel, catalog, a, App.Screens.Title.MainMenuEntry.FramesIn(PresentationTime));
                // Show Details is the Card Album's detail page (same screen in the original).
                var detailProfile = App.Screens.Album.CardAlbumDetailProfile.LoadEmbedded();
                visuals.UseAlbumAssets(screenAssets, detailProfile.AssetKeys(), Assets.Presentation.CardAlbumResources.ArchiveKey, Assets.Presentation.CardAlbumResources.CardInfoKey);
                duelDetail = new App.Screens.Album.CardAlbumDetailRenderer(GraphicsDevice, visuals, panel, catalog, detailProfile,
                    new Assets.Presentation.CardDetailSource(visuals.SourceBytes("eboot"), visuals.SourceBytes("cardinfo")));
                duelField.Detail = duelDetail.Draw;
            }
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException)
        { Console.Error.WriteLine("DUEL_FIELD_PENDING " + e.Message); }
        return duelField;
    }
    private void DrawPreferences()
    {
        var value = setup?.Preferences;
        gum.BindMenu("NativePreferences", [$"OPPONENT SEED: {value?.OpponentSeed ?? options.Seed}",
            $"INTEGER SCALING: {(value?.IntegerScaling == true ? "ON" : "OFF")}", "RESET", "BACK"], menuIndex);
        gum.BindText("NativePreferences", "Notice", preferencesNotice);
    }
    /// <summary>Pointer rows sharing the native screens' <see cref="menuIndex"/> focus.</summary>
    private Func<InputFrame, InputFrame> Rows(Func<Rendering.CanvasPoint?, int?> hit) =>
        frame => PointerTargets.Apply(frame, hit(frame.Pointer.Position), menuIndex, i => menuIndex = i);

    /// <summary>Text-row hit areas of the native screens: rows of <paramref name="pitch"/> from <paramref name="y"/> (paged from
    /// <paramref name="first"/>, <paramref name="page"/> rows shown), from <paramref name="x"/> to the right edge.</summary>
    private static int? TextRow(Rendering.CanvasPoint? at, float x, float y, float pitch, int count, int first, int page = int.MaxValue)
    {
        if (at is not { } point || point.X < x - 4 || point.X >= 470) return null;
        int row = (int)MathF.Floor((point.Y - y + 3) / pitch);
        return row >= 0 && row < page && first + row < count ? first + row : null;
    }

    private InputFrame EditorPointer(InputFrame frame)
    {
        if (editor is null || editor.Draft is not { } draft) return frame;
        if (editor.EditingText is not null) return PointerTargets.Clicks(frame);
        if (editor.MenuOpen)
            return PointerTargets.Apply(frame, TextRow(frame.Pointer.Position, 12, 44, 20, Decks.DeckEditorSession.Actions.Count, 0), editor.MenuIndex, editor.PointMenu);
        int count = draft.VisibleCards().Count, start = editor.CardIndex / 8 * 8;
        var at = frame.Pointer.Position;
        int? card = at is { X: < 305 } ? TextRow(at, 10, 49, 17, count, start, 8) : null;
        return PointerTargets.Apply(frame, card, editor.CardIndex, i => editor.CardIndex = i);
    }

    private ControlsEditor? controls;

    /// <summary>Controls: rebinding saves at once with the other preferences; the mapper switches to the new keys on the next frame.</summary>
    private void UpdateControls(InputFrame frame)
    {
        controls ??= new(input.Bindings);
        switch (controls.Handle(frame, input.LastKeys))
        {
            case ControlsOutcome.Back: controls = null; screen = ScreenId.PcTools; break;
            case ControlsOutcome.Changed:
                input.Bindings = controls.Bindings;
                if (setup is not null && !options.PresentationSmoke)
                    RecoverSetup(() => setup.SavePreferences(setup.Preferences with { KeyBindings = controls.Bindings.ToDocument() }));
                break;
        }
    }

    private void DrawControls()
    {
        var c = controls ??= new(input.Bindings);
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        renderer.Fill(new(16, 8, 448, 256), new Color(18, 27, 43), Rendering.SceneLayer.Background);
        renderer.Text("CONTROLS", (int)ControlsEditor.NameX, 14, gold);
        renderer.Text("KEY 1", (int)ControlsEditor.SlotX0 + 4, 14, muted);
        renderer.Text("KEY 2", (int)ControlsEditor.SlotX1 + 4, 14, muted);
        for (int row = 0; row < ControlsEditor.Rows; row++)
        {
            int y = (int)ControlsEditor.RowY(row);
            bool focused = row == c.Row;
            if (row >= KeyBindings.Actions.Length)
            {
                if (focused) renderer.Fill(new((int)ControlsEditor.NameX - 4, y - 2, 200, 14), new Color(42, 59, 74));
                renderer.Text(row == c.ResetRow ? "RESET TO DEFAULTS" : "BACK", (int)ControlsEditor.NameX, y, focused ? gold : Color.White);
                continue;
            }
            var (action, name) = KeyBindings.Actions[row];
            renderer.Text(name.ToUpperInvariant(), (int)ControlsEditor.NameX, y, focused ? gold : Color.White);
            for (int slot = 0; slot < KeyBindings.Slots; slot++)
            {
                int x = (int)(slot == 0 ? ControlsEditor.SlotX0 : ControlsEditor.SlotX1);
                bool cell = focused && slot == c.Slot;
                if (cell) renderer.Fill(new(x, y - 2, (int)ControlsEditor.SlotWidth, 14), c.Capturing ? new Color(90, 70, 30) : new Color(42, 59, 74));
                string label = cell && c.Capturing ? "PRESS A KEY..." : KeyBindings.KeyName(c.Bindings[action, slot]).ToUpperInvariant();
                renderer.Text(label, x + 4, y, action == InputAction.Back && slot == 0 ? muted : cell ? gold : Color.White);
            }
        }
        renderer.Text(c.Capturing ? "PRESS A KEY. ESC OR RIGHT CLICK: CANCEL" : "ENTER/CLICK: REBIND  LEFT/RIGHT: SLOT  ESC: BACK",
            (int)ControlsEditor.NameX, 248, muted);
    }
    private void DrawFreeDuel() => DrawMenuPanel("SUPPORTED DECK / 40 CARDS", InitialOpponentDecks.Names.ToArray());
    private void DrawPause() => DrawMenuPanel("DUEL PAUSED", ["RESUME", "END DUEL"]);
    private void DrawMenuPanel(string title, string[] rows, bool selection = true)
    {
        renderer.Fill(new(24, 28, 432, 216), new Color(18, 27, 43), Rendering.SceneLayer.Background);
        renderer.Text(title, 40, 48, new Color(238, 196, 104));
        for (int i = 0; i < rows.Length; i++) renderer.Text((selection && menuIndex == i ? "> " : "  ") + rows[i], 40, 84 + 23 * i);
    }
}
