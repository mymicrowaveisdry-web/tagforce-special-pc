using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Text.Json;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.PC.Screens;

namespace TagForceSpecial.PC.Game;

public enum ScreenId { Title, PcTools, DeckSelection, Setup, Duel, Result, Controls, Paused, DeckEditor, SavedDecks, Preferences, AssetDiagnostics, TitleStartMenu, TitleFlow }

public sealed partial class GameLoop : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly HostOptions options;
    private readonly GameThreadGuard graphicsThread = new();
    private readonly TagForceSpecial.PC.App.Input.PspInputMapper input = new();
    private SpriteRenderer renderer = null!;
    private RenderTarget2D canvas = null!;
    private SpriteBatch presenter = null!;
    private DuelController? duel;
    private int menuIndex, choiceIndex, inspectIndex = -1, smokeCommands, frames;
    private bool captured, capturedResult, savedReplay, smokeFinished;
    private double opponentAt;
    private string setupMessage = "Local game data is required to start a duel.";

    public GameLoop(HostOptions options)
    {
        this.options = options;
        graphics = new(this)
        {
            PreferredBackBufferWidth = options.PixelExact ? 480 : 960,
            PreferredBackBufferHeight = options.PixelExact ? 272 : 544,
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "Tag Force Special PC - Initial GUI";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
        // Research/application assets are not copied through Content or published with the host.
    }

    protected override void LoadContent()
    {
        graphicsThread.Check();
        renderer = new(GraphicsDevice);
        presenter = new(GraphicsDevice);
        canvas = new(GraphicsDevice, PspViewport.Width, PspViewport.Height, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        InitializeSetup();
        InitializeRouting();
        if (options.AssetDiagnostics)
        {
            diagnosticScreenIndex = Array.IndexOf(DiagnosticScreens, options.DiagnosticScreen);
            OpenAssetDiagnostics();
        }
        if (options.Smoke && !SetupAutomation && !StartDuel(options.DeckVariant)) throw new InvalidOperationException(setupMessage);
    }

    private bool StartDuel(int variant)
    {
        if (options.CachePath is null && setup?.Cache is null)
        {
            setupMessage = "Select your original ISO in Local Game Setup, then prepare a deck.";
            screen = ScreenId.Setup;
            return false;
        }
        var validation = options.CachePath is null ? null : GameCache.Validate(options.CachePath);
        var cache = validation?.Cache ?? (options.CachePath is null ? setup?.Cache : null);
        if (cache is null)
        {
            setupMessage = "Game data could not be verified. Rebuild it in Local Game Setup.";
            screen = ScreenId.Setup;
            return false;
        }
        try { duel = SupportedDuelFactory.Create(cache, variant, CurrentSeed); }
        catch (InvalidDataException error)
        {
            setupMessage = error.Message;
            screen = ScreenId.Setup;
            return false;
        }
        screen = ScreenId.Duel;
        choiceIndex = 0;
        inspectIndex = -1;
        return true;
    }

    protected override void Update(GameTime gameTime)
    {
        PollSetupTask();
        var frame = options.PresentationSmoke ? PresentationSmokeInput()
            : options.RealtimeWalkRoot is not null ? RealtimeWalkInput(gameTime) : input.Poll(IsActive);
        navigationTime = gameTime;
        // Input is ignored while a fade runs; the change at black may also move to another screen.
        bool fading = screenFade.Active;
        screenFade.Tick(gameTime.ElapsedGameTime);
        if (fading) frame = default;
        // F12 is a host utility shortcut, separate from unsupported original PSP actions.
        if (frame.UtilityRequested && screen is ScreenId.Title or ScreenId.TitleStartMenu or ScreenId.PcTools or ScreenId.TitleFlow)
        { utilityReturn = screen; screen = ScreenId.Setup; return; }
        if (options.Smoke)
        {
            RunSetupSmoke();
            if (duel is null && SetupAutomation) return;
            // Runs the actual window/render path and the same controller used by interactive play.
            // The human path chooses the first offered action; the opponent uses its seeded policy.
            if (frames > 1 && duel is not null)
                for (int i = 0; i < 12 && duel.View.Outcome is null; i++)
                {
                    bool moved = duel.View.Choices.Count > 0
                        ? duel.Choose(duel.View.Revision, 0) : duel.StepOpponent();
                    smokeCommands++;
                    if (!moved || smokeCommands > 6000)
                        throw new InvalidOperationException($"GUI smoke stalled at revision {duel.View.Revision}: {duel.Notice}");
                }
            if (!smokeFinished && duel?.View.Outcome is { } outcome)
            {
                smokeFinished = true;
                screen = ScreenId.Result;
                Console.WriteLine($"GUI_SMOKE completed commands={smokeCommands} frames={frames} winner={outcome.Winner} reason={outcome.Reason} policy={AI.DeterministicPolicy.Version} seed={CurrentSeed}");
            }
            return;
        }
        router!.Tick(frame, gameTime.ElapsedGameTime, PresentationTime);
        animations.Update(PresentationTime);
        audioCues.Update();
        base.Update(gameTime);
    }

    private void MoveMenu(GuiButtons buttons, int count)
    {
        if (buttons.Up) menuIndex = (menuIndex + count - 1) % count;
        if (buttons.Down) menuIndex = (menuIndex + 1) % count;
    }

    protected override void Draw(GameTime gameTime)
    {
        graphicsThread.Check();
        GraphicsDevice.SetRenderTarget(canvas);
        GraphicsDevice.Clear(new Color(9, 15, 27));
        renderer.Begin();
        router!.Draw(new(renderer, PresentationTime));
        renderer.End();
        DrawDiagnosticPreviewTexture();
        gum.Draw();
        DrawFlowOverlay();
        DrawTransitionOverlay();
        if (options.PresentationSmoke) CheckPresentationSmokeFrame();
        if (options.RealtimeWalkRoot is not null) CheckRealtimeWalkFrame(gameTime);
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var target = PspViewport.Fit(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight, options.PixelExact || setup?.Preferences.IntegerScaling == true);
        presenter.Begin(samplerState: SamplerState.PointClamp);
        presenter.Draw(canvas, new Rectangle(target.X, target.Y, target.Width, target.Height), Color.White);
        presenter.End();
        frames++;
        CaptureEditor();
        if (!captured && options.CapturePath is { } path && (!options.AssetDiagnostics || diagnosticTask is null))
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            canvas.SaveAsPng(output, 480, 272);
            captured = true;
            Console.WriteLine("GUI_CAPTURE logical=480x272");
            if (!options.Smoke) Exit();
        }
        if (screen == ScreenId.Result && duel is not null)
        {
            if (!capturedResult && options.ResultCapturePath is { } resultPath)
            {
                using var output = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write);
                canvas.SaveAsPng(output, 480, 272);
                capturedResult = true;
            }
            if (!savedReplay && options.ReplayPath is { } replayPath)
            {
                using var output = new FileStream(replayPath, FileMode.CreateNew, FileAccess.Write);
                var replay = duel.ExportReplay(InitialOpponentDecks.ManifestId);
                JsonSerializer.Serialize(output, replay, new JsonSerializerOptions { WriteIndented = true });
                savedReplay = true;
                Console.WriteLine($"GUI_REPLAY commands={replay.Steps.Count} digest={replay.CommandDigest()}");
            }
        }
        if (smokeFinished) Exit();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        graphicsThread.Check();
        importCancellation?.Cancel();
        diagnosticPreview?.Dispose();
        diagnosticPreviewBlend?.Dispose();
        router?.Dispose();
        gum.Dispose();
        flowVisuals?.Dispose();
        originalTitle?.Dispose();
        screenAssets.Dispose();
        canvas.Dispose();
        renderer.Dispose();
        presenter.Dispose();
        base.UnloadContent();
    }
}
