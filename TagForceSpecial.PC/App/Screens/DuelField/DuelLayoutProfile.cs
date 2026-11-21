using Microsoft.Xna.Framework;
using TagForceSpecial.Core;

namespace TagForceSpecial.PC.App.Screens.DuelField;

public enum DuelPileKind { Deck, Extra, Graveyard }

/// <summary>Measured Duel Field placements (NPJH00142 v1.01, English 2.1, Normal Duel), in the board's world units. The player is +z.
/// Values come from the step 3 GE captures; each member says whether it was observed or derived.</summary>
public static class DuelLayoutProfile
{
    /// <summary>Observed: five slots per row, 105 apart, numbered left to right on screen for both players (board mesh quads).</summary>
    public const float SlotPitch = 105f, FirstSlotX = -210f;
    /// <summary>Observed: monster and Spell/Trap card centres (cards at 0 and the board mesh quads).</summary>
    public const float MonsterZ = 117.730469f, SpellTrapZ = 222.730469f;
    /// <summary>Observed pile centres: Deck and Extra Deck rows, Graveyard (pile captures); Field Spell and Pendulum from the pad meshes.</summary>
    public const float PileX = 315f, DeckZ = 275.851562f, GraveyardZ = 65.855469f, PendulumX = 300f, PendulumZ = 170.84f;
    /// <summary>Observed hands: player cards 65 apart at y 200, z 440 (4, 5 and 6 cards); the focused card rises to y 215, z 430.
    /// Opponent backs 70 apart at y 50, z −300 (3 and 4 cards).</summary>
    public const float HandPitch = 65f, HandY = 200f, HandZ = 440f, FocusedHandY = 215f, FocusedHandZ = 430f;
    public const float OpponentHandPitch = 70f, OpponentHandY = 50f, OpponentHandZ = -300f;

    /// <summary>Observed rotations: opponent cards turn 180° about the vertical; hand cards tilt toward the camera.</summary>
    public static readonly Matrix Opponent = new(-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1);
    public static readonly Matrix HandTilt = new(1, 0, 0, 0, 0, 0.696777f, 0.71727f, 0, 0, -0.71727f, 0.696777f, 0, 0, 0, 0, 1);
    /// <summary>Observed for an opponent's face-down defense card; the player's defense rotation is derived by removing the 180° turn.</summary>
    public static readonly Matrix OpponentDefense = new(0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 1);
    public static readonly Matrix PlayerDefense = new(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1);

    /// <summary>Observed pile height: 0.6115 for one card, +0.3815 per further card (1 to 37 cards, within 0.001).</summary>
    public static float PileHeight(int count) => 0.6115f + 0.3815f * (count - 1);
    /// <summary>The pile mesh spans ±0.704 × ±0.344 × ±1; its bone scales it to 62 × height × 88 and stands it on the board.</summary>
    private const float PileMeshHalfHeight = 0.344208f;

    /// <summary>Fitted digit anchors in card-local space, reproducing every captured Level digit: field monsters (2 samples) and hand cards (6).</summary>
    public static readonly Vector3 FieldLevelAnchor = new(0, -6.5f, 34.5f), HandLevelAnchor = new(0, 0.5f, -47.25f);

    public static Matrix SlotWorld(bool player, bool spellTrap, int index, BattlePosition? position)
    {
        float x = FirstSlotX + SlotPitch * index, z = spellTrap ? SpellTrapZ : MonsterZ;
        return Rotation(player, position) * Matrix.CreateTranslation(x, 0, player ? z : -z);
    }

    public static Matrix PendulumWorld(bool player, int index) =>
        Rotation(player, null) * Matrix.CreateTranslation(index == 0 ? -PendulumX : PendulumX, 0, player ? PendulumZ : -PendulumZ);

    public static Matrix FieldSpellWorld(bool player) =>
        Rotation(player, null) * Matrix.CreateTranslation(player ? -PileX : PileX, 0, player ? GraveyardZ : -GraveyardZ);

    /// <summary>World of a pile (before its count-driven bone): player Deck right and Extra Deck left, Graveyard right of the monsters;
    /// the opponent's are mirrored through the board centre.</summary>
    public static Matrix PileWorld(bool player, DuelPileKind pile)
    {
        float x = pile == DuelPileKind.Extra ? -PileX : PileX, z = pile == DuelPileKind.Graveyard ? GraveyardZ : DeckZ;
        return player ? Matrix.CreateTranslation(x, 0, z) : Opponent * Matrix.CreateTranslation(-x, 0, -z);
    }

    public static Matrix PileBone(int count)
    {
        float height = PileHeight(count);
        return Matrix.CreateScale(44f, height / (2 * PileMeshHalfHeight), 44f) * Matrix.CreateTranslation(0, height / 2, 0);
    }

    public static Vector3 PileTop(bool player, DuelPileKind pile, int count) =>
        Vector3.Transform(new Vector3(0, PileHeight(count), 0), PileWorld(player, pile));

    /// <summary>Observed for 4, 5 and 6 cards: x = ⌊(i − (n − 1) / 2) × 65⌋.</summary>
    public static Matrix HandWorld(int index, int count, bool focused) => HandTilt * Matrix.CreateTranslation(
        MathF.Floor((index - (count - 1) / 2f) * HandPitch), focused ? FocusedHandY : HandY, focused ? FocusedHandZ : HandZ);

    public static Matrix OpponentHandWorld(int index, int count) => HandTilt * Matrix.CreateTranslation(
        MathF.Floor((index - (count - 1) / 2f) * OpponentHandPitch), OpponentHandY, OpponentHandZ);

    private static Matrix Rotation(bool player, BattlePosition? position) =>
        position is BattlePosition.FaceUpDefense or BattlePosition.FaceDownDefense ? (player ? PlayerDefense : OpponentDefense)
        : player ? Matrix.Identity : Opponent;

    /// <summary>Screen position of a world point through the captured board camera (the card template's view, projection and viewport).</summary>
    public static Vector2 Project(DuelFieldProfile field, Vector3 world)
    {
        var card = field.Piece("card.face")[0];
        var view = PspDrawRenderer.Matrix43(field.Matrices[card.View]);
        float[] p = field.Matrices[card.Projection];
        var projection = new Matrix(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11], p[12], p[13], p[14], p[15]);
        var clip = Vector4.Transform(new Vector4(world, 1), view * projection);
        float[] v = field.Viewports[card.Viewport];
        return new(clip.X / clip.W * v[0] + v[3] - v[6], clip.Y / clip.W * v[1] + v[4] - v[7]);
    }
}
