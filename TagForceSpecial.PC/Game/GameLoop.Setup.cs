using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private SetupStore? setup;
    private DeckEditorSession? editor;
    private SetupOperation? setupOperation;
    private int setupSmokeStage, editorFrame;
    private string preferencesNotice = "Changes are saved locally.";
    private static readonly string[] SetupActions = ["SELECT ORIGINAL ISO", "RESUME IMPORT", "VERIFY CURRENT CACHE", "REBUILD FROM ORIGINAL ISO", "RESTART INTERRUPTED WORK", "PREFERENCES", "RESTORE SETTINGS BACKUP", "RESOURCE STATUS", "PC DUEL TOOLS"];
    private void InitializeSetup()
    {
        if (options.Smoke && !SetupAutomation) return;
        string root = options.DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial");
        setup = new(root); editor = new(setup); setupOperation = new(setup);
        Window.TextInput += (_, e) =>
        {
            if (titleFlow?.Editing == true) { titleFlow.TypeCharacter(e.Character); return; }
            editor.TypeCharacter(e.Character);
        };
        if (setup.Issue is { } issue) setupMessage = issue.Message;
        else if (options.SetupSource is null && setup.Preferences.ActiveGeneration is not null)
        {
            setupOperation.Validate(initial: true);
        }
        if (options.SetupSource is { } source) { screen = ScreenId.Setup; BeginImport(source); }
    }
    private void BeginImport(string source)
    {
        if (setupOperation?.Import(source) != true) return;
        setupMessage = "Importing local game data";
        screen = ScreenId.Setup;
    }
    private uint CurrentSeed => options.SeedSpecified ? options.Seed : setup?.Preferences.OpponentSeed ?? options.Seed;
    private bool SetupAutomation => options.Smoke && (options.SetupSource is not null || options.CachePath is null && options.DataRoot is not null);
    private void PollSetupTask()
    {
        if (setupOperation?.Poll() is not { } result) return;
        setupMessage = result.Message;
        if (result.Success) TryActivateImportedTitle();
    }
    private void OpenEditor()
    {
        if (setup?.Cache is null) { screen = ScreenId.Setup; menuIndex = 0; return; }
        editor!.Open();
        screen = ScreenId.DeckEditor;
    }
    private bool LaunchDraft()
    {
        if (editor is null) return false;
        var result = editor.Launch(CurrentSeed);
        if (result.Controller is null)
        {
            if (result.SetupError is { } error) { setupMessage = error; screen = ScreenId.Setup; }
            return false;
        }
        duel = result.Controller;
        screen = ScreenId.Duel; choiceIndex = 0; inspectIndex = -1;
        return true;
    }
    private void UpdateSetupScreen(GuiButtons buttons)
    {
        if (setupOperation?.Busy == true)
        { if (buttons.Back) { setupOperation?.Cancel(); setupMessage = "Stopping safely..."; } return; }
        if (buttons.Back) { screen = utilityReturn; return; }
        MoveMenu(buttons, SetupActions.Length);
        if (!buttons.Confirm || setup is null) return;
        try
        {
            switch (menuIndex)
            {
                case 0: ContinueToGame(); break;
                case 1: case 5:
                    string? file = WindowsFilePicker.ChooseIso(); if (file is not null) BeginImport(file); break;
                case 2:
                    if (setup.Cache is null) { setupMessage = "Import or verify game data before importing recipes."; break; }
                    string? recipes = options.SetupFlowSmokeRoot is not null && options.RecipeReferenceRoot is { } reference
                        ? Path.Combine(reference, "recipes", "viewer.json") : WindowsFilePicker.ChooseRecipeLibrary();
                    if (recipes is not null) BeginRecipeImport(recipes);
                    break;
                case 3:
                    if (setup.ResumeSource() is { } source) BeginImport(source);
                    else setupMessage = "No previous source is available. Select your original ISO.";
                    break;
                case 2: setupOperation!.Validate(); setupMessage = "Checking local game data"; break;
                case 4: setup.RestartWork(); setupMessage = "Interrupted work retained separately. Select your ISO or resume to restart."; break;
                case 5: screen = ScreenId.Preferences; menuIndex = 0; break;
                case 6: setupMessage = setup.RestorePreferences().Issue?.Message ?? "Previous preferences restored."; editor!.ResetDraft(); break;
                case 7: OpenAssetDiagnostics(); break;
                case 8: screen = ScreenId.PcTools; break;
            }
        }
        catch (Exception e) when (SetupStore.Recoverable(e)) { setupMessage = "The operation could not finish. Check local storage and retry."; }
    }
    private void UpdatePreferences(GuiButtons buttons)
    {
        if (setup is null) return;
        if (buttons.Back) { screen = ScreenId.PcTools; menuIndex = 0; return; }
        MoveMenu(buttons, 4);
        if (!buttons.Confirm && !buttons.Left && !buttons.Right) return;
        var value = setup.Preferences;
        if (menuIndex == 0) value = value with { OpponentSeed = unchecked(value.OpponentSeed + (buttons.Left ? uint.MaxValue : 1u)) };
        if (menuIndex == 1) value = value with { IntegerScaling = !value.IntegerScaling };
        if (menuIndex == 2) value = value with { OpponentSeed = 7, IntegerScaling = false };
        if (menuIndex == 3) { screen = ScreenId.PcTools; menuIndex = 0; return; }
        setup.SavePreferences(value);
        preferencesNotice = "Preferences saved.";
    }
    private void UpdateEditor(GuiButtons buttons)
    {
        switch (editor?.Update(buttons))
        {
            case DeckEditorRequest.SavedDecks: screen = ScreenId.SavedDecks; menuIndex = 0; break;
            case DeckEditorRequest.Play: LaunchDraft(); break;
            case DeckEditorRequest.Back: screen = ScreenId.PcTools; menuIndex = 0; break;
        }
    }
    private void UpdateSavedDecks(GuiButtons buttons)
    {
        if (buttons.Back) { screen = ScreenId.DeckEditor; return; }
        if (editor!.SavedDecks.Count == 0) { if (buttons.Confirm) screen = ScreenId.DeckEditor; return; }
        MoveMenu(buttons, editor.SavedDecks.Count);
        if (!buttons.Confirm) return;
        editor.LoadSaved(menuIndex);
        screen = ScreenId.DeckEditor;
    }
    private void DrawSetupScreen()
    {
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        renderer.Text("LOCAL GAME SETUP", 12, 12, gold, 2);
        renderer.Wrapped(setupMessage, 12, 36, 75, 3, muted);
        if (setupOperation?.Busy == true)
        {
            renderer.Wrapped(setupOperation!.Progress.Message, 12, 87, 75, 3);
            renderer.Fill(new(12, 125, 450, 12), new Color(30, 43, 60));
            renderer.Fill(new(12, 125, Math.Clamp(setupOperation!.Progress.Percent, 0, 100) * 450 / 100, 12), new Color(75, 174, 177));
            renderer.Text($"{setupOperation!.Progress.Percent}%   ESC/B: STOP SAFELY", 12, 153);
        }
        else
            for (int i = 0; i < SetupActions.Length; i++)
                renderer.Text((menuIndex == i ? "> " : "  ") + SetupActions[i] + (i == 0 && !CanContinueToGame ? " (IMPORT REQUIRED)" : ""),
                    12, 77 + i * 15, i == 0 && !CanContinueToGame ? muted : menuIndex == i ? gold : Color.White);
        renderer.Text("ESC/B: BACK   ORIGINALS READ-ONLY. ASSETS LOCAL.", 12, 251, muted);
    }
    private void DrawEditor()
    {
        if (editor is null || editor.Draft is not { } draft) return;
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        renderer.Text("DECK EDITOR", 8, 8, gold, 2);
        renderer.Text(draft.Name.Length > 30 ? draft.Name[..30] : draft.Name, 202, 12);
        if (editor.EditingText is not null)
        {
            renderer.Text(editor.EditingName ? "DECK NAME" : "SEARCH CARD NAME OR ID", 12, 55, gold);
            renderer.Wrapped(editor.EditingText + "_", 12, 78, 70, 3);
            renderer.Text("TYPE TO EDIT. ENTER: APPLY  ESC: CANCEL", 12, 131, muted);
            return;
        }
        if (editor.MenuOpen)
        {
            for (int i = 0; i < DeckEditorSession.Actions.Count; i++)
                renderer.Text((editor.MenuIndex == i ? "> " : "  ") + DeckEditorSession.Actions[i], 12, 44 + i * 20, editor.MenuIndex == i ? gold : Color.White);
            return;
        }
        renderer.Text($"{draft.Section} / {draft.Filter}  SEARCH: {draft.Search}", 8, 32, muted);
        var cards = draft.VisibleCards();
        editor.CardIndex = cards.Count == 0 ? 0 : Math.Clamp(editor.CardIndex, 0, cards.Count - 1);
        int start = editor.CardIndex / 8 * 8;
        for (int i = start; i < Math.Min(start + 8, cards.Count); i++)
        {
            var c = cards[i]; int y = 49 + (i - start) * 17;
            if (i == editor.CardIndex) renderer.Fill(new(6, y - 2, 299, 15), new Color(42, 59, 74));
            string name = c.Name.Length > 35 ? c.Name[..35] : c.Name;
            renderer.Text(name, 10, y, i == editor.CardIndex ? gold : Color.White);
            renderer.Text($"{draft.Count(c.GameCardId, draft.Section)}/{draft.Count(c.GameCardId)}", 270, y, muted);
        }
        if (cards.Count == 0) renderer.Text("NO MATCHING SUPPORTED CARDS", 10, 51, muted);
        else
        {
            var card = cards[editor.CardIndex];
            renderer.Wrapped(card.Name, 316, 49, 26, 2, gold);
            renderer.Text($"ATK {card.Statistics.Attack} DEF {card.Statistics.Defense}", 316, 74, muted);
            renderer.Wrapped(card.Description, 316, 91, 26, 10, muted);
        }
        var legal = draft.Validate();
        renderer.Text($"MAIN {draft.Cards(DeckSection.Main).Count}  EXTRA {draft.Cards(DeckSection.Extra).Count}  SIDE {draft.Cards(DeckSection.Side).Count}  {legal.Legality}", 8, 194, legal.CanPlay ? Color.Cyan : gold);
        renderer.Wrapped(legal.CanPlay ? "Ready for supported play." : legal.Diagnostics.FirstOrDefault()?.Detail ?? "Deck requires attention.", 8, 209, 76, 2, muted);
        renderer.Wrapped(editor.Notice, 8, 239, 77, 3, gold);
        renderer.Text("UP/DOWN:CARD  RIGHT:+ LEFT:- TAB:SECTION ENTER:MENU", 8, 265, muted);
    }
    private void RunSetupSmoke()
    {
        if (!SetupAutomation || duel is not null) return;
        if (setupOperation?.Busy == true) return;
        if (setup?.Cache is null) throw new InvalidOperationException(setupMessage);
        if (setupSmokeStage == 0)
        {
            OpenEditor(); var draft = editor!.Draft!; draft.Name = "Setup acceptance";
            uint id = draft.Cards(DeckSection.Main)[0]; draft.Remove(id); draft.Add(id);
            editor.Save(); editor.ReloadDraft();
            setupSmokeStage = 1; editorFrame = frames; return;
        }
        if (frames <= editorFrame + 1) return;
        if (!LaunchDraft()) throw new InvalidOperationException(editor!.Notice);
        Console.WriteLine($"GUI_SETUP ready cache={(options.SetupSource is null ? "reused" : "imported")} saved=true reloaded=true legal=true");
    }
    private bool capturedEditor;
    private void CaptureEditor()
    {
        if (capturedEditor || screen != ScreenId.DeckEditor || options.EditorCapture is not { } path) return;
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        canvas.SaveAsPng(output, 480, 272); capturedEditor = true;
    }
    private void DrawSetupLists()
    {
        var gold = new Color(238, 196, 104);
        renderer.Text(screen == ScreenId.Preferences ? "PREFERENCES" : "SAVED DECK REVISIONS", 12, 12, gold, 2);
        string[] rows = screen == ScreenId.Preferences
            ? [$"OPPONENT SEED: {setup?.Preferences.OpponentSeed}", $"INTEGER SCALING: {(setup?.Preferences.IntegerScaling == true ? "ON" : "OFF")}", "RESET DISPLAY/SEED PREFERENCES", "BACK"]
            : editor!.SavedDecks.Select(d =>
            {
                string revision = Path.GetFileNameWithoutExtension(d.Key);
                return d.Name + " / " + revision[..Math.Min(8, revision.Length)];
            }).ToArray();
        int start = menuIndex / 8 * 8;
        for (int i = start; i < Math.Min(start + 8, rows.Length); i++)
            renderer.Wrapped((menuIndex == i ? "> " : "  ") + rows[i], 12, 55 + (i - start) * 22, 75, 2, menuIndex == i ? gold : Color.White);
        if (rows.Length == 0) renderer.Text("NO SAVED REVISIONS. ESC: BACK", 12, 60);
        if (screen == ScreenId.Preferences) renderer.Wrapped(preferencesNotice, 12, 204, 75, 3, gold);
        renderer.Text("UP/DOWN: SELECT  ENTER: APPLY  ESC: BACK", 12, 247, gold);
    }

}
