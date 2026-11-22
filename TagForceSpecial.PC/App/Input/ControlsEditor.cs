using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Input;

public enum ControlsOutcome { None, Changed, Back }

/// <summary>The Controls screen: one row per action (two key slots each), then Reset to defaults and Back. Confirm on a slot waits for the
/// next key (Escape or a right click cancels; F12 and Escape cannot be taken); Left/Right pick the slot. The mouse picks rows and slots
/// directly. Bindings change only through <see cref="KeyBindings.With"/>, so a key never ends up on two actions.</summary>
public sealed class ControlsEditor(KeyBindings bindings)
{
    public const float Top = 34, Pitch = 15, NameX = 24, SlotX0 = 168, SlotX1 = 300, SlotWidth = 120, Right = 456;
    public static int Rows => KeyBindings.Actions.Length + 2;
    public int ResetRow => KeyBindings.Actions.Length;
    public int BackRow => KeyBindings.Actions.Length + 1;
    public KeyBindings Bindings { get; private set; } = bindings;
    public int Row { get; private set; }
    public int Slot { get; private set; }
    public bool Capturing { get; private set; }
    private KeyboardState before;

    public static float RowY(int row) => Top + row * Pitch;

    /// <param name="keys">This frame's keyboard (raw, for capture).</param>
    public ControlsOutcome Handle(InputFrame input, KeyboardState keys)
    {
        var previous = before;
        before = keys;
        var p = input.Pointer;
        if (Capturing)
        {
            if (p.RightPressed || keys.IsKeyDown(Keys.Escape) && !previous.IsKeyDown(Keys.Escape)) { Capturing = false; return ControlsOutcome.None; }
            var pressed = keys.GetPressedKeys().Where(k => !previous.IsKeyDown(k)).Select(k => (PhysicalKey)k)
                .Where(k => Array.IndexOf(KeyBindings.Reserved, k) < 0).ToArray();
            if (pressed.Length == 0) return ControlsOutcome.None;
            Capturing = false;
            var next = Bindings.With(KeyBindings.Actions[Row].Action, Slot, pressed[0]);
            if (ReferenceEquals(next, Bindings)) return ControlsOutcome.None;
            Bindings = next;
            return ControlsOutcome.Changed;
        }
        bool click = false;
        if (Hit(p.Position) is var (row, slot) && (p.Moved || p.LeftPressed))
        {
            Row = row;
            if (slot is int s) Slot = s;
            click = p.LeftPressed;
        }
        if (input.IsPressed(InputAction.Up) || p.Wheel > 0) Row = (Row + Rows - 1) % Rows;
        else if (input.IsPressed(InputAction.Down) || p.Wheel < 0) Row = (Row + 1) % Rows;
        else if (input.IsPressed(InputAction.Left) || input.IsPressed(InputAction.Right)) Slot = 1 - Slot;
        if (input.IsPressed(InputAction.Back) || p.RightPressed) return ControlsOutcome.Back;
        if (!input.IsPressed(InputAction.Confirm) && !click) return ControlsOutcome.None;
        if (Row == BackRow) return ControlsOutcome.Back;
        if (Row == ResetRow) { Bindings = KeyBindings.Default; return ControlsOutcome.Changed; }
        // Back slot 0 is Escape for good; it cannot be captured.
        if (!(KeyBindings.Actions[Row].Action == InputAction.Back && Slot == 0)) Capturing = true;
        return ControlsOutcome.None;
    }

    /// <summary>Row under the pointer and, on an action row, the slot column under it.</summary>
    public (int Row, int? Slot)? Hit(CanvasPoint? at)
    {
        if (at is not { } point || point.X < NameX - 4 || point.X >= Right || point.Y < Top - 2) return null;
        int row = (int)MathF.Floor((point.Y - Top + 2) / Pitch);
        if (row < 0 || row >= Rows) return null;
        if (row >= KeyBindings.Actions.Length) return (row, null);
        int? slot = point.X >= SlotX1 ? 1 : point.X >= SlotX0 ? 0 : null;
        return (row, slot);
    }
}
