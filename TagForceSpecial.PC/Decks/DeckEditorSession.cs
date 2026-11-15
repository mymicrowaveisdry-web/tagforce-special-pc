using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Decks;

public enum DeckEditorRequest { None, SavedDecks, Play, Back }
public sealed record DeckLaunchResult(DuelController? Controller, string? SetupError = null);

/// <summary>Owns an editable deck session and revision transactions; returns navigation requests to the host.</summary>
public sealed class DeckEditorSession(SetupStore store)
{
    private readonly DeckLibrary library = new(store.Storage);
    public DeckDraft? Draft { get; private set; }
    public string? DraftKey { get; private set; }
    public IReadOnlyList<SavedDeck> SavedDecks { get; private set; } = [];
    public int CardIndex { get; set; }
    public int MenuIndex { get; private set; }
    public bool MenuOpen { get; private set; }
    public string? EditingText { get; private set; }
    public bool EditingName { get; private set; }
    public string Notice { get; set; } = "Right: add  Left: remove  Tab: section  Enter: menu";
    public static IReadOnlyList<string> Actions { get; } = Array.AsReadOnly(new[]
    {
        "SAVE NEW REVISION", "NAME DECK", "SEARCH CARDS", "CHANGE FILTER", "LOAD SAVED REVISION",
        "NEW EMPTY DECK", "INITIAL SUPPORTED DECK", "PLAY THIS DECK", "BACK"
    });

    public void Open()
    {
        EnsureDraft();
        CardIndex = 0; MenuOpen = false;
    }

    /// <summary>Loads the selected saved revision, else the built-in deck, once per session; the native Deck Edit shares this draft.</summary>
    public void EnsureDraft()
    {
        if (Draft is not null || store.Cache is null) return;
        if (store.Preferences.SelectedDeck is { } key)
        {
            try { Draft = new(store.Cache.Catalog.Catalog, library.Load(key)); DraftKey = key; }
            catch (Exception e) when (SetupStore.Recoverable(e))
            { Notice = "Saved revision is unreadable. Load an earlier revision or use a new deck."; }
        }
        Draft ??= new(store.Cache.Catalog.Catalog, InitialOpponentDecks.Create(0));
    }

    public void ResetDraft() => Draft = null;
    public void ReloadDraft() => Draft = new(store.Cache!.Catalog.Catalog, library.Load(DraftKey!));

    public void Save()
    {
        if (Draft is null) return;
        var deck = Draft.Snapshot(Guid.NewGuid().ToString("N"));
        string key = library.Save(deck, DraftKey);
        store.SavePreferences(store.Preferences with { SelectedDeck = key });
        DraftKey = key;
        Notice = Draft.Validate().CanPlay ? "Revision saved. This deck is ready to play." : "Draft saved. Fix the legality messages before playing.";
    }

    public DeckLaunchResult Launch(uint seed)
    {
        if (Draft is null) return new(null);
        var result = store.ValidateActive();
        if (!result.Success) return new(null, result.Issue!.Message);
        if (!Draft.Validate().CanPlay) { Notice = "Fix deck legality before starting a duel."; return new(null); }
        Save();
        // A duel receives the persisted revision rather than mutable editor cards.
        var saved = library.Load(DraftKey!);
        return new(SupportedDuelFactory.Create(store.Cache!, saved, seed));
    }

    public void LoadSaved(int index)
    {
        var selected = SavedDecks[index];
        if (selected.Deck is null) { Notice = selected.Error!; return; }
        Draft = new(store.Cache!.Catalog.Catalog, library.Load(selected.Key)); DraftKey = selected.Key;
        store.SavePreferences(store.Preferences with { SelectedDeck = selected.Key });
        CardIndex = 0;
    }

    public void TypeCharacter(char character)
    {
        if (EditingText is null) return;
        if (character == '\b') { if (EditingText.Length > 0) EditingText = EditingText[..^1]; }
        else if (!char.IsControl(character) && EditingText.Length < 48) EditingText += character;
    }

    public DeckEditorRequest Update(GuiButtons buttons)
    {
        if (Draft is null) return DeckEditorRequest.None;
        if (EditingText is not null)
        {
            if (buttons.Back) { EditingText = null; return DeckEditorRequest.None; }
            if (buttons.Confirm)
            {
                if (EditingName && string.IsNullOrWhiteSpace(EditingText)) { Notice = "Enter a name for this deck."; return DeckEditorRequest.None; }
                if (EditingName) Draft.Name = EditingText; else Draft.Search = EditingText;
                EditingText = null; CardIndex = 0;
            }
            return DeckEditorRequest.None;
        }
        if (MenuOpen)
        {
            if (buttons.Back) { MenuOpen = false; return DeckEditorRequest.None; }
            if (buttons.Up) MenuIndex = (MenuIndex + Actions.Count - 1) % Actions.Count;
            if (buttons.Down) MenuIndex = (MenuIndex + 1) % Actions.Count;
            if (!buttons.Confirm) return DeckEditorRequest.None;
            MenuOpen = false;
            switch (MenuIndex)
            {
                case 0: Save(); break;
                case 1: EditingName = true; EditingText = Draft.Name; break;
                case 2: EditingName = false; EditingText = Draft.Search; break;
                case 3: Draft.Filter = (DeckFilter)(((int)Draft.Filter + 1) % 3); CardIndex = 0; break;
                case 4: SavedDecks = library.List(); return DeckEditorRequest.SavedDecks;
                case 5: Draft = new(Draft.Catalog, InitialOpponentDecks.Create(0)); Draft.Clear(); Draft.Name = "New deck"; DraftKey = null; break;
                case 6: Draft = new(Draft.Catalog, InitialOpponentDecks.Create(0)); DraftKey = null; break;
                case 7: return DeckEditorRequest.Play;
                case 8: return DeckEditorRequest.Back;
            }
            return DeckEditorRequest.None;
        }
        if (buttons.Back) { MenuOpen = true; MenuIndex = 0; return DeckEditorRequest.None; }
        var cards = Draft.VisibleCards();
        if (cards.Count > 0)
        {
            if (buttons.Up) CardIndex = (CardIndex + cards.Count - 1) % cards.Count;
            if (buttons.Down) CardIndex = (CardIndex + 1) % cards.Count;
            CardIndex = Math.Clamp(CardIndex, 0, cards.Count - 1);
            if (buttons.Right) Draft.Add(cards[CardIndex].GameCardId);
            if (buttons.Left) Draft.Remove(cards[CardIndex].GameCardId);
        }
        if (buttons.Inspect) Draft.Section = (DeckSection)(((int)Draft.Section + 1) % 3);
        if (buttons.Confirm) { MenuOpen = true; MenuIndex = 0; }
        return DeckEditorRequest.None;
    }
}
