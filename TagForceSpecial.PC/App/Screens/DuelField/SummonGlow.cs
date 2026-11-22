using Microsoft.Xna.Framework;
using TagForceSpecial.Core;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>The Normal Summon landing glow (matched walk): 16 additive lens_aP8A beams per 30 fps game frame, replayed from the captured
/// centre-zone frames and moved to the summoned zone (inferred for other zones: same shape, translated with the zone).</summary>
internal static class SummonGlow
{
    private const int CapturedZone = 2;

    public static void Draw(DuelDrawList list, DuelFieldProfile field, int frame, int zone)
    {
        string piece = "summon.glow." + frame;
        var shift = DuelLayoutProfile.SlotWorld(true, false, zone, BattlePosition.FaceUpAttack).Translation
            - DuelLayoutProfile.SlotWorld(true, false, CapturedZone, BattlePosition.FaceUpAttack).Translation;
        var draws = field.Piece(piece);
        for (int i = 0; i < draws.Length; i++)
            list.Field(DuelPart.Cards, piece, i, new(World: PspDrawRenderer.Matrix43(field.Matrices[draws[i].World]) * Matrix.CreateTranslation(shift)));
    }
}
