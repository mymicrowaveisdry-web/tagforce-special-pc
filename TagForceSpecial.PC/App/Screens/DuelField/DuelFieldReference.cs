using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Developer verification only: renders the measured field layer for the states of the original GE captures, so each PNG can be
/// compared pixel by pixel with the matching native screenshot. Never part of the player UI.</summary>
public static class DuelFieldReference
{
    /// <summary>Capture name, backdrop piece, phase and turn of each reference state; the player is the turn player in every capture.</summary>
    public static IReadOnlyList<(string Name, string Backdrop, DuelPhase Phase, int Turn)> States { get; } =
    [
        ("aki-board", "tf6", DuelPhase.Main1, 2),
        ("joey-board", "default", DuelPhase.Battle, 3),
        ("yuya-chain", "tf4", DuelPhase.Main1, 1)
    ];

    public static void Capture(DuelFieldVisuals field, string root, Func<uint, DuelCardFacts?>? facts)
    {
        Directory.CreateDirectory(root);
        foreach (var (name, backdrop, phase, turn) in States)
        {
            var view = name == "aki-board" ? AkiBoard() : EmptyView(phase, turn);
            var list = DuelScene.Compose(new(view, 0, -1, "", 0, new(backdrop), facts), field.Profile);
            field.Render(list.FieldDraws);
            using var output = new FileStream(Path.Combine(root, name + ".field.png"), FileMode.CreateNew, FileAccess.Write);
            field.Layer.SaveAsPng(output, 480, 272);
        }
    }

    /// <summary>The aki-board capture: Gusto Gulldo and a set card on the player's centre slots, four cards in hand with Caam raised by the
    /// cursor, Lonefire Blossom and a set card on Aki's centre slots, three cards in her hand; Decks 36 and 37, Extra Decks 15.</summary>
    public static DuelView AkiBoard()
    {
        static CardView Face(int instance, uint id, BattlePosition? position = null) => new(instance, id, "", "", null, null, position, false);
        static CardView Hidden(BattlePosition? position = null) => new(null, null, "Hidden card", "", null, null, position, true);
        var self = new PlayerView(PlayerId.One, 8000, 36, 15,
            [Face(1, 9167), Face(2, 9167), Face(3, 5290), Face(4, 9168)],
            [null, null, Face(5, 9164, BattlePosition.FaceUpAttack), null, null],
            [null, null, new CardView(6, 9167, "", "", null, null, null, false, FaceDown: true), null, null], new CardView?[2], [], []);
        var opponent = new PlayerView(PlayerId.Two, 8000, 37, 15, [Hidden(), Hidden(), Hidden()],
            [null, null, Face(7, 7568, BattlePosition.FaceUpAttack), null, null],
            [null, null, Hidden(), null, null], new CardView?[2], [], []);
        return new(0, PlayerId.One, PlayerId.One, 2, DuelPhase.Main1, self, opponent, "", [new ChoiceView(0, "", 4)], null);
    }

    public static DuelView EmptyView(DuelPhase phase, int turn) => new(0, PlayerId.One, PlayerId.One, turn, phase,
        Empty(PlayerId.One), Empty(PlayerId.Two), "", [], null);

    private static PlayerView Empty(PlayerId id) => new(id, 8000, 0, 0, [], new CardView?[5], new CardView?[5], new CardView?[2], [], []);
}
