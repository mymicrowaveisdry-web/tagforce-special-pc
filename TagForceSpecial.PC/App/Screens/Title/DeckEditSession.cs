using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.PC.App.Screens.Title;

public enum DeckEditTab { Trunk, Deck, Extra, Side }
public enum DeckEditPrompt { None, Move, ExitQuestion, SaveQuestion }
public enum DeckEditOutcome { None, Leave, LeaveAndSave }

/// <summary>One visible list entry: the card and its counts as the original's row shows them.</summary>
public sealed record DeckEditEntry(CardDefinition Card, int Owned, int InMainOrExtra, int InSide, int InTrunk);

/// <summary>Observed Deck Edit core behaviour (Phase 6.2a, phase-62a handoff): Square cycles Trunk, Deck, EX, Side; seven rows are visible and
/// the cursor wraps at both ends; confirm picks a card and asks where it goes ("The card will be moved to %s."), confirm again moves it,
/// back cancels; an empty Side does nothing; back on the list asks "Exit Deck Edit and return to the game. Is this OK?" (OK focused), then
/// "Save all contents acquired so far?" (YES focused). PC confirm is PSP Circle in the original and PSP Cross here; PC back is the reverse.
/// The Trunk lists the cards the PC supports, owned <see cref="OwnedCopies"/> times each: it is not the original save's collection.
/// Triangle opens the Deck Edit Menu (<see cref="DeckEditMenus"/>); the Trunk list follows <see cref="Query"/>.</summary>
public sealed class DeckEditSession
{
    public const int OwnedCopies = 3;
    private readonly DeckDraft draft;
    private readonly IReadOnlyList<CardDefinition> pool;
    private readonly int visible;
    private readonly string initial;
    private static readonly DeckSection[] Sections = [DeckSection.Main, DeckSection.Extra, DeckSection.Side];
    private static readonly Lazy<DeckEditProfile> EmbeddedProfile = new(DeckEditProfile.LoadEmbedded);
    private static readonly Lazy<DeckEditMenuProfile> EmbeddedMenus = new(DeckEditMenuProfile.LoadEmbedded);
    private readonly Dictionary<DeckSection, uint[]> entered;
    private Dictionary<DeckSection, uint[]>? undo;
    public DeckEditTab Tab { get; private set; } = DeckEditTab.Trunk;
    public int Cursor { get; private set; }
    public int Scroll { get; private set; }
    public DeckEditPrompt Prompt { get; private set; }
    /// <summary>0 = OK/YES (the observed default), 1 = CANCEL/NO.</summary>
    public int PromptFocus { get; private set; }
    public DeckDraft Draft => draft;
    public bool Changed => Fingerprint() != initial;
    public DeckEditQuery Query { get; }
    public DeckEditMenus Menus { get; }
    /// <summary>Undo Previous Move is available (greyed otherwise).</summary>
    public bool CanUndo => undo is not null;

    public DeckEditSession(DeckDraft draft, IEnumerable<CardDefinition> pool, int visibleRows, DeckEditProfile? profile = null,
        DeckEditMenuProfile? menuProfile = null)
    {
        this.draft = draft;
        this.pool = pool.OrderBy(c => c.GameCardId).ToArray();
        visible = visibleRows;
        initial = Fingerprint();
        entered = Snapshot();
        var frames = profile ?? EmbeddedProfile.Value;
        Query = new(card => frames.FrameKey(card.Codes?.RawKind ?? 0) is { } key ? DeckEditQuery.FrameCell(key.Value) : null);
        Menus = new(menuProfile ?? EmbeddedMenus.Value);
    }

    private Dictionary<DeckSection, uint[]> Snapshot() => Sections.ToDictionary(s => s, s => draft.Cards(s).ToArray());
    private void Restore(Dictionary<DeckSection, uint[]> state) { foreach (var (section, cards) in state) draft.ReplaceSection(section, cards); }

    private string Fingerprint() => string.Join('|', new[] { DeckSection.Main, DeckSection.Extra, DeckSection.Side }.Select(s => string.Join(',', draft.Cards(s))));

    public static DeckSection SectionOf(DeckEditTab tab) => tab switch
    {
        DeckEditTab.Extra => DeckSection.Extra, DeckEditTab.Side => DeckSection.Side, _ => DeckSection.Main
    };

    /// <summary>Extra Deck monsters by their confirmed rules; otherwise Fusion and Synchro kinds (the original shows them on the EX tab).</summary>
    public static bool IsExtra(CardDefinition card) => ConfirmedCardPool.Contains(card.GameCardId)
        ? ConfirmedCardPool.IsExtraDeck(card.GameCardId) : card.Codes?.RawKind is 3 or 18;

    private DeckEditEntry Entry(CardDefinition card)
    {
        int main = draft.Count(card.GameCardId, IsExtra(card) ? DeckSection.Extra : DeckSection.Main), side = draft.Count(card.GameCardId, DeckSection.Side);
        return new(card, OwnedCopies, main, side, Math.Max(0, OwnedCopies - main - side));
    }

    private IEnumerable<DeckEditEntry> Trunk() => pool.Select(Entry).Where(e => e.InTrunk > 0);

    public IReadOnlyList<DeckEditEntry> Entries(DeckEditTab tab) => tab == DeckEditTab.Trunk
        ? Query.Apply(Trunk())
        : draft.Cards(SectionOf(tab)).Select(id => Entry(draft.Catalog[id])).ToArray();

    public IReadOnlyList<DeckEditEntry> Current => Entries(Tab);
    public DeckEditEntry? Focused => Current.Count == 0 ? null : Current[Math.Min(Cursor, Current.Count - 1)];
    public int Count(DeckEditTab tab) => tab == DeckEditTab.Trunk ? pool.Sum(c => Entry(c).InTrunk) : draft.Cards(SectionOf(tab)).Count;
    public int CardTypesInPossession => pool.Count;

    /// <summary>Destination named by the move prompt: Trunk cards go to their deck (Deck or EX), deck cards back to the Trunk.</summary>
    public DeckEditTab Destination => Tab == DeckEditTab.Trunk ? Focused is { } e && IsExtra(e.Card) ? DeckEditTab.Extra : DeckEditTab.Deck : DeckEditTab.Trunk;

    public DeckEditOutcome Handle(PspInputFrame input)
    {
        if (Menus.Open) { Menus.Handle(input, this); return DeckEditOutcome.None; }
        bool confirm = input.IsPressed(PspAction.Cross), back = input.IsPressed(PspAction.Circle);
        switch (Prompt)
        {
            case DeckEditPrompt.Move:
                if (confirm) { Move(); Prompt = DeckEditPrompt.None; }
                else if (back) Prompt = DeckEditPrompt.None;
                return DeckEditOutcome.None;
            case DeckEditPrompt.ExitQuestion:
            case DeckEditPrompt.SaveQuestion:
                if (input.IsPressed(PspAction.Left) || input.IsPressed(PspAction.Right)) PromptFocus = 1 - PromptFocus;
                if (Prompt == DeckEditPrompt.ExitQuestion)
                {
                    if (back || confirm && PromptFocus == 1) { Prompt = DeckEditPrompt.None; PromptFocus = 0; return DeckEditOutcome.None; }
                    if (!confirm) return DeckEditOutcome.None;
                    // Observed: OK asks whether to save; nothing changed still asks in the original, as on Deck Menu returns.
                    Prompt = DeckEditPrompt.SaveQuestion; PromptFocus = 0;
                    return DeckEditOutcome.None;
                }
                if (confirm) return PromptFocus == 0 ? DeckEditOutcome.LeaveAndSave : DeckEditOutcome.Leave;
                return back ? DeckEditOutcome.Leave : DeckEditOutcome.None;
        }
        int rows = Current.Count;
        if (input.IsPressed(PspAction.Square)) { Tab = (DeckEditTab)(((int)Tab + 1) % 4); Cursor = 0; Scroll = 0; return DeckEditOutcome.None; }
        if (rows > 0 && input.IsPressed(PspAction.Down)) MoveCursor(Cursor + 1 >= rows ? 0 : Cursor + 1);
        else if (rows > 0 && input.IsPressed(PspAction.Up)) MoveCursor(Cursor == 0 ? rows - 1 : Cursor - 1);
        if (input.IsPressed(PspAction.Triangle)) { Menus.OpenMain(); return DeckEditOutcome.None; }
        if (confirm && Focused is not null) { Prompt = DeckEditPrompt.Move; return DeckEditOutcome.None; }
        if (back) { Prompt = DeckEditPrompt.ExitQuestion; PromptFocus = 0; }
        return DeckEditOutcome.None;
    }

    private void MoveCursor(int next)
    {
        Cursor = next;
        if (Cursor < Scroll) Scroll = Cursor;
        else if (Cursor >= Scroll + visible) Scroll = Cursor - visible + 1;
    }

    /// <summary>Moves the focused card through DeckDraft, which keeps the PC's own add/remove rules.</summary>
    private void Move()
    {
        if (Focused is not { } entry) return;
        uint id = entry.Card.GameCardId;
        undo = Snapshot();
        var previous = draft.Section;
        try
        {
            if (Tab == DeckEditTab.Trunk)
            {
                draft.Section = IsExtra(entry.Card) ? DeckSection.Extra : DeckSection.Main;
                if (entry.InTrunk > 0) draft.Add(id);
            }
            else
            {
                draft.Section = SectionOf(Tab);
                draft.Remove(id);
            }
        }
        finally { draft.Section = previous; }
        Clamp();
    }

    private void Clamp()
    {
        int rows = Current.Count;
        if (rows == 0) { Cursor = 0; Scroll = 0; }
        else if (Cursor >= rows) MoveCursor(rows - 1);
        if (Scroll > Math.Max(0, rows - visible)) Scroll = Math.Max(0, rows - visible);
    }

    /// <summary>The list's order or filter changed: the cursor returns to the top (inferred).</summary>
    public void ListChanged() { Cursor = 0; Scroll = 0; }

    /// <summary>Attach/Remove Label on the focused card; labels live in this session only (their save format is unmeasured).</summary>
    public void ToggleLabel()
    {
        if (Focused is not { } entry) return;
        if (!Query.Labeled.Add(entry.Card.GameCardId)) Query.Labeled.Remove(entry.Card.GameCardId);
        Clamp();
    }

    /// <summary>Undo Previous Move: restores the sections as they were before the last move, reset or empty.</summary>
    public void Undo()
    {
        if (undo is null) return;
        Restore(undo); undo = null; Clamp();
    }

    /// <summary>Reset Deck: the deck as it was when Deck Edit opened (inferred: the last saved state is the same on entry).</summary>
    public void ResetDeck() { undo = Snapshot(); Restore(entered); Clamp(); }

    /// <summary>Empty Deck: every card back to the Trunk (observed Deck 0, Trunk +57 with an empty Side).</summary>
    public void EmptyDeck() { undo = Snapshot(); foreach (var section in Sections) draft.ReplaceSection(section, []); Clamp(); }

    /// <summary>Trunk card types matching Narrow Search selections, for the Run notice.</summary>
    public int CountMatching(IReadOnlyDictionary<DeckEditNarrowPanel, HashSet<int>> conditions) => Trunk().Count(e => Query.Matches(e.Card, conditions));
}
