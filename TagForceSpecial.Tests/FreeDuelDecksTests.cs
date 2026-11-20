using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.FreeDuel;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelDecksTests
{
    private static readonly FreeDuelDecksProfile Decks = FreeDuelDecksProfile.LoadEmbedded();
    private static readonly FreeDuelPickerProfile Roster = FreeDuelPickerProfile.LoadEmbedded();
    private const int Aki = 75, Fujiwara = 70; // No.076 (partner) and No.071 (not a partner)

    private static void Tap(FreeDuelDeckPickerSession s, InputAction a) => s.Handle(new(a, a));
    private static void Tap(FreeDuelSession s, InputAction a) => s.Handle(new(a, a));

    [Fact]
    public void PartnersListThePcDeckThenPartnersDeckThenTheirFourOwnDecks()
    {
        var aki = FreeDuelDeckPickerSession.OpponentEntries(Decks, Aki);
        Assert.Equal(6, aki.Count);
        Assert.True(aki[0].Enabled);
        Assert.Equal(FreeDuelVersusRenderer.PcDeckLabel, aki[0].Literal);
        Assert.Equal(Decks.PartnerDeck, aki[1].Source);
        Assert.Equal(1351602, aki[2].Source!.Offset); // "Pruning of the Black Rose", EBOOT recipe 47 = 3 + 4 x (13 - 2)
        Assert.All(aki.Skip(1), e => Assert.False(e.Enabled));
        var fujiwara = FreeDuelDeckPickerSession.OpponentEntries(Decks, Fujiwara);
        Assert.Equal(5, fujiwara.Count);
        Assert.DoesNotContain(fujiwara, e => e.Source == Decks.PartnerDeck);
        Assert.Equal(Roster.Characters.Length, Decks.CharacterDecks.Length);
    }

    [Fact]
    public void OriginalRowsShowANoticeAndThePcRowIsPicked()
    {
        var s = new FreeDuelDeckPickerSession(false, FreeDuelDeckPickerSession.OpponentEntries(Decks, Aki), Decks.VisibleRows);
        Tap(s, InputAction.Down);
        Assert.Equal(FreeDuelDeckPickerSession.Outcome.None, s.Handle(new(InputAction.Confirm, InputAction.Confirm)));
        Assert.True(s.NoticeOpen);
        Tap(s, InputAction.Back);
        Assert.False(s.NoticeOpen);
        Tap(s, InputAction.Up); Tap(s, InputAction.Up);
        Assert.Equal(5, s.Focus); // wraps to the last of six rows
        Tap(s, InputAction.Down);
        Assert.Equal(FreeDuelDeckPickerSession.Outcome.Picked, s.Handle(new(InputAction.Confirm, InputAction.Confirm)));
    }

    [Fact]
    public void PlayerListHasThePcDeckFirstThenSavedRecipesBySlot()
    {
        var saved = new[] { (2, "B", new DateTime(2019, 5, 30, 19, 55, 0)), (0, "A", new DateTime(2019, 5, 30, 20, 12, 0)) };
        var list = FreeDuelDeckPickerSession.PlayerEntries("PC DECK", saved);
        Assert.Equal(["PC DECK", "A", "B"], list.Select(e => e.Literal));
        Assert.Equal([true, false, false], list.Select(e => e.Enabled));
        Assert.Equal([null, 0, 2], list.Select(e => e.Slot));
    }

    [Fact]
    public void VersusOpensBothPickersAndClosesThemWithoutChangingTheChoice()
    {
        var s = new FreeDuelSession(new FreeDuelSettingsSession(44), "P", "PC DECK", Roster, Aki, new Random(1), Decks, []);
        for (int i = 0; i < 5; i++) Tap(s, InputAction.Confirm);
        Tap(s, InputAction.Down); Tap(s, InputAction.Confirm);
        Assert.False(s.DeckPicker!.Player);
        Tap(s, InputAction.Confirm); // the PC deck
        Assert.Null(s.DeckPicker);
        Tap(s, InputAction.Down); Tap(s, InputAction.Confirm);
        Assert.True(s.DeckPicker!.Player);
        Tap(s, InputAction.Back);
        Assert.Null(s.DeckPicker);
        Assert.Equal((Aki, FreeDuelVersusFocus.PlayerDeck), (s.Opponent, s.Versus!.Focus));
    }
}
