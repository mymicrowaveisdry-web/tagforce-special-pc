using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Game;

public sealed partial class GameLoop
{
    private SetupStore? setup;
    private DeckLibrary? library;
    private DeckDraft? draft;
    private string? draftKey;
    private IReadOnlyList<SavedDeck> savedDecks = [];
    private Task<SetupResult>? setupTask;
    private CancellationTokenSource? importCancellation;
    private readonly ConcurrentQueue<ImportProgress> importProgress = new();
    private ImportProgress progress = new(0, "Select your original ISO to begin");
    private int cardIndex, editorMenuIndex, setupSmokeStage, editorFrame;
    private bool editorMenu;
    private string? editingText;
    private bool editingName;
    private string editorNotice = "Right: add  Left: remove  Tab: section  Enter: menu";
    private string preferencesNotice = "Changes are saved locally.";
    private static readonly string[] SetupActions = ["SELECT ORIGINAL ISO", "RESUME IMPORT", "VERIFY CURRENT CACHE", "REBUILD FROM ORIGINAL ISO", "RESTART INTERRUPTED WORK", "PREFERENCES", "RESTORE SETTINGS BACKUP", "RESOURCE STATUS", "BACK"];
    private static readonly string[] EditorActions = ["SAVE NEW REVISION", "NAME DECK", "SEARCH CARDS", "CHANGE FILTER", "LOAD SAVED REVISION", "NEW EMPTY DECK", "INITIAL SUPPORTED DECK", "PLAY THIS DECK", "BACK"];

    private void InitializeSetup()
    {
        if (options.Smoke && !SetupAutomation) return;
        string root = options.DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TagForceSpecial");
        setup = new(root); library = new(setup.Storage);
        Window.TextInput += (_, e) =>
        {
            if (editingText is null) return;
            if (e.Character == '\b') { if (editingText.Length > 0) editingText = editingText[..^1]; }
            else if (!char.IsControl(e.Character) && editingText.Length < 48) editingText += e.Character;
        };
        if (setup.Issue is { } issue) setupMessage = issue.Message;
        else if (options.SetupSource is null && setup.Preferences.ActiveGeneration is not null)
        {
            progress = new(0, "Checking existing local game data");
            setupTask = Task.Run(setup.ValidateActive);
        }
        if (options.SetupSource is { } source) { screen = ScreenId.Setup; BeginImport(source); }
    }
    private void BeginImport(string source)
    {
        if (setup is null || setupTask is { IsCompleted: false }) return;
        importCancellation?.Dispose(); importCancellation = new();
        var token = importCancellation.Token;
        progress = new(0, "Checking original image"); setupMessage = "Importing local game data";
        setupTask = Task.Run(() => setup.Import(source, new QueuedProgress(importProgress), token));
        screen = ScreenId.Setup;
    }
    private uint CurrentSeed => options.SeedSpecified ? options.Seed : setup?.Preferences.OpponentSeed ?? options.Seed;
    private bool SetupAutomation => options.Smoke && (options.SetupSource is not null || options.CachePath is null && options.DataRoot is not null);
    private void PollSetupTask()
    {
        while (importProgress.TryDequeue(out var update)) progress = update;
        if (setupTask is not { IsCompleted: true }) return;
        try
        {
            var result = setupTask.GetAwaiter().GetResult();
            setupMessage = result.Success ? "Local game data is ready. Open Decks to prepare and play." : result.Issue!.Message;
        }
        catch (Exception e) when (SetupStore.Recoverable(e)) { setupMessage = "Setup could not finish. Retry or select your ISO again."; }
        setupTask = null;
    }
    private void OpenEditor()
    {
        if (setup?.Cache is null) { screen = ScreenId.Setup; menuIndex = 0; return; }
        if (draft is null)
        {
            if (setup.Preferences.SelectedDeck is { } key)
            {
                try { draft = new(setup.Cache.Catalog.Catalog, library!.Load(key)); draftKey = key; }
                catch (Exception e) when (SetupStore.Recoverable(e)) { editorNotice = "Saved revision is unreadable. Load an earlier revision or use a new deck."; }
            }
            draft ??= new(setup.Cache.Catalog.Catalog, InitialOpponentDecks.Create(0));
        }
        screen = ScreenId.DeckEditor; cardIndex = 0; editorMenu = false;
    }
    private void SaveDraft()
    {
        if (draft is null || setup is null || library is null) return;
        var deck = draft.Snapshot(Guid.NewGuid().ToString("N"));
        string key = library.Save(deck, draftKey);
        setup.SavePreferences(setup.Preferences with { SelectedDeck = key });
        draftKey = key;
        editorNotice = draft.Validate().CanPlay ? "Revision saved. This deck is ready to play." : "Draft saved. Fix the legality messages before playing.";
    }
    private bool LaunchDraft()
    {
        if (setup is null || draft is null) return false;
        var result = setup.ValidateActive();
        if (!result.Success) { setupMessage = result.Issue!.Message; screen = ScreenId.Setup; return false; }
        if (!draft.Validate().CanPlay) { editorNotice = "Fix deck legality before starting a duel."; return false; }
        SaveDraft();
        // Reload the persisted revision rather than passing a mutable editor list into the duel.
        var saved = library!.Load(draftKey!);
        duel = SupportedDuelFactory.Create(setup.Cache!, saved, CurrentSeed);
        screen = ScreenId.Duel; choiceIndex = 0; inspectIndex = -1;
        return true;
    }
    private void UpdateSetupScreen(GuiButtons buttons)
    {
        if (setupTask is not null)
        { if (buttons.Back) { importCancellation?.Cancel(); setupMessage = "Stopping safely..."; } return; }
        if (buttons.Back) { screen = ScreenId.MainMenu; menuIndex = 0; return; }
        MoveMenu(buttons, SetupActions.Length);
        if (!buttons.Confirm || setup is null) return;
        try
        {
            switch (menuIndex)
            {
                case 0: case 3:
                    string? file = WindowsFilePicker.ChooseIso(); if (file is not null) BeginImport(file); break;
                case 1:
                    if (setup.ResumeSource() is { } source) BeginImport(source);
                    else setupMessage = "No previous source is available. Select your original ISO.";
                    break;
                case 2: setupTask = Task.Run(setup.ValidateActive); setupMessage = "Checking local game data"; break;
                case 4: setup.RestartWork(); setupMessage = "Interrupted work retained separately. Select your ISO or resume to restart."; break;
                case 5: screen = ScreenId.Preferences; menuIndex = 0; break;
                case 6: setupMessage = setup.RestorePreferences().Issue?.Message ?? "Previous preferences restored."; draft = null; break;
                case 7: setupMessage = setup.Resources is { } resources
                    ? $"Imported {setup.Cache!.Catalog.Catalog.Count} card definitions and {resources.Cards.Count} image entries. Card art, fonts and audio are not yet available."
                    : "No verified resource index is active. Import or rebuild from your original ISO."; break;
                case 8: screen = ScreenId.MainMenu; menuIndex = 0; break;
            }
        }
        catch (Exception e) when (SetupStore.Recoverable(e)) { setupMessage = "The operation could not finish. Check local storage and retry."; }
    }
    private void UpdatePreferences(GuiButtons buttons)
    {
        if (setup is null) return;
        if (buttons.Back) { screen = ScreenId.MainMenu; menuIndex = 0; return; }
        MoveMenu(buttons, 4);
        if (!buttons.Confirm && !buttons.Left && !buttons.Right) return;
        var value = setup.Preferences;
        if (menuIndex == 0) value = value with { OpponentSeed = unchecked(value.OpponentSeed + (buttons.Left ? uint.MaxValue : 1u)) };
        if (menuIndex == 1) value = value with { IntegerScaling = !value.IntegerScaling };
        if (menuIndex == 2) value = value with { OpponentSeed = 7, IntegerScaling = false };
        if (menuIndex == 3) { screen = ScreenId.MainMenu; menuIndex = 0; return; }
        setup.SavePreferences(value);
        preferencesNotice = "Preferences saved.";
    }
    private void UpdateEditor(GuiButtons buttons)
    {
        if (draft is null) return;
        if (editingText is not null)
        {
            if (buttons.Back) { editingText = null; return; }
            if (buttons.Confirm)
            {
                if (editingName && string.IsNullOrWhiteSpace(editingText)) { editorNotice = "Enter a name for this deck."; return; }
                if (editingName) draft.Name = editingText; else draft.Search = editingText;
                editingText = null; cardIndex = 0;
            }
            return;
        }
        if (editorMenu)
        {
            if (buttons.Back) { editorMenu = false; return; }
            if (buttons.Up) editorMenuIndex = (editorMenuIndex + EditorActions.Length - 1) % EditorActions.Length;
            if (buttons.Down) editorMenuIndex = (editorMenuIndex + 1) % EditorActions.Length;
            if (!buttons.Confirm) return;
            editorMenu = false;
            switch (editorMenuIndex)
            {
                case 0: SaveDraft(); break;
                case 1: editingName = true; editingText = draft.Name; break;
                case 2: editingName = false; editingText = draft.Search; break;
                case 3: draft.Filter = (DeckFilter)(((int)draft.Filter + 1) % 3); cardIndex = 0; break;
                case 4: savedDecks = library!.List(); screen = ScreenId.SavedDecks; menuIndex = 0; break;
                case 5: draft = new(draft.Catalog, InitialOpponentDecks.Create(0)); draft.Clear(); draft.Name = "New deck"; draftKey = null; break;
                case 6: draft = new(draft.Catalog, InitialOpponentDecks.Create(0)); draftKey = null; break;
                case 7: LaunchDraft(); break;
                case 8: screen = ScreenId.MainMenu; menuIndex = 0; break;
            }
            return;
        }
        if (buttons.Back) { editorMenu = true; editorMenuIndex = 0; return; }
        var cards = draft.VisibleCards();
        if (cards.Count > 0)
        {
            if (buttons.Up) cardIndex = (cardIndex + cards.Count - 1) % cards.Count;
            if (buttons.Down) cardIndex = (cardIndex + 1) % cards.Count;
            cardIndex = Math.Clamp(cardIndex, 0, cards.Count - 1);
            if (buttons.Right) draft.Add(cards[cardIndex].GameCardId);
            if (buttons.Left) draft.Remove(cards[cardIndex].GameCardId);
        }
        if (buttons.Inspect) draft.Section = (DeckSection)(((int)draft.Section + 1) % 3);
        if (buttons.Confirm) { editorMenu = true; editorMenuIndex = 0; }
    }
    private void UpdateSavedDecks(GuiButtons buttons)
    {
        if (buttons.Back) { screen = ScreenId.DeckEditor; return; }
        if (savedDecks.Count == 0) { if (buttons.Confirm) screen = ScreenId.DeckEditor; return; }
        MoveMenu(buttons, savedDecks.Count);
        if (!buttons.Confirm) return;
        var selected = savedDecks[menuIndex];
        if (selected.Deck is null) { editorNotice = selected.Error!; screen = ScreenId.DeckEditor; return; }
        draft = new(setup!.Cache!.Catalog.Catalog, library!.Load(selected.Key)); draftKey = selected.Key;
        setup.SavePreferences(setup.Preferences with { SelectedDeck = selected.Key });
        screen = ScreenId.DeckEditor; cardIndex = 0;
    }
    private void DrawSetupScreen()
    {
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        renderer.Text("LOCAL GAME SETUP", 12, 12, gold, 2);
        renderer.Wrapped(setupMessage, 12, 36, 75, 3, muted);
        if (setupTask is not null)
        {
            renderer.Wrapped(progress.Message, 12, 87, 75, 3);
            renderer.Fill(new(12, 125, 450, 12), new Color(30, 43, 60));
            renderer.Fill(new(12, 125, Math.Clamp(progress.Percent, 0, 100) * 450 / 100, 12), new Color(75, 174, 177));
            renderer.Text($"{progress.Percent}%   ESC/B: STOP SAFELY", 12, 153);
        }
        else
            for (int i = 0; i < SetupActions.Length; i++)
                renderer.Text((menuIndex == i ? "> " : "  ") + SetupActions[i], 12, 82 + i * 19, menuIndex == i ? gold : Color.White);
        renderer.Text("ORIGINALS STAY READ-ONLY. ASSETS STAY LOCAL.", 12, 251, muted);
    }
    private void DrawEditor()
    {
        if (draft is null) return;
        var gold = new Color(238, 196, 104); var muted = new Color(143, 162, 187);
        renderer.Text("DECK EDITOR", 8, 8, gold, 2);
        renderer.Text(draft.Name.Length > 30 ? draft.Name[..30] : draft.Name, 202, 12);
        if (editingText is not null)
        {
            renderer.Text(editingName ? "DECK NAME" : "SEARCH CARD NAME OR ID", 12, 55, gold);
            renderer.Wrapped(editingText + "_", 12, 78, 70, 3);
            renderer.Text("TYPE TO EDIT. ENTER: APPLY  ESC: CANCEL", 12, 131, muted);
            return;
        }
        if (editorMenu)
        {
            for (int i = 0; i < EditorActions.Length; i++)
                renderer.Text((editorMenuIndex == i ? "> " : "  ") + EditorActions[i], 12, 44 + i * 20, editorMenuIndex == i ? gold : Color.White);
            return;
        }
        renderer.Text($"{draft.Section} / {draft.Filter}  SEARCH: {draft.Search}", 8, 32, muted);
        var cards = draft.VisibleCards();
        cardIndex = cards.Count == 0 ? 0 : Math.Clamp(cardIndex, 0, cards.Count - 1);
        int start = cardIndex / 8 * 8;
        for (int i = start; i < Math.Min(start + 8, cards.Count); i++)
        {
            var c = cards[i]; int y = 49 + (i - start) * 17;
            if (i == cardIndex) renderer.Fill(new(6, y - 2, 299, 15), new Color(42, 59, 74));
            string name = c.Name.Length > 35 ? c.Name[..35] : c.Name;
            renderer.Text(name, 10, y, i == cardIndex ? gold : Color.White);
            renderer.Text($"{draft.Count(c.GameCardId, draft.Section)}/{draft.Count(c.GameCardId)}", 270, y, muted);
        }
        if (cards.Count == 0) renderer.Text("NO MATCHING SUPPORTED CARDS", 10, 51, muted);
        else
        {
            var card = cards[cardIndex];
            renderer.Wrapped(card.Name, 316, 49, 26, 2, gold);
            renderer.Text($"ATK {card.Statistics.Attack} DEF {card.Statistics.Defense}", 316, 74, muted);
            renderer.Wrapped(card.Description, 316, 91, 26, 10, muted);
        }
        var legal = draft.Validate();
        renderer.Text($"MAIN {draft.Cards(DeckSection.Main).Count}  EXTRA {draft.Cards(DeckSection.Extra).Count}  SIDE {draft.Cards(DeckSection.Side).Count}  {legal.Legality}", 8, 194, legal.CanPlay ? Color.Cyan : gold);
        renderer.Wrapped(legal.CanPlay ? "Ready for supported play." : legal.Diagnostics.FirstOrDefault()?.Detail ?? "Deck requires attention.", 8, 209, 76, 2, muted);
        renderer.Wrapped(editorNotice, 8, 239, 77, 3, gold);
        renderer.Text("UP/DOWN:CARD  RIGHT:+ LEFT:- TAB:SECTION ENTER:MENU", 8, 265, muted);
    }
    private void RunSetupSmoke()
    {
        if (!SetupAutomation || duel is not null) return;
        if (setupTask is not null) return;
        if (setup?.Cache is null) throw new InvalidOperationException(setupMessage);
        if (setupSmokeStage == 0)
        {
            OpenEditor(); draft!.Name = "Setup acceptance";
            uint id = draft.Cards(DeckSection.Main)[0]; draft.Remove(id); draft.Add(id);
            SaveDraft(); draft = new(setup.Cache.Catalog.Catalog, library!.Load(draftKey!));
            setupSmokeStage = 1; editorFrame = frames; return;
        }
        if (frames <= editorFrame + 1) return;
        if (!LaunchDraft()) throw new InvalidOperationException(editorNotice);
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
            : savedDecks.Select(d =>
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
    private sealed class QueuedProgress(ConcurrentQueue<ImportProgress> queue) : IProgress<ImportProgress>
    { public void Report(ImportProgress value) => queue.Enqueue(value); }
}
