using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.Tests;

public sealed class DuelSceneTests
{
    private static CardView Public(int id, string name) => new(id, (uint)id, name, name + " text", 1000, 1000, null, false);
    // The projection already blanks hidden cards; the scene must not rely on that.
    private static CardView Hidden(BattlePosition? position = null) => new(null, null, "Secret Dragon", "Secret text", 3000, 2500, position, true);

    private static PlayerView Player(PlayerId id, IReadOnlyList<CardView> hand, CardView? monster = null, CardView? set = null) =>
        new(id, 8000, 35, 0, hand, [monster, null, null, null, null], [null, null, set, null, null], [null, null], [], []);

    private static DuelView View(IReadOnlyList<ChoiceView>? choices = null) => new(4, PlayerId.One, PlayerId.One, 3, DuelPhase.Main1,
        Player(PlayerId.One, [Public(1, "Feral Imp"), Public(2, "Skull Servant")], monster: Public(3, "Battle Ox")),
        Player(PlayerId.Two, [Hidden(), Hidden()], monster: Hidden(BattlePosition.FaceDownDefense), set: Hidden()),
        "Choose an action", choices ?? [new ChoiceView(0, "Summon Feral Imp", 1), new ChoiceView(1, "End turn")], null);

    private static IEnumerable<string> Texts(DuelDrawList list) => list.Draws.Select(d => d switch
    {
        DuelText t => t.Text,
        DuelWrapped w => w.Text,
        _ => ""
    });

    [Fact]
    public void HiddenCardsNeverContributeTheirNameOrText()
    {
        var view = View();
        for (int inspect = -1; inspect < 8; inspect++)
        {
            var texts = Texts(DuelScene.Compose(new(view, 0, inspect, "", 0))).ToArray();
            Assert.DoesNotContain(texts, t => t.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void InspectCyclesThroughPublicCardsOnly()
    {
        var view = View();
        var cards = CardInspector.PublicCards(view);
        Assert.Equal([1, 2, 3], cards.Select(c => c.InstanceId!.Value));
        Assert.Equal(2, CardInspector.Target(view, 1, 4)!.InstanceId);
        Assert.Equal(1, CardInspector.Target(view, 0, -1)!.InstanceId);
        Assert.Null(CardInspector.Target(view, 1, -1));
    }

    [Fact]
    public void FocusFrameMarksOnlyTheInspectedSlot()
    {
        var view = View();
        var focus = DuelScene.Compose(new(view, 0, -1, "", 0)).Draws.OfType<DuelBorder>().Where(b => b.Part == DuelPart.Focus).ToArray();
        var expected = DuelSceneLayout.RowSlots(view).Single(s => s.Card?.InstanceId == 1).Rect;
        Assert.Equal(expected, Assert.Single(focus).Rect);
        Assert.DoesNotContain(DuelScene.Compose(new(view, 1, -1, "", 0)).Draws, d => d.Part == DuelPart.Focus);
    }

    [Fact]
    public void PartsDrawInSceneOrder()
    {
        var draws = DuelScene.Compose(new(View(), 0, -1, "", 0)).Draws;
        Assert.Equal(DuelPart.Board, draws[0].Part);
        Assert.Equal([DuelPart.Board, DuelPart.Hud, DuelPart.PhaseBar, DuelPart.Cards, DuelPart.Focus, DuelPart.Inspector, DuelPart.Prompt],
            draws.Select(d => d.Part).Distinct());
        Assert.Equal(DuelPart.Prompt, draws[^1].Part);
    }

    [Fact]
    public void OutcomeBannerIsTheLastLayer()
    {
        var view = View([]) with { Outcome = new DuelOutcome(PlayerId.One, PlayerId.Two, DuelEndReason.LifePointsDepleted) };
        var draws = DuelScene.Compose(new(view, 0, -1, "", 0)).Draws;
        var banner = draws.TakeLast(4).ToArray();
        Assert.All(banner, draw => Assert.Equal(DuelPart.Effects, draw.Part));
        Assert.Equal(SceneLayer.Modal, Assert.IsType<DuelFill>(banner[0]).Layer);
        var text = banner.OfType<DuelText>().ToArray();
        Assert.Equal(["You win", "Opponent reached 0 LP"], text.Select(draw => draw.Text));
        Assert.All(text, draw => Assert.Equal(SceneLayer.ModalText, draw.Layer));
    }

    [Fact]
    public void OriginalFieldStartsWithBackdropAndBoardInGeOrder()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var draws = DuelScene.Compose(new(View(), 0, -1, "", 0, new("tf6")), field).Draws.OfType<DuelFieldDraw>().ToArray();
        int backdrop = field.Piece("backdrop.tf6").Length;
        Assert.All(draws.Take(backdrop), d => Assert.Equal("backdrop.tf6", d.Piece));
        Assert.Equal(Enumerable.Range(0, backdrop), draws.Take(backdrop).Select(d => d.Index));
        Assert.Equal("board", draws[backdrop].Piece);
    }

    [Theory]
    [InlineData(DuelPhase.Draw, -70f)]
    [InlineData(DuelPhase.Main1, 0f)]
    [InlineData(DuelPhase.End, 105f)]
    public void ActivePhaseMovesByMeasuredCellPitchWithItsOwnLabel(DuelPhase phase, float offset)
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var active = DuelScene.Compose(new(View() with { Phase = phase }, 0, -1, "", 0), field).Draws.OfType<DuelFieldDraw>()
            .Where(d => d.Piece == "phase.active").ToArray();
        Assert.Equal(4, active.Length);
        Assert.All(active, d => Assert.Equal(offset, d.Placement.Offset.X));
        Assert.Equal(field.Piece("phase.cells")[PhaseBar.LabelIndex(phase)].Texture, active[1].Placement.Texture);
    }

    [Fact]
    public void TurnGlowFollowsTheTurnPlayersPortrait()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        float Glow(PlayerId active) => DuelScene.Compose(new(View() with { ActivePlayer = active }, 0, -1, "", 0), field).Draws
            .OfType<DuelFieldDraw>().Single(d => d.Piece == "portrait.player.glow").Placement.Offset.X;
        Assert.Equal(0f, Glow(PlayerId.One));
        Assert.Equal(DuelHud.OpponentPortraitOffset, Glow(PlayerId.Two));
    }

    [Fact]
    public void HiddenAndFaceDownCardsNeverRequestTheirFace()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        // A leaked identity on a hidden card, and the viewer's own set card, must both draw the back.
        var leaked = new CardView(null, 4242, "Secret Dragon", "", null, null, null, true);
        var ownSet = new CardView(9, 4343, "Own Set", "", null, null, null, false, FaceDown: true);
        var view = View() with
        {
            Self = Player(PlayerId.One, [Public(1, "Feral Imp")], set: ownSet),
            Opponent = Player(PlayerId.Two, [leaked], monster: leaked with { Position = BattlePosition.FaceDownDefense })
        };
        var keys = DuelScene.Compose(new(view, 0, -1, "", 0), field).Draws.OfType<DuelFieldDraw>().Select(d => d.Placement.Texture).OfType<string>().ToArray();
        Assert.DoesNotContain(CardSpriteRenderer.FaceKey(4242), keys);
        Assert.DoesNotContain(CardSpriteRenderer.FaceKey(4343), keys);
        Assert.Contains(CardSpriteRenderer.FaceKey(1), keys);
    }

    [Fact]
    public void PileCounterLandsOnTheMeasuredPixels()
    {
        var field = DuelFieldProfile.LoadEmbedded();
        var list = new DuelDrawList();
        FieldCounters.Draw(list, DuelPart.Board, field, 36, DuelLayoutProfile.PileTop(true, DuelPileKind.Deck, 36), FieldCounters.White);
        var template = field.Piece("digit.small")[0].Vertices![0];
        var first = list.Draws.OfType<DuelFieldDraw>().First().Placement.Offset;
        Assert.Equal((463f, 218f), (first.X + template[0], first.Y + template[1]));
    }

    [Theory]
    [InlineData(4, new[] { -98f, -33f, 32f, 97f })]
    [InlineData(5, new[] { -130f, -65f, 0f, 65f, 130f })]
    [InlineData(6, new[] { -163f, -98f, -33f, 32f, 97f, 162f })]
    public void HandSpacingMatchesCapturedHands(int count, float[] xs) =>
        Assert.Equal(xs, Enumerable.Range(0, count).Select(i => DuelLayoutProfile.HandWorld(i, count, false).Translation.X));

    [Fact]
    public void ComposeIsDeterministic()
    {
        var input = new DuelSceneInput(View(), 0, 2, "Opponent: set a card", 0.25);
        Assert.Equal(DuelScene.Compose(input).Draws, DuelScene.Compose(input).Draws);
    }
}
