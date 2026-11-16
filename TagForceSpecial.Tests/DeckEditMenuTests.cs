using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.Tests;

/// <summary>Deck Edit Menu chain: measured profile, menu navigation and the list query. PC Cross = confirm, PC Circle = back.</summary>
public class DeckEditMenuTests
{
    /// <summary>The supported pool: eight Normal monsters (raw kind 0) and six Normal Spells (13, icon 0).</summary>
    private static DeckEditSession Session(out DeckDraft draft)
    {
        var catalog = new CardCatalog(new(TfspCardMetadata.CardInfoSha256, "authored-deck-edit-menu-fixture"),
            PlayablePreview.Cards.Select((c, i) => new CardDefinition(c.CardId, $"Card {c.CardId}", "Text.",
                new CardStatistics(c.Spell is null ? 1000 + i : null, c.Spell is null ? 800 : null, c.Spell is null ? (uint)(i % 8 + 1) : 0u),
                new CardNumericCodes(c.Spell is null ? 5u : 8u, c.Spell is null ? 12u : 25u, c.Spell is null ? 0u : 13u, 0, 0, 0, 0), new CardSourceRow(400 + i, 0, 0, 0))));
        draft = new DeckDraft(catalog, InitialOpponentDecks.Create(0));
        return new DeckEditSession(draft, PlayablePreview.Cards.Select(c => catalog[c.CardId]), 7);
    }

    private static void Tap(DeckEditSession session, params PspAction[] actions)
    {
        foreach (var action in actions) session.Handle(new(action, action));
    }

    private static void OpenRow(DeckEditSession session, int row)
    {
        for (int i = 0; i < row; i++) Tap(session, PspAction.Down);
        Tap(session, PspAction.Cross);
    }

    [Fact]
    public void ProfileHoldsEveryPopupAndStretchesBandsWithFixedCorners()
    {
        var profile = DeckEditMenuProfile.LoadEmbedded();
        Assert.All(DeckEditMenus.PopupIds, id => Assert.Equal(id, profile[id].Id));
        Assert.Equal(9, profile["main"].Rows!.Length);
        Assert.Equal(11, profile["sort"].Rows!.Length);
        Assert.Equal(11, profile["card-frame"].Cells!.Length);
        Assert.Equal(26, profile["level"].Cells!.Length);
        Assert.Equal(66, profile.Pulse.Cycle.Length);
        var band = profile.Band(10, 20, 110, 39, profile.FocusColor, 200).ToArray();
        Assert.Equal(9, band.Length);
        Assert.Equal(10, band.Min(p => p.Points.Min(q => q[0])));
        Assert.Equal(110, band.Max(p => p.Points.Max(q => q[0])));
        Assert.Equal(39, band.Max(p => p.Points.Max(q => q[1])));
        Assert.All(band, p => Assert.Equal(200, p.Color[3]));
    }

    [Fact]
    public void TriangleOpensTheMenuAndBackClosesOneLevelAtATime()
    {
        var session = Session(out _);
        Tap(session, PspAction.Triangle);
        Assert.Equal("main", session.Menus.Top!.Id);
        OpenRow(session, 3);
        Assert.Equal(["main", "search"], session.Menus.Stack.Select(l => l.Id));
        Tap(session, PspAction.Circle);
        Assert.Equal("main", session.Menus.Top!.Id);
        Tap(session, PspAction.Circle);
        Assert.False(session.Menus.Open);
        Assert.Equal(DeckEditPrompt.None, session.Prompt);
    }

    [Fact]
    public void GreyedRowsTakeTheCursorAndUndoRestoresTheLastMove()
    {
        var session = Session(out var draft);
        var before = draft.Cards(DeckSection.Main).ToArray();
        Tap(session, PspAction.Triangle);
        OpenRow(session, 8); // Undo Previous Move, greyed: nothing happens
        Assert.Equal(8, session.Menus.Top!.Focus);
        Tap(session, PspAction.Circle);
        Tap(session, PspAction.Cross, PspAction.Cross); // move the first Trunk card into the Deck
        Assert.NotEqual(before, draft.Cards(DeckSection.Main));
        Assert.True(session.CanUndo);
        Tap(session, PspAction.Triangle);
        OpenRow(session, 8);
        Assert.Equal(before, draft.Cards(DeckSection.Main));
        Assert.False(session.Menus.Open);
    }

    [Fact]
    public void SortPreviewsLiveAndBackRestoresTheSavedOrder()
    {
        var session = Session(out _);
        var unsorted = session.Current.Select(e => e.Card.GameCardId).ToArray();
        Tap(session, PspAction.Triangle);
        OpenRow(session, 3);
        Tap(session, PspAction.Cross); // Sort, starting on Name
        Tap(session, PspAction.Down, PspAction.Down, PspAction.Down, PspAction.Down); // ATK
        Assert.Equal(DeckEditSortKey.Attack, session.Query.SortKey);
        Assert.Equal(session.Current.Select(e => e.Card.Statistics.Attack ?? -2).Order(), session.Current.Select(e => e.Card.Statistics.Attack ?? -2));
        Tap(session, PspAction.Circle);
        Assert.Null(session.Query.SortKey);
        Assert.Equal(unsorted, session.Current.Select(e => e.Card.GameCardId));
    }

    [Fact]
    public void OrderInversePickClosesAllMenus()
    {
        var session = Session(out _);
        Tap(session, PspAction.Triangle);
        OpenRow(session, 3);
        Tap(session, PspAction.Cross, PspAction.Down, PspAction.Down); // Sort > Level/Rank
        Tap(session, PspAction.Right, PspAction.Down, PspAction.Cross); // Order Inverse
        Assert.False(session.Menus.Open);
        Assert.Equal(DeckEditSortKey.LevelRank, session.Query.SortKey);
        Assert.True(session.Query.Inverse);
    }

    [Fact]
    public void CardFrameRunCountsThenNarrowsAndResetRestores()
    {
        var session = Session(out _);
        int all = session.Current.Count, spells = session.Current.Count(e => e.Card.Codes!.RawKind == 13);
        Tap(session, PspAction.Triangle);
        OpenRow(session, 3);
        OpenRow(session, 1); // Narrow Search
        Tap(session, PspAction.Cross); // Card Frame grid (3 x 4)
        Tap(session, PspAction.Down, PspAction.Down, PspAction.Right, PspAction.Cross); // Spell
        Assert.True(session.Menus.IsSelected("card-frame", 9));
        Tap(session, PspAction.Down, PspAction.Cross); // Run
        Assert.Equal(spells, session.Menus.RunCount);
        Assert.Empty(session.Menus.Stack); // the notice shows over the already narrowed list
        Assert.Equal(spells, session.Current.Count);
        Tap(session, PspAction.Cross); // OK
        Assert.False(session.Menus.Open);
        Assert.Equal(spells, session.Current.Count);
        Assert.All(session.Current, e => Assert.Equal(13u, e.Card.Codes!.RawKind));
        Tap(session, PspAction.Triangle);
        OpenRow(session, 7); // Reset Narrow Search Results
        Assert.Equal(all, session.Current.Count);
    }

    [Fact]
    public void LabelsFilterTheTrunkAndLabelingKeepsTheMenuOpen()
    {
        var session = Session(out _);
        uint first = session.Focused!.Card.GameCardId;
        Tap(session, PspAction.Triangle);
        OpenRow(session, 1); // Attach/Remove Label
        Assert.True(session.Menus.Open);
        Assert.Contains(first, session.Query.Labeled);
        Tap(session, PspAction.Down, PspAction.Down, PspAction.Cross); // Search Menu (row 3) from row 1
        OpenRow(session, 4); // Label Display Settings
        Tap(session, PspAction.Down, PspAction.Down, PspAction.Cross); // Only Show Labeled Cards
        Assert.False(session.Menus.Open);
        Assert.Equal([first], session.Current.Select(e => e.Card.GameCardId));
    }

    [Fact]
    public void EmptyDeckAppliesAtOnceAndUnrecreatedRowsShowTheNotice()
    {
        var session = Session(out var draft);
        Tap(session, PspAction.Triangle);
        OpenRow(session, 4);
        OpenRow(session, 4); // Empty Deck
        Assert.Empty(draft.Cards(DeckSection.Main));
        Assert.False(session.Menus.Open);
        Tap(session, PspAction.Triangle, PspAction.Cross); // Card Details
        Assert.Equal(("main", 0), session.Menus.Notice);
        Tap(session, PspAction.Circle);
        Assert.Null(session.Menus.Notice);
        Assert.Equal("main", session.Menus.Top!.Id);
    }
}
