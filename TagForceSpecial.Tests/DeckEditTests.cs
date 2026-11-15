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
        // Spells and Normal monsters were seen on recorded rows and previews (Card Frame captures); unobserved kinds stay labelled inferred.
        Assert.False(profile.Uv("cardFrameByKind", 13u)!.Value.Inferred);
        Assert.False(profile.Uv("cardFrameByKind", 0u)!.Value.Inferred);
        Assert.Equal(new DeckEditLookup<string>("deck.all.frame.p04", false), profile.FrameKey(13));
        Assert.Equal(new DeckEditLookup<string>("deck.all.frame.p00", false), profile.FrameKey(0));
        Assert.Equal(new DeckEditLookup<string>("deck.all.frame.p05", false), profile.FrameKey(14));
        Assert.Equal(new DeckEditLookup<string>("deck.all.frame-xyz", false), profile.FrameKey(23));
        Assert.True(DeckEditProfile.IsPendulumFrame(profile.FrameKey(26)!.Value.Value));
        Assert.True(profile.FrameKey(2)!.Value.Inferred);
        Assert.Null(profile.FrameKey(99));
        // The preview card is composed offscreen and shown unscaled at the composite box; its pieces were recorded per card frame.
        var card = profile.Card;
        Assert.Equal(new[] { 5f, 4f, 113f, 158f }, card.Composite);
        Assert.Equal(7 / 128f, card.DigitCell, 4);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" }, card.AttributeUv.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { 32f, 96f }, card.Stars["level"].Spans["8"]);
        Assert.Equal(new[] { 20f, 98f }, card.Stars["level"].Spans["11"]);
        // '?' and Pendulum Scales: the scale digits are the stat digit sprites; 10 has its own sprite.
        Assert.Equal(10 * card.DigitCell, card.Question[0], 3);
        Assert.Equal(7 * card.DigitCell, card.PendulumScaleUv["7"][0], 3);
        Assert.Equal(new[] { 8f, 93f }, card.PendulumScaleX);
        // Card text: system font ltn0 at 0.2 x 0.18, wrapped inside the recorded bounds, 3 lines for monsters and 6 for Spells/Traps.
        Assert.Equal(0.2f, card.Text.ScaleX, 3);
        Assert.InRange(card.Text.Width, card.Text.WidthBounds[0], card.Text.WidthBounds[1]);
        Assert.Equal((3, 6), (card.Text.MonsterLines, card.Text.SpellTrapLines));
        Assert.Equal(new[] { 14f, 92f }, card.Stars["rank"].Spans["11"]);
        Assert.Equal("deck.all.spelltrap", card.SpellLabel.AssetKey);
        Assert.Contains("deck.all.spelltrap", profile.AssetKeys());
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
