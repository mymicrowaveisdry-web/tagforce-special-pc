using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Rendering;
using TagForceSpecial.PC.Screens;

namespace TagForceSpecial.PC.Game;

public enum ScreenId { Title, MainMenu, DeckSelection, Setup, Duel, Result, Controls }

public sealed partial class GameLoop : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly HostOptions options;
    private readonly GuiInput input = new();
    private SpriteRenderer renderer = null!;
    private RenderTarget2D canvas = null!;
    private SpriteBatch presenter = null!;
    private DuelController? duel;
    private ScreenId screen;
    private int menuIndex, choiceIndex, inspectIndex = -1, smokeCommands, frames;
    private bool captured;
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
        renderer = new(GraphicsDevice);
        presenter = new(GraphicsDevice);
        canvas = new(GraphicsDevice, PspViewport.Width, PspViewport.Height, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        if (options.Smoke && !StartDuel(0)) throw new InvalidOperationException(setupMessage);
    }

    private bool StartDuel(int variant)
    {
        if (options.CachePath is null)
        {
            setupMessage = "Launch with --cache pointing to your validated private cache.";
            screen = ScreenId.Setup;
            return false;
        }
        var validation = GameCache.Validate(options.CachePath);
        var cache = validation.Cache;
        if (cache is null)
        {
            setupMessage = "Game data could not be verified. Rebuild it in Local Game Setup.";
            screen = ScreenId.Setup;
            return false;
        }
        try
        {
            var first = InitialDeckPolicy.Example();
            if (variant == 1) first = new(first.DeckId, first.Revision, first.OwnerId, first.CatalogId,
                first.CatalogRevision, first.SourceBuild, first.PolicyId, first.PolicyRevision, first.Main.Reverse());
            var selection = TagForceSpecial.PC.Decks.SelectedDecks.Validate(cache, InitialDeckPolicy.Create(),
                first, InitialDeckPolicy.ExampleCollection(), InitialDeckPolicy.Example("local-opponent"),
                InitialDeckPolicy.ExampleCollection("local-opponent"));
            if (selection.Selection is not { } decks) throw new InvalidDataException("Deck selection is not valid.");
            duel = new(PlayablePreview.CreateEngine(cache.Catalog.Catalog, decks.First.Main, decks.Second.Main), cache.Catalog.Catalog);
        }
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
        var buttons = input.Poll(IsActive);
        if (options.Smoke)
        {
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
            if (duel?.View.Outcome is { } outcome)
            {
                Console.WriteLine($"GUI_SMOKE completed commands={smokeCommands} winner={outcome.Winner} reason={outcome.Reason}");
                Exit();
            }
            return;
        }
        if (buttons.Back)
        {
            screen = screen == ScreenId.MainMenu ? ScreenId.Title : ScreenId.MainMenu;
            menuIndex = 0;
            return;
        }
        switch (screen)
        {
            case ScreenId.Title:
                if (buttons.Confirm) screen = ScreenId.MainMenu;
                break;
            case ScreenId.MainMenu:
                MoveMenu(buttons, 3);
                if (buttons.Confirm)
                {
                    if (menuIndex == 2) Exit();
                    else screen = menuIndex == 0 ? ScreenId.DeckSelection : ScreenId.Controls;
                    menuIndex = 0;
                }
                break;
            case ScreenId.DeckSelection:
                MoveMenu(buttons, 2);
                if (buttons.Confirm) StartDuel(menuIndex);
                break;
            case ScreenId.Duel when duel is not null:
                int count = duel.View.Choices.Count;
                if (count > 0)
                {
                    int delta = buttons.Down ? 1 : buttons.Up ? -1 : buttons.Right ? 4 : buttons.Left ? -4 : 0;
                    choiceIndex = (choiceIndex + delta % count + count) % count;
                    if (delta != 0) inspectIndex = -1;
                    if (buttons.Confirm)
                    {
                        duel.Choose(duel.View.Revision, choiceIndex);
                        choiceIndex = 0;
                        inspectIndex = -1;
                    }
                }
                if (buttons.Inspect) inspectIndex++;
                if (IsActive && gameTime.TotalGameTime.TotalSeconds >= opponentAt)
                {
                    if (duel.StepOpponent()) choiceIndex = 0;
                    opponentAt = gameTime.TotalGameTime.TotalSeconds + .35;
                }
                if (duel.View.Outcome is not null) screen = ScreenId.Result;
                break;
            case ScreenId.Result:
                if (buttons.Confirm)
                {
                    screen = ScreenId.DeckSelection; menuIndex = 0;
                }
                break;
        }
        base.Update(gameTime);
    }

    private void MoveMenu(GuiButtons buttons, int count)
    {
        if (buttons.Up) menuIndex = (menuIndex + count - 1) % count;
        if (buttons.Down) menuIndex = (menuIndex + 1) % count;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(canvas);
        GraphicsDevice.Clear(new Color(9, 15, 27));
        renderer.Begin();
        if (screen is ScreenId.Duel or ScreenId.Result && duel is not null)
            DuelScreen.Draw(renderer, duel.View, choiceIndex, inspectIndex, duel.Notice,
                options.PixelExact ? 0 : gameTime.TotalGameTime.TotalSeconds);
        else DrawMenu();
        renderer.End();
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        var target = PspViewport.Fit(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight, options.PixelExact);
        presenter.Begin(samplerState: SamplerState.PointClamp);
        presenter.Draw(canvas, new Rectangle(target.X, target.Y, target.Width, target.Height), Color.White);
        presenter.End();
        frames++;
        if (!captured && options.CapturePath is { } path)
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            canvas.SaveAsPng(output, 480, 272);
            captured = true;
            Console.WriteLine("GUI_CAPTURE logical=480x272");
            if (!options.Smoke) Exit();
        }
        base.Draw(gameTime);
    }

    private void DrawMenu()
    {
        var gold = new Color(238, 196, 104);
        var muted = new Color(143, 162, 187);
        for (int i = 0; i < 8; i++)
            renderer.Fill(new(12 + i * 10, 24 + i * 8, 456 - i * 20, 224 - i * 16), new Color(18 + i, 27 + i, 43 + i), SceneLayer.Background);
        renderer.Text("TAG FORCE", 46, 43, gold, 3);
        renderer.Text("SPECIAL / PC", 47, 71, muted, 2);
        switch (screen)
        {
            case ScreenId.Title:
                renderer.Text("LOCAL DUEL", 48, 112, gold);
                renderer.Wrapped("Prepare your local game data, build a supported deck and challenge the initial opponent.", 48, 133, 59, 3, muted);
                renderer.Text("PRESS ENTER / A", 48, 195);
                break;
            case ScreenId.MainMenu:
                Menu(["DUEL", "CONTROLS", "EXIT"]);
                break;
            case ScreenId.DeckSelection:
                renderer.Text("SUPPORTED DECK / 40 CARDS", 48, 109, gold);
                Menu(["INITIAL PREVIEW", "INITIAL PREVIEW / REVERSE ORDER"], 132);
                renderer.Wrapped("14 supported IDs. Ordered decks and an authored local collection.", 48, 211, 59, 3, muted);
                break;
            case ScreenId.Setup:
                renderer.Text("LOCAL DATA REQUIRED", 48, 109, gold);
                renderer.Wrapped(setupMessage, 48, 130, 59, 5);
                renderer.Wrapped("Use the source/cache import commands documented in Foundations.md, then relaunch with --cache. ESC returns to menu.", 48, 190, 59, 4, muted);
                break;
            case ScreenId.Controls:
                renderer.Text("UP/DOWN OR D-PAD: CHOOSE ACTION", 48, 118);
                renderer.Text("LEFT/RIGHT: PAGE ACTIONS", 48, 136);
                renderer.Text("ENTER / A: CONFIRM", 48, 154);
                renderer.Text("TAB / Y: INSPECT VISIBLE CARDS", 48, 172);
                renderer.Text("ESC / B: RETURN TO MENU", 48, 190);
                break;
        }
        renderer.Text("480 X 272   /   LOCAL ASSETS   /   INITIAL SLICE", 48, 242, muted);

        void Menu(string[] items, int top = 120)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (menuIndex == i) renderer.Fill(new(42, top + i * 23 - 5, 390, 20), new Color(49, 65, 79));
                renderer.Text((menuIndex == i ? "> " : "  ") + items[i], 48, top + i * 23, menuIndex == i ? gold : Color.White);
            }
        }
    }

    protected override void UnloadContent()
    {
        canvas.Dispose();
        renderer.Dispose();
        presenter.Dispose();
        base.UnloadContent();
    }
}
