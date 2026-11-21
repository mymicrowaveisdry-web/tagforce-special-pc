using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the turn, the current phase and the turn player.</summary>
public static class PhaseBar
{
    /// <summary>Measured: the six cells sit 35 pixels apart from DP at x = 210; the captured highlight is on Main Phase 1 (x = 280).</summary>
    public const float CellPitch = 35f;
    private const int CapturedPhase = (int)DuelPhase.Main1;

    public static void Draw(DuelDrawList list, DuelView view) =>
        list.Text(DuelPart.PhaseBar, $"T{view.Turn} {view.Phase} / P{(int)view.ActivePlayer + 1}", 8, 16, DuelPalette.Muted);

    /// <summary>Original strip: frame, six phase cells (drawn End Phase first), then the highlight, the current label and two glows on the
    /// current cell. The glows pulse in the original; this repeats the captured instant.</summary>
    public static void DrawOriginal(DuelDrawList list, DuelFieldProfile field, DuelPhase phase)
    {
        list.FieldPiece(DuelPart.PhaseBar, field, "phase.frame");
        list.FieldPiece(DuelPart.PhaseBar, field, "phase.cells");
        var offset = new Vector2(((int)phase - CapturedPhase) * CellPitch, 0);
        string label = field.Piece("phase.cells")[LabelIndex(phase)].Texture!;
        list.Field(DuelPart.PhaseBar, "phase.active", 0, new(Offset: offset));
        list.Field(DuelPart.PhaseBar, "phase.active", 1, new(Offset: offset, Texture: label));
        list.Field(DuelPart.PhaseBar, "phase.active", 2, new(Offset: offset));
        list.Field(DuelPart.PhaseBar, "phase.active", 3, new(Offset: offset));
    }

    /// <summary>Index of a phase's label draw in "phase.cells": background and label pairs from End Phase back to Draw Phase.</summary>
    public static int LabelIndex(DuelPhase phase) => (5 - (int)phase) * 2 + 1;
}
