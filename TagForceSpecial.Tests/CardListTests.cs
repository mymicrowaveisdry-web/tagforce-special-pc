using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.Tests;

/// <summary>The original card list (Order 3.2 captures order32-gallery): column and scroll movement, wrap, Close row, closing, and the
/// visibility of each pile.</summary>
public sealed class CardListTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 59.94);
    private static CardView Card(int id) => new(id, (uint)(1000 + id), "Card " + id, "", 1000, 1000, null, false);

    private static CardListModal Walk(int count, params (int Dx, int Dy)[] steps) =>
        steps.Aggregate(new CardListModal(true, CardListPile.ExtraDeck), (m, s) => CardList.Move(m, s.Dx, s.Dy, count, repeated: false));

    [Fact]
    public void RightCrossesTheColumnsThenScrollsThenWraps()
    {
        Assert.Equal((4, 0), Sel(Walk(15, (1, 0), (1, 0), (1, 0), (1, 0))));
        Assert.Equal((4, 1), Sel(Walk(15, (1, 0), (1, 0), (1, 0), (1, 0), (1, 0))));
        Assert.Equal((4, 10), Sel(Walk(15, Enumerable.Repeat((1, 0), 14).ToArray())));
        Assert.Equal((0, 0), Sel(Walk(15, Enumerable.Repeat((1, 0), 15).ToArray())));
    }

    [Fact]
    public void LeftOnTheFirstCardGoesToTheLast() => Assert.Equal((4, 10), Sel(Walk(15, (-1, 0))));

    [Fact]
    public void DownMovesToCloseWhereLeftAndRightStay()
    {
        var close = Walk(15, (0, -1), (1, 0), (-1, 0));
        Assert.True(close.OnClose);
        Assert.Equal((0, 0), Sel(close));
        Assert.False(CardList.Move(close, 0, 1, 15, false).OnClose);
    }

    private static (int, int) Sel(CardListModal m) => (m.Column, m.Scroll);

    private static DuelView View(IReadOnlyList<CardView>? extra) => new(1, PlayerId.One, PlayerId.One, 2, DuelPhase.Main1,
        new(PlayerId.One, 8000, 30, extra?.Count ?? 0, [], new CardView?[5], new CardView?[5], new CardView?[2], [Card(50)], []) { ExtraDeck = extra },
        new(PlayerId.Two, 8000, 30, 15, [], new CardView?[5], new CardView?[5], new CardView?[2], [Card(60)], []),
        "", [new(0, "Phase: Battle", null, ActionKind.Phase, Phase: DuelPhase.Battle), new(1, "End turn", null, ActionKind.EndTurn)], null);

    private static DuelFieldInput Opened(DuelView view)
    {
        var input = new DuelFieldInput();
        input.Handle(view, default, Frame);
        input.Focus.FocusOn(DuelFieldRows.Build(view).SelectMany(r => r).First(o => o.Own && o.Kind == FieldObjectKind.Extra));
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame);
        return input;
    }

    [Fact]
    public void ShowCardListOpensTheOwnExtraDeckAndCrossClosesIt()
    {
        var view = View(Enumerable.Range(1, 7).Select(Card).ToArray());
        var input = Opened(view);
        Assert.Equal(new CardListModal(true, CardListPile.ExtraDeck), input.Modal);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame); // ○ on a card: nothing
        Assert.IsType<CardListModal>(input.Modal);
        input.Handle(view, new(InputAction.Back, InputAction.Back), Frame);
        Assert.Null(input.Modal);
        Assert.Equal(FieldObjectKind.Extra, input.Focus.Current.Kind);
    }

    [Fact]
    public void CircleOnCloseClosesTheList()
    {
        var view = View(Enumerable.Range(1, 7).Select(Card).ToArray());
        var input = Opened(view);
        input.Handle(view, new(InputAction.Down, InputAction.Down), Frame);
        input.Handle(view, new(InputAction.Confirm, InputAction.Confirm), Frame);
        Assert.Null(input.Modal);
    }

    [Fact]
    public void TheOpponentsExtraDeckIsNeverListed()
    {
        var view = View(null);
        Assert.Empty(CardList.Cards(view, own: false, CardListPile.ExtraDeck));
        Assert.Equal([60], CardList.Cards(view, own: false, CardListPile.Graveyard).Select(c => c.InstanceId!.Value));
    }

    [Fact]
    public void TheProjectionGivesTheExtraDeckOnlyToItsOwner()
    {
        const uint Main = 20001, Extra = 20002;
        var catalog = new TagForceSpecial.Cards.CardCatalog(new(null, "synthetic-card-list"),
            new[] { Main, Extra }.Select(id => new TagForceSpecial.Cards.CardDefinition(id, $"{id}", "", new(1000, 1000, 4))));
        var rules = new[] { Main, Extra }.ToDictionary(id => id, id => new DuelCardRules(CardCategory.Monster,
            AllowsNormalSummon: id == Main, AllowsBasicBattle: true, AllowsExtraDeck: id == Extra, AllowsSummonMaterial: id == Main));
        uint[] deck = [.. Enumerable.Repeat(Main, 40)];
        var e = new DuelEngine(catalog, rules, deck, deck, playerOneExtraDeck: [Extra, Extra]);
        var own = PlayerProjection.Create(e.State, PlayerId.One, PlayerId.One, catalog);
        var seen = PlayerProjection.Create(e.State, PlayerId.One, PlayerId.Two, catalog);
        Assert.Equal([Extra, Extra], own.ExtraDeck!.Select(c => c.CardId!.Value));
        Assert.Null(seen.ExtraDeck);
        Assert.Equal(2, seen.ExtraDeckCount);
        Assert.Null(own.Banished);
    }
}