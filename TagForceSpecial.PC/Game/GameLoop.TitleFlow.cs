using Microsoft.Xna.Framework;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Setup;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.Effects;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private const string FontFile = "fonts/psp-latin.pgf";
    private TitleFlow? titleFlow;
    private TitleFlowVisuals? flowVisuals;
    private PlayerProfileStore? profiles;
    private bool smokeProfile;
    private string? renderedFlowKey;
    private int renderedMenuFrame = -1, renderedOptionsFrame = -1;
    private string? renderedDeckEditKey;
    private readonly ScreenFade screenFade = new();

    /// <summary>The flow is presented only when every source texture and text reference validates.</summary>
    private bool TryInitializeTitleFlow()
    {
        if (setup is null) return false;
        try
        {
            ImportPspFont();
            string path = setup.Storage.PathFor(FontFile);
            byte[]? font = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var next = new TitleFlowVisuals(GraphicsDevice, screenAssets, font);
            try
            {
                next.ValidateText(); gum.BindFlowLayer(next.Layer); next.RenderMainMenuLabels(); gum.BindMainMenu(next);
                next.RenderDeckMenuLabels(); gum.BindDeckMenu(next); gum.BindDeckEdit(next.DeckEditLayer);
                next.RenderOptionsLayers(); next.RenderDialogLayers(); gum.BindOptions(next);
            }
            catch { next.Dispose(); throw; }
            flowVisuals?.Dispose();
            flowVisuals = next;
            profiles = new(setup.Storage);
            titleFlow = new(new FlowHost(this), Environment.UserName, instantTransitions: options.PixelExact);
            if (font is null) Console.Error.WriteLine("TITLE_FLOW_FONT_MISSING text uses the authored fallback; pass --psp-font once.");
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException || SetupStore.Recoverable(e))
        {
            setupMessage = "Original title assets need a validated title presentation import.";
            Console.Error.WriteLine("TITLE_FLOW_PENDING " + e.Message);
            return false;
        }
    }

    private void ImportPspFont()
    {
        if (options.PspFontPath is not { } source || setup is null) return;
        byte[] bytes = File.ReadAllBytes(source);
        if (new PgfFont(bytes).Sha256 != TitleFlowVisuals.RequiredFontSha256)
            throw new InvalidDataException("The selected file is not the pinned PSP Latin system font.");
        string target = setup.Storage.PathFor(FontFile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
    }

    private bool ProfilePresent => options.PresentationSmoke ? smokeProfile : profiles?.Present == true;

    private void BeginFlow(Action<TitleFlow> begin)
    {
        begin(titleFlow!);
        renderedFlowKey = null;
        screen = ScreenId.TitleFlow;
    }

    private static string? FrameFor(TitleFlowStep step) => step switch
    {
        TitleFlowStep.StorageCheck => "storage",
        TitleFlowStep.NoData => "no-data",
        TitleFlowStep.CancelLoadNoData => "cancel-load.black",
        TitleFlowStep.NameEntry => "entry.name",
        TitleFlowStep.RulesQuestion => "entry.rules",
        TitleFlowStep.ConfidenceQuestion => "entry.confidence",
        TitleFlowStep.DuelistLevel => "entry.level",
        TitleFlowStep.CreateQuestion => "entry.create",
        TitleFlowStep.QuitQuestion => "main-menu.quit",
        _ => null // Emulator/firmware keyboard and save dialogs are native PC UI, not game assets; the Main Menu has its own Gum screen.
    };

    /// <summary>
    /// Exact comparisons fix the game frame per focused row; otherwise the background restarts from its starting phase
    /// on each entry.
    /// </summary>
    private int MainMenuFrame(TitleFlow flow, TitleFlowVisuals visuals) => options.PixelExact
        ? options.MainMenuFrames.GetValueOrDefault(flow.MenuFocus)
        : (visuals.MainMenuMotion.Profile.EntryAnimationFrame + flow.MenuFrame) % visuals.MainMenuMotion.Profile.LoopFrames;

    /// <summary>Starts a screen change behind the measured fade. Exact runs change at once so each state stays frame-fixed.</summary>
    private void ChangeBehindFade(Action action, bool fadeIn, int lead = 0)
    {
        if (options.PixelExact) action();
        else if (!screenFade.Active) screenFade.Begin(action, fadeIn, lead);
    }

    private void DrawTransitionOverlay()
    {
        int alpha = screenFade.Alpha;
        if (activeScreen == ScreenId.TitleFlow && titleFlow is { Step: TitleFlowStep.MainMenu or TitleFlowStep.MainMenuUnavailable or TitleFlowStep.Options or TitleFlowStep.DeckMenu } flow)
            alpha = Math.Max(alpha, MainMenuEntry.OverlayAlpha(MainMenuEntryFrame(flow)));
        if (alpha == 0) return;
        renderer.Begin();
        renderer.Fill(new(0, 0, 480, 272), new Color(0, 0, 0, alpha), Rendering.SceneLayer.Modal);
        renderer.End();
    }

    private int MainMenuEntryFrame(TitleFlow flow) => options.MainMenuEntryFrame ?? flow.MenuFrame;

    private void RefreshMainMenu(TitleFlow flow, TitleFlowVisuals visuals)
    {
        int frame = MainMenuFrame(flow, visuals), entry = MainMenuEntryFrame(flow);
        // After the entry sequence only the background frame can change what the layer holds.
        int key = frame * 64 + Math.Min(entry, MainMenuEntry.ReadyFrame);
        if (key != renderedMenuFrame) { visuals.RenderMainMenuBackground(frame, entry); renderedMenuFrame = key; }
        // Measured: after the submenu closes, the menu's focus pulse restarts on the submenu's curve; exact runs hold its first value.
        int pulse = flow.MenuFocusRestartFrame is { } restart
            ? flow.DeckMenuProfile.PulseSinceFocus(options.PixelExact ? 0 : restart) : visuals.MainMenuMotion.ScalarsAt(frame).Pulse;
        gum.UpdateMainMenu(flow.MenuFocus, pulse, entry, flow.MainMenuDimmed ? flow.DeckMenuProfile.MenuDim : 255);
        gum.ShowMainMenuNotice(flow.Step == TitleFlowStep.MainMenuUnavailable ? flow.FocusedDestination : null);
        if (flow.DeckMenu is { } deckMenu)
        {
            // Exact runs fix the submenu's own frame as well as the background's.
            int deckFrame = options.PixelExact ? options.DeckMenuFrame ?? flow.DeckMenuProfile.ReadyFrame : deckMenu.Frame;
            gum.UpdateDeckMenu(flow.DeckMenuProfile, deckMenu.Focus, deckFrame, deckMenu.FocusVisible(deckFrame));
            gum.ShowDeckMenuNotice(deckMenu.NoticeOpen ? deckMenu.Focused : null);
            visuals.SetDeckMenuFallback(flow.MenuFocus, deckMenu.FocusVisible(deckFrame) ? deckMenu.Focus : null);
        }
        else visuals.SetMainMenuFallback(flow.MenuFocus);
    }

    private void RefreshOptions(TitleFlow flow, TitleFlowVisuals visuals)
    {
        var session = flow.Options!;
        int frame = MainMenuFrame(flow, visuals), entry = MainMenuEntryFrame(flow);
        int key = frame * 64 + Math.Min(entry, MainMenuEntry.ReadyFrame);
        // Its own cache key: the menu's layer can hold the same frame number without holding this picture.
        if (key != renderedOptionsFrame) { visuals.RenderOptionsBackground(frame, entry); renderedOptionsFrame = key; }
        // Exact runs fix the background frame; the pulse counts from the same entry, so it follows from that frame.
        var motion = visuals.MainMenuMotion.Profile;
        int sinceEntry = options.PixelExact ? (frame - motion.EntryAnimationFrame + motion.LoopFrames) % motion.LoopFrames : flow.MenuFrame;
        gum.ShowOptionsSaveQuestion(null, visuals.OptionsProfile.Rows.Length, visuals.OptionsProfile.Groups.Length);
        gum.UpdateOptions(visuals, session.Values, session.Focus, visuals.OptionsProfile.PulseAt(sinceEntry));
        gum.ShowOptionsDialog(session.CancelQuestion ? session.CancelFocus : null);
        visuals.SetOptionsFallback(session.Values, session.Focus);
    }

    /// <summary>The native draft, as the PC deck editor opens it: the selected saved revision, else the built-in deck.</summary>
    private void EnsureDraft()
    {
        if (draft is not null || setup?.Cache is null) return;
        if (setup.Preferences.SelectedDeck is { } key)
        {
            try { draft = new(setup.Cache.Catalog.Catalog, library!.Load(key)); draftKey = key; }
            catch (Exception e) when (SetupStore.Recoverable(e)) { editorNotice = "Saved revision is unreadable. Load an earlier revision or use a new deck."; }
        }
        draft ??= new(setup.Cache.Catalog.Catalog, InitialOpponentDecks.Create(0));
    }

    private void RefreshDeckEdit(TitleFlow flow, TitleFlowVisuals visuals, DeckEditSession edit)
    {
        gum.Show("OriginalMainMenu", false); gum.Show("OriginalDeckMenu", false); gum.Show("OriginalTitleFlow", false);
        if (edit.Prompt == DeckEditPrompt.SaveQuestion)
        {
            // Observed after OK: "Save all contents acquired so far?" over the bare animated background, as from Options.
            int bare = MainMenuFrame(flow, visuals);
            if (bare * 64 != renderedOptionsFrame) { visuals.RenderOptionsBackground(bare, 0); renderedOptionsFrame = bare * 64; }
            gum.Show("OriginalOptions", true);
            gum.ShowOptionsDialog(null);
            gum.ShowOptionsSaveQuestion(edit.PromptFocus, visuals.OptionsProfile.Rows.Length, visuals.OptionsProfile.Groups.Length);
            renderedDeckEditKey = null;
            return;
        }
        gum.Show("OriginalOptions", false);
        gum.Show("OriginalDeckEdit", true);
        string key = $"{edit.Tab}/{edit.Cursor}/{edit.Scroll}/{edit.Prompt}/{edit.PromptFocus}/{edit.Count(DeckEditTab.Deck)}/{edit.Count(DeckEditTab.Extra)}/{edit.Count(DeckEditTab.Side)}/{edit.Count(DeckEditTab.Trunk)}";
        if (key == renderedDeckEditKey) return;
        visuals.RenderDeckEdit(edit, edit.Draft.Name);
        renderedDeckEditKey = key;
    }

    private void RefreshFlow()
    {
        if (titleFlow is null || flowVisuals is null) return;
        bool menu = titleFlow.Step is TitleFlowStep.MainMenu or TitleFlowStep.MainMenuUnavailable or TitleFlowStep.DeckMenu;
        bool optionsScreen = titleFlow.Step == TitleFlowStep.Options && titleFlow.Options is not null;
        gum.Show("OriginalMainMenu", menu);
        gum.Show("OriginalDeckMenu", titleFlow.DeckMenu is not null);
        gum.Show("OriginalDeckEdit", false);
        if (titleFlow.Step == TitleFlowStep.DeckEdit && titleFlow.DeckEdit is { } edit) { RefreshDeckEdit(titleFlow, flowVisuals, edit); return; }
        gum.Show("OriginalOptions", optionsScreen);
        string? frame = FrameFor(titleFlow.Step);
        gum.Show("OriginalTitleFlow", frame is not null);
        if (menu) { renderedFlowKey = null; RefreshMainMenu(titleFlow, flowVisuals); return; }
        if (optionsScreen) { renderedFlowKey = null; RefreshOptions(titleFlow, flowVisuals); return; }
        if (titleFlow.Step == TitleFlowStep.OptionsSaveQuestion)
        {
            // Drawn over the bare animated background without header or bars; entry frame 0 draws neither header nor
            // title.
            int bare = MainMenuFrame(titleFlow, flowVisuals);
            if (bare * 64 != renderedOptionsFrame) { flowVisuals.RenderOptionsBackground(bare, 0); renderedOptionsFrame = bare * 64; }
            gum.Show("OriginalOptions", true);
            gum.ShowOptionsDialog(null);
            gum.ShowOptionsSaveQuestion(titleFlow.ButtonFocus, flowVisuals.OptionsProfile.Rows.Length, flowVisuals.OptionsProfile.Groups.Length);
            return;
        }
        if (frame is null) return;
        // The end-game question sits over the live menu; exact runs keep it on its dump's own instant.
        int? background = titleFlow.Step == TitleFlowStep.QuitQuestion && !options.PixelExact ? MainMenuFrame(titleFlow, flowVisuals) : null;
        string key = $"{frame}/{titleFlow.ButtonFocus}/{titleFlow.MenuFocus}/{titleFlow.Name}/{titleFlow.Level}/{background}";
        if (key == renderedFlowKey) return;
        flowVisuals.Render(frame, titleFlow.ButtonFocus, titleFlow.MenuFocus, titleFlow.Name, titleFlow.Level, background);
        renderedFlowKey = key;
    }

    /// <summary>Drawn above Gum: system-style UI and any fallback text.</summary>
    private void DrawFlowOverlay()
    {
        if (flowVisuals is { OriginalFontAvailable: false } visuals && activeScreen is ScreenId.TitleFlow or ScreenId.TitleStartMenu)
        {
            renderer.Begin();
            foreach (var line in visuals.FallbackLines) renderer.Text(line.Text, line.X, line.Y, line.Color);
            renderer.End();
        }
        if (activeScreen != ScreenId.TitleFlow || titleFlow is null) return;
        if (titleFlow.Step is not (TitleFlowStep.NameKeyboard or TitleFlowStep.SystemSavePrompt or TitleFlowStep.SystemSaveDone)) return;
        renderer.Begin();
        renderer.Fill(new(0, 0, 480, 272), new Color(16, 16, 16), Rendering.SceneLayer.Background);
        if (titleFlow.Step == TitleFlowStep.NameKeyboard)
        {
            renderer.Text($"NAME ({TitleFlow.NameLimit} LETTERS OR LESS)", 120, 70, Color.White);
            renderer.Text(titleFlow.Name + "_", 190, 110, new Color(238, 196, 104));
            renderer.Text("TYPE ON KEYBOARD", 160, 160, Color.White);
            renderer.Text("ENTER/F1: FINISH   ESC: DELETE", 90, 190, Color.White);
        }
        else if (titleFlow.Step == TitleFlowStep.SystemSavePrompt)
        {
            renderer.Text(ProfilePresent ? "DO YOU WANT TO OVERWRITE THE DATA?" : "DO YOU WANT TO SAVE THIS DATA?", 100, 100, Color.White);
            renderer.Text((titleFlow.ButtonFocus == 0 ? "> " : "  ") + "YES", 170, 140, Color.White);
            renderer.Text((titleFlow.ButtonFocus == 1 ? "> " : "  ") + "NO", 270, 140, Color.White);
        }
        else
        {
            renderer.Text("SAVE COMPLETED.", 170, 120, Color.White);
            renderer.Text("ESC / B: BACK", 185, 170, Color.White);
        }
        renderer.End();
    }

    private sealed class FlowHost(GameLoop owner) : ITitleFlowHost
    {
        public bool ProfilePresent => owner.ProfilePresent;
        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
        {
            name = ""; level = DuelistLevelChoice.FirstTimer; options = PlayerOptions.Defaults();
            if (owner.options.PresentationSmoke)
            {
                name = "Smoke";
                return owner.smokeProfile;
            }
            if (!owner.profiles!.TryLoad() || owner.profiles.Current is not { } current) return false;
            name = current.Name; level = current.Level; options = current.Options ?? PlayerOptions.Defaults();
            return true;
        }
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options)
        {
            if (owner.options.PresentationSmoke) owner.smokeProfile = true;
            else owner.profiles!.Save(name, level, options);
        }
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options)
        {
            if (!owner.options.PresentationSmoke) owner.profiles!.Save(name, level, options);
        }
        /// <summary>Deck Edit works on the native draft (the same one the PC deck tools edit). The Trunk is the supported card pool.
        /// Presentation checks start each visit from a fresh built-in deck so repeated cycles stay identical, and never save.</summary>
        public DeckEditSession? OpenDeckEdit()
        {
            if (owner.setup?.Cache?.Catalog.Catalog is not { } catalog || owner.flowVisuals is null) return null;
            DeckDraft draft;
            if (owner.options.PresentationSmoke) draft = new(catalog, InitialOpponentDecks.Create(0));
            else { owner.EnsureDraft(); draft = owner.draft!; }
            return new DeckEditSession(draft, PlayablePreview.Cards.Select(c => catalog[c.CardId]), owner.flowVisuals.DeckEditProfile.VisibleRows);
        }
        public void SaveDeckEdit(DeckEditSession session)
        {
            if (!owner.options.PresentationSmoke && ReferenceEquals(session.Draft, owner.draft)) owner.SaveDraft();
        }
    }

    private void LeaveFlowIfDone(ScreenContext context)
    {
        if (titleFlow?.Step == TitleFlowStep.ExitToTitle && activeScreen == ScreenId.TitleFlow) context.Navigation.GoTo("original.title");
    }

    private sealed class TitleFlowScreen(GameLoop owner) : IGameScreen
    {
        private ScreenContext context = null!;
        public string ScreenId => "original.title-flow";
        public void OnEnter(ScreenContext context, ScreenArguments arguments)
        {
            this.context = context;
            owner.activeScreen = Game.ScreenId.TitleFlow;
            owner.renderedFlowKey = null;
            owner.RefreshFlow();
        }
        public void HandleInput(PspInputFrame input)
        {
            var flow = owner.titleFlow!;
            flow.Handle(input);
            if (flow.PendingStep is { } pending)
                // Dialogs hold briefly after confirming. The title has its own fade-in; the menu's entry sequence includes one.
                owner.ChangeBehindFade(() => { flow.CommitPending(); owner.LeaveFlowIfDone(context); owner.RefreshFlow(); }, pending == TitleFlowStep.ExitToTitle, flow.PendingLeadFrames);
            owner.LeaveFlowIfDone(context);
            owner.RefreshFlow();
        }
        public void Update(TimeSpan simulationDelta, TimeSpan presentationTime)
        {
            var flow = owner.titleFlow!;
            if (owner.options.PresentationSmoke)
            {
                // Hold the storage frame for its own capture step, then resolve it deterministically.
                if (owner.HoldSmokeStorage()) return;
                if (flow.Step == TitleFlowStep.StorageCheck) simulationDelta = TitleFlow.StorageCheckDuration;
            }
            flow.Update(simulationDelta);
            owner.RefreshFlow();
        }
        public void Draw(ScreenDrawContext draw) { }
        public void OnExit()
        {
            owner.gum.Show("OriginalTitleFlow", false);
            owner.gum.Show("OriginalMainMenu", false);
            owner.gum.Show("OriginalDeckMenu", false);
            owner.gum.Show("OriginalDeckEdit", false);
            owner.renderedMenuFrame = -1;
        }
    }
}
