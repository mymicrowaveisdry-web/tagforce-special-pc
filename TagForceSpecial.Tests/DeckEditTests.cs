using TagForceSpecial.Cards;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Decks;

namespace TagForceSpecial.Tests;

/// <summary>Deck Edit core screen (Phase 6.2a): measured profile lookups and the observed list behaviour. PC Cross = confirm, PC Circle = back.</summary>
public class DeckEditTests
{
    /// <summary>The supported pool with its catalog kinds: eight Normal monsters (raw kind 0) and six Spells (13).</summary>
    private static CardCatalog Catalog() => new(new(TfspCardMetadata.CardInfoSha256, "authored-deck-edit-fixture"),
        PlayablePreview.Cards.Select((c, i) => new CardDefinition(c.CardId, $"Card {c.CardId}", "Text.", new CardStatistics(c.Spell is null ? 1000 : null, c.Spell is null ? 800 : null, c.Spell is null ? 4u : 0u),
            new CardNumericCodes(c.Spell is null ? 5u : 8u, c.Spell is null ? 12u : 25u, c.Spell is null ? 0u : 13u, 0, 0, 0, 0), new CardSourceRow(400 + i, 0, 0, 0))));

    private static DeckEditSession Session(out DeckDraft draft)
    {
        var catalog = Catalog();
        draft = new DeckDraft(catalog, InitialOpponentDecks.Create(0));
        return new DeckEditSession(draft, PlayablePreview.Cards.Select(c => catalog[c.CardId]), 7);
    }

    private static DeckEditOutcome Tap(DeckEditSession session, PspAction action) => session.Handle(new(action, action));

    [Fact]
    public void ProfileSeparatesObservedFromInferredLookups()
    {
        var profile = DeckEditProfile.LoadEmbedded();
        Assert.Equal(7, profile.VisibleRows);
        Assert.Equal(30f, profile.RowPitch);
        // Spells were seen on recorded rows; Normal monsters were not, so their pieces come from the labelled inferred section.
        Assert.False(profile.Uv("cardFrameByKind", 13u)!.Value.Inferred);
        Assert.True(profile.Uv("cardFrameByKind", 0u)!.Value.Inferred);
        Assert.False(profile.FramePalette(13)!.Value.Inferred);
        Assert.Equal(4, profile.FramePalette(13)!.Value.Value);
        Assert.True(profile.FramePalette(0)!.Value.Inferred);
        Assert.Null(profile.FramePalette(99));
        // Measured on every recorded row: cell = internal index - 95 (Yubel - The Ultimate Nightmare is index 3394, sheet 4, cell 163).
        var mini = profile.GetMiniImage(3394)!.Value;
        Assert.Equal("deck.all.cis-all.f04", mini.AssetKey);
        Assert.Equal(23 * 18 / 512f, mini.Uv[0], 4);
        Assert.Equal(5 * 18 / 512f, mini.Uv[1], 4);
        Assert.Null(profile.GetMiniImage(94));
    }

    [Fact]
    public void SquareCyclesTrunkDeckExSideAndResetsTheCursor()
    {
        var session = Session(out _);
        Assert.Equal(DeckEditTab.Trunk, session.Tab);
        Tap(session, PspAction.Square); Assert.Equal(DeckEditTab.Deck, session.Tab);
        Tap(session, PspAction.Down); Assert.Equal(1, session.Cursor);
        Tap(session, PspAction.Square); Assert.Equal(DeckEditTab.Extra, session.Tab); Assert.Equal(0, session.Cursor);
        Tap(session, PspAction.Square); Assert.Equal(DeckEditTab.Side, session.Tab);
        Tap(session, PspAction.Square); Assert.Equal(DeckEditTab.Trunk, session.Tab);
    }

    [Fact]
    public void CursorWrapsBothWaysAndScrollKeepsItVisible()
    {
        var session = Session(out var draft);
        Tap(session, PspAction.Square);
        int rows = draft.Cards(DeckSection.Main).Count;
        Tap(session, PspAction.Up);
        Assert.Equal(rows - 1, session.Cursor);
        Assert.Equal(rows - 7, session.Scroll);
        Tap(session, PspAction.Down);
        Assert.Equal((0, 0), (session.Cursor, session.Scroll));
        for (int i = 0; i < 8; i++) Tap(session, PspAction.Down);
        Assert.Equal((8, 2), (session.Cursor, session.Scroll));
        // Left and Right do nothing in the list layout.
        Tap(session, PspAction.Left); Tap(session, PspAction.Right);
        Assert.Equal(8, session.Cursor);
    }

    [Fact]
    public void MovingACardGoesThroughTheDraftAndCancelLeavesItUnchanged()
    {
        var session = Session(out var draft);
        int deck = session.Count(DeckEditTab.Deck), trunk = session.Count(DeckEditTab.Trunk);
        Assert.True(trunk > 0);
        Tap(session, PspAction.Cross);
        Assert.Equal(DeckEditPrompt.Move, session.Prompt);
        Assert.Equal(DeckEditTab.Deck, session.Destination);
        Tap(session, PspAction.Circle);
        Assert.Equal(DeckEditPrompt.None, session.Prompt);
        Assert.Equal(deck, session.Count(DeckEditTab.Deck));
        Tap(session, PspAction.Cross); Tap(session, PspAction.Cross);
        Assert.Equal((deck + 1, trunk - 1), (session.Count(DeckEditTab.Deck), session.Count(DeckEditTab.Trunk)));
        Assert.True(session.Changed);
        // From the Deck tab a card goes back to the Trunk.
        Tap(session, PspAction.Square);
        Tap(session, PspAction.Cross);
        Assert.Equal(DeckEditTab.Trunk, session.Destination);
        Tap(session, PspAction.Cross);
        Assert.Equal((deck, trunk), (session.Count(DeckEditTab.Deck), session.Count(DeckEditTab.Trunk)));
        Assert.Equal(deck, draft.Cards(DeckSection.Main).Count);
    }

    [Fact]
    public void AnEmptySideDoesNothingOnConfirm()
    {
        var session = Session(out _);
        for (int i = 0; i < 3; i++) Tap(session, PspAction.Square);
        Assert.Equal(DeckEditTab.Side, session.Tab);
        Assert.Empty(session.Current);
        Tap(session, PspAction.Cross);
        Assert.Equal(DeckEditPrompt.None, session.Prompt);
    }

    [Fact]
    public void ExitAsksWithOkFocusedThenAsksToSaveWithYesFocused()
    {
        var session = Session(out _);
        Tap(session, PspAction.Circle);
        Assert.Equal((DeckEditPrompt.ExitQuestion, 0), (session.Prompt, session.PromptFocus));
        Tap(session, PspAction.Right);
        Assert.Equal(DeckEditOutcome.None, Tap(session, PspAction.Cross));
        Assert.Equal((DeckEditPrompt.None, 0), (session.Prompt, session.PromptFocus));
        Tap(session, PspAction.Circle);
        Tap(session, PspAction.Circle);
        Assert.Equal(DeckEditPrompt.None, session.Prompt);
        Tap(session, PspAction.Circle);
        Tap(session, PspAction.Cross);
        Assert.Equal((DeckEditPrompt.SaveQuestion, 0), (session.Prompt, session.PromptFocus));
        Assert.Equal(DeckEditOutcome.LeaveAndSave, Tap(session, PspAction.Cross));
        var second = Session(out _);
        Tap(second, PspAction.Circle); Tap(second, PspAction.Cross); Tap(second, PspAction.Right);
        Assert.Equal(DeckEditOutcome.Leave, Tap(second, PspAction.Cross));
        var third = Session(out _);
        Tap(third, PspAction.Circle); Tap(third, PspAction.Cross);
        Assert.Equal(DeckEditOutcome.Leave, Tap(third, PspAction.Circle));
    }

    private sealed class Host(bool catalog) : ITitleFlowHost
    {
        public int Saves { get; private set; }
        public bool ProfilePresent => false;
        public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
        { name = ""; level = DuelistLevelChoice.FirstTimer; options = PlayerOptions.Defaults(); return false; }
        public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) { }
        public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) { }
        public DeckEditSession? OpenDeckEdit() => catalog ? Session(out _) : null;
        public void SaveDeckEdit(DeckEditSession session) => Saves++;
    }

    private static void Press(TitleFlow flow, PspAction action) { flow.Handle(new(action, action)); flow.Handle(new(PspAction.None, PspAction.None)); }

    private static TitleFlow OnDeckEditRow(Host host)
    {
        var flow = new TitleFlow(host, "Tester", instantTransitions: true);
        flow.BeginMainMenu();
        for (int i = 0; i < 4; i++) Press(flow, PspAction.Down);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        return flow;
    }

    [Fact]
    public void DeckEditRowOpensTheScreenAndReturnsToTheSubmenuSavingOnlyOnYes()
    {
        var host = new Host(catalog: true);
        var flow = OnDeckEditRow(host);
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DeckEdit, flow.Step);
        Assert.NotNull(flow.DeckEdit);
        Press(flow, PspAction.Circle); Press(flow, PspAction.Cross); Press(flow, PspAction.Right); Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.Equal(0, flow.DeckMenu!.Focus);
        Assert.True(flow.DeckMenu.Ready);
        Assert.Equal(0, host.Saves);
        Press(flow, PspAction.Cross);
        Press(flow, PspAction.Circle); Press(flow, PspAction.Cross); Press(flow, PspAction.Cross);
        Assert.Equal(1, host.Saves);
    }

    [Fact]
    public void WithoutACardCatalogTheRowKeepsItsPcNotice()
    {
        var flow = OnDeckEditRow(new Host(catalog: false));
        Press(flow, PspAction.Cross);
        Assert.Equal(TitleFlowStep.DeckMenu, flow.Step);
        Assert.True(flow.DeckMenu!.NoticeOpen);
        Assert.Equal(DeckMenuDestinations.DeckEditCore, DeckMenuDestinations.All[0].Status);
    }
}
