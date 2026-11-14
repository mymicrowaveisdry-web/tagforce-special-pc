using System.Text.Json;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Recipes;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private int setupFlowStage;
    private bool setupFlowInitiallyReady;
    private int setupFlowRecipeCount;
    private readonly List<object> setupFlowObservations = [];

    /// <summary>Bounded native startup/import/Continue scenario using the same setup actions as interactive input.</summary>
    private void CheckSetupFlowFrame()
    {
        if (options.SetupFlowSmokeRoot is not { } root) return;
        if (navigationTime.TotalGameTime.TotalSeconds > 120) throw new InvalidOperationException("Startup flow did not complete.");
        if (setupTask is not null) return;
        Directory.CreateDirectory(root);
        void Capture(string name)
        {
            using var output = new FileStream(Path.Combine(root, name + ".png"), FileMode.CreateNew, FileAccess.Write);
            canvas.SaveAsPng(output, 480, 272);
            setupFlowObservations.Add(new { state = name, screen = screen.ToString(), canContinue = CanContinueToGame,
                recipeCount = setupFlowRecipeCount, gameplayCommands = smokeCommands });
        }
        var confirm = new GuiButtons(false, false, false, false, true, false, false);
        if (setupFlowStage == 0)
        {
            if (screen != ScreenId.Setup) throw new InvalidOperationException("Normal startup bypassed the importer.");
            setupFlowInitiallyReady = CanContinueToGame;
            Capture("importer");
            setupFlowStage = 1;
            if (setupFlowInitiallyReady && options.RecipeReferenceRoot is not null)
            {
                menuIndex = 2;
                UpdateSetupScreen(confirm);
                return;
            }
        }
        if (setupFlowStage == 1)
        {
            if (screen != ScreenId.Setup) throw new InvalidOperationException("Import completion bypassed the importer.");
            if (setupFlowInitiallyReady)
            {
                var stored = new RecipeLibraryStore(setup!.Storage).OpenActive(setup.Cache!.Catalog.Catalog);
                if (stored is null && options.RecipeReferenceRoot is not null)
                    throw new InvalidOperationException("Recipe import was not activated.");
                setupFlowRecipeCount = stored?.Records.Count ?? 0;
                var reloaded = new RecipeLibraryStore(new Setup.PrivateStorage(setup.Storage.Root)).OpenActive(setup.Cache.Catalog.Catalog);
                if (reloaded?.Identity != stored?.Identity || reloaded?.Records.Count != stored?.Records.Count)
                    throw new InvalidOperationException("Imported recipes did not survive reopening storage.");
                Capture(options.RecipeReferenceRoot is not null ? "recipes-imported" : "recipes-reloaded");
            }
            menuIndex = 0;
            UpdateSetupScreen(confirm);
            if (screen != (setupFlowInitiallyReady ? ScreenId.Title : ScreenId.Setup))
                throw new InvalidOperationException("Continue did not honor usable-import state.");
            setupFlowStage = 2;
            return;
        }
        Capture(setupFlowInitiallyReady ? "game" : "import-required");
        if (smokeCommands != 0) throw new InvalidOperationException("Setup issued a gameplay command.");
        using (var output = new FileStream(Path.Combine(root, "trace.json"), FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(output, new { importerShown = true, importedGameUsable = setupFlowInitiallyReady,
                recipeCount = setupFlowRecipeCount, observations = setupFlowObservations, gameplayCommands = smokeCommands });
        Console.WriteLine($"SETUP_FLOW_SMOKE ready={setupFlowInitiallyReady} recipes={setupFlowRecipeCount} commands=0");
        Exit();
    }
}
