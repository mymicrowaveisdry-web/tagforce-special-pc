using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>The attack trail of the original (rel_DuelDraw: constructor 0x95897e0, update 0x958996c, draw 0x9589d74, Hermite 0x9589c0c).
/// A 64-entry alpha table and a head counter; each update (one per 2 vblanks, measured) wraps the counter at 63 or advances it by 3, sets
/// the entries in (counter − 3, counter] to 255 and fades every other entry by 10 (clamped at 0). Particle i of 60 sits on the Hermite curve
/// at t = i/63 between the endpoints with vertical tangents ±500.</summary>
public static class AttackTrail
{
    public const int Entries = 64, Particles = 60, Step = 3, Fade = 10, VblanksPerUpdate = 2;
    public const float Segments = 63, Tangent = 500, HeadLift = 17.5f;
    /// <summary>Particle and head colour (the constructor's a2 for the player's attack, 0xffff6060): RGB 0x60, 0x60, 0xff.</summary>
    public static readonly Vector3 Rgb = new(0x60 / 255f, 0x60 / 255f, 1f);

    /// <summary>Whole updates after <paramref name="elapsed"/> at 59.94 vblanks per second.</summary>
    public static int Updates(TimeSpan elapsed) => (int)(elapsed.TotalSeconds * 59.94 / VblanksPerUpdate);

    /// <summary>The alpha table after <paramref name="updates"/> updates of a fresh (zeroed) effect. After the first lap the table repeats
    /// every 22 updates, so long runs are reduced to one lap past the first.</summary>
    public static byte[] Alpha(int updates)
    {
        const int lap = (Entries - 1) / Step + 1; // counter values 3, 6, ..., 63, 0
        if (updates > 2 * lap) updates = lap + (updates - lap) % lap;
        var table = new byte[Entries];
        int counter = 0;
        for (int n = 0; n < updates; n++)
        {
            if (counter >= Entries - 1) counter = 0; else counter += Step;
            for (int i = 0; i < Entries; i++)
                table[i] = i <= counter && counter - Step < i ? (byte)255 : (byte)Math.Max(0, table[i] - Fade);
        }
        return table;
    }

    /// <summary>Particle <paramref name="index"/>: P = (1 − h)·P0 + h·P1 + (0, 500·t(1 − t), 0), t = index/63, h = t²(3 − 2t).</summary>
    public static Vector3 Point(Vector3 from, Vector3 to, int index)
    {
        float t = index / Segments, h = t * t * (3 - 2 * t);
        return Vector3.Lerp(from, to, h) + new Vector3(0, Tangent * t * (1 - t), 0);
    }
}

/// <summary>The original attack-target state on the field: the target cursor meshes, the sword over the attacker, and the trail with its
/// arrow head (aki-attack draws 70, 79, 96–159). The pointer is the focus overlay's, on the target.</summary>
public static class AttackTargetOverlay
{
    /// <summary>Measured (one column): the trail starts 30 above the attacker's slot and ends on the target slot; the sword stands at the
    /// attacker + (0.32, 30, −19.22). The PC rotates the sword toward the target in other columns.</summary>
    public static readonly Vector3 TrailStart = new(0, 30, 0), SwordOffset = new(0.32f, 30f, -19.22f);

    public static void DrawCursors(DuelDrawList list, DuelFieldProfile field, FocusContext context, FieldObject target)
    {
        var (centre, rotation) = InputFocusOverlay.Place(context, target);
        // Measured: both meshes one unit above the target slot, ×60, turned with the opponent's side.
        var side = target.Own ? Matrix.Identity : Matrix.CreateRotationY(MathF.PI);
        var world = Matrix.CreateScale(60f) * (target.Card is null ? side : rotation) * Matrix.CreateTranslation(centre.X, 1, centre.Z);
        list.Field(DuelPart.Focus, "attack.cursor", 0, new(World: world));
        list.Field(DuelPart.Focus, "attack.cursor.glow", 0, new(World: world));
    }

    public static void DrawTrail(DuelDrawList list, DuelFieldProfile field, Vector3 attacker, Vector3 target, int updates)
    {
        var towardTarget = target - attacker;
        var heading = Matrix.CreateRotationY(MathF.Atan2(-towardTarget.X, -towardTarget.Z));
        for (int i = 0; i < field.Piece("attack.sword").Length; i++)
        {
            var sword = Moved(field, "attack.sword", i, Vector3.Zero) * heading;
            sword.Translation = attacker + Vector3.TransformNormal(SwordOffset, heading);
            list.Field(DuelPart.Effects, "attack.sword", i, new(World: sword));
        }
        var alpha = AttackTrail.Alpha(updates);
        var from = attacker + TrailStart;
        for (int i = 0; i < AttackTrail.Particles; i++)
            list.Field(DuelPart.Effects, "attack.particle", 0, new(World: Moved(field, "attack.particle", 0, AttackTrail.Point(from, target, i)),
                Color: new Vector4(AttackTrail.Rgb, alpha[i] / 255f)));
        float top = alpha[AttackTrail.Particles] / 255f, bottom = alpha[AttackTrail.Entries - 1] / 255f;
        list.Field(DuelPart.Effects, "attack.head", 0, new(World: Moved(field, "attack.head", 0, target + new Vector3(0, AttackTrail.HeadLift, 0)),
            Color: new Vector4(AttackTrail.Rgb, 1), VertexAlpha: [top, top, bottom, bottom]));
    }

    /// <summary>The captured world matrix of a template draw with its translation replaced.</summary>
    private static Matrix Moved(DuelFieldProfile field, string piece, int index, Vector3 at) =>
        PspDrawRenderer.Matrix43(field.Matrices[field.Piece(piece)[index].World]) with { M41 = at.X, M42 = at.Y, M43 = at.Z };

    /// <summary>The attacker of a target selection: the monster slot holding the choices' acting card.</summary>
    public static Vector3? AttackerCentre(DuelView view, IReadOnlyList<ChoiceView> choices)
    {
        if (choices.FirstOrDefault()?.FocusInstanceId is not { } id) return null;
        for (int i = 0; i < view.Self.Monsters.Count; i++)
            if (view.Self.Monsters[i]?.InstanceId == id) return DuelLayoutProfile.SlotWorld(true, false, i, null).Translation;
        return null;
    }
}

/// <summary>Battle Phase ATK/DEF readouts over face-up monsters (aki-attack draws 81–93; battle-idle shows them on both sides): ATK white,
/// DEF at RGB 0x80, 9 px lower, both centred like the other card_icon03 numerals. Anchor fitted from one column (exact on both sides):
/// slot-local (0, 26, 90), turned with the slot's side, without the defence rotation (not captured).</summary>
public static class BattleReadouts
{
    public static readonly Vector3 Anchor = new(0, 26, 90);
    public static readonly Vector4 Defense = new(128 / 255f, 128 / 255f, 128 / 255f, 1);
    public const int LineSpacing = 9;

    public static void Draw(DuelDrawList list, DuelFieldProfile field, DuelView view)
    {
        if (view.Phase != DuelPhase.Battle) return;
        foreach (var (owner, own) in new[] { (view.Self, true), (view.Opponent, false) })
            for (int i = 0; i < owner.Monsters.Count; i++)
            {
                if (owner.Monsters[i] is not { Hidden: false, FaceDown: false, Attack: { } atk, Defense: { } def }) continue;
                var at = Vector3.Transform(Anchor, DuelLayoutProfile.SlotWorld(own, false, i, null));
                FieldCounters.Draw(list, DuelPart.Cards, field, atk, at, FieldCounters.White);
                FieldCounters.Draw(list, DuelPart.Cards, field, def, at, Defense, LineSpacing);
            }
    }
}
