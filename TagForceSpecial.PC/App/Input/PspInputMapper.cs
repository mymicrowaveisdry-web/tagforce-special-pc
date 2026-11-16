using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Input;

[Flags]
public enum PspAction { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Cross = 16, Circle = 32, Triangle = 64, Square = 128, L = 256, R = 512, Start = 1024, Select = 2048 }

/// <summary>Mouse in 480 x 272 canvas coordinates. <see cref="Position"/> is null outside the picture (letterbox or outside the window);
/// presses there are dropped. <see cref="Wheel"/> counts notches, positive away from the user.</summary>
public readonly record struct PointerInput(CanvasPoint? Position, bool Moved, bool LeftPressed, bool RightPressed, int Wheel)
{
    public bool Any => Moved || LeftPressed || RightPressed || Wheel != 0;
}

public readonly record struct PspInputFrame(PspAction Pressed, PspAction Held, bool UtilityRequested = false, PointerInput Pointer = default)
{
    public bool IsPressed(PspAction action) => (Pressed & action) != 0;
    /// <summary>Adds presses (held for this frame too, as a key tap would be) from a non-key source such as the mouse.</summary>
    public PspInputFrame With(PspAction pressed) => this with { Pressed = Pressed | pressed, Held = Held | pressed };
    public GuiButtons ToGuiButtons() => new(IsPressed(PspAction.Up), IsPressed(PspAction.Down),
        IsPressed(PspAction.Left), IsPressed(PspAction.Right), IsPressed(PspAction.Cross),
        IsPressed(PspAction.Circle), IsPressed(PspAction.Triangle));
}

/// <summary>The sole physical input sampler. Refocus primes held actions without triggering them. Keys follow <see cref="Bindings"/>;
/// gamepad buttons are fixed; the mouse is sampled only when a picture rectangle is given.</summary>
public sealed class PspInputMapper
{
    private PspAction previous;
    private bool previousUtility;
    private bool ready;
    private MouseState? previousMouse;
    private int wheelRemainder;
    public KeyBindings Bindings { get; set; } = KeyBindings.Default;
    /// <summary>A text field has focus; typing keys do not act (see <see cref="KeyBindings.Types"/>).</summary>
    public bool TextEntry { get; set; }
    /// <summary>The keyboard state of the last poll (the Controls screen captures the next key from it).</summary>
    public KeyboardState LastKeys { get; private set; }

    public PspInputFrame Poll(bool active, CanvasRect? picture = null) =>
        Poll(Keyboard.GetState(), GamePad.GetState(PlayerIndex.One), active, picture is { } rect ? (Mouse.GetState(), rect) : null);

    public PspInputFrame Poll(KeyboardState keys, GamePadState pad, bool active, (MouseState State, CanvasRect Picture)? mouse = null)
    {
        LastKeys = keys;
        PspAction held = Bindings.Held(keys, TextEntry);
        void Map(PspAction action, Buttons button) { if (pad.IsButtonDown(button)) held |= action; }
        Map(PspAction.Up, Buttons.DPadUp); Map(PspAction.Down, Buttons.DPadDown);
        Map(PspAction.Left, Buttons.DPadLeft); Map(PspAction.Right, Buttons.DPadRight);
        Map(PspAction.Cross, Buttons.A); Map(PspAction.Circle, Buttons.B);
        Map(PspAction.Triangle, Buttons.Y); Map(PspAction.Square, Buttons.X);
        Map(PspAction.L, Buttons.LeftShoulder); Map(PspAction.R, Buttons.RightShoulder);
        Map(PspAction.Start, Buttons.Start); Map(PspAction.Select, Buttons.Back);
        var pointer = mouse is { } m && ready && active ? Sample(m.State, m.Picture) : default;
        if (mouse is { } current) previousMouse = current.State;
        var frame = new PspInputFrame(ready && active ? held & ~previous : PspAction.None, active ? held : PspAction.None,
            ready && active && keys.IsKeyDown(Keys.F12) && !previousUtility, pointer);
        previousUtility = keys.IsKeyDown(Keys.F12);
        previous = held; ready = active;
        return frame;
    }

    private PointerInput Sample(MouseState state, CanvasRect picture)
    {
        var position = PspViewport.ToCanvas(state.X, state.Y, picture);
        if (previousMouse is not { } before) return new(position, false, false, false, 0);
        bool moved = state.X != before.X || state.Y != before.Y;
        bool left = position is not null && state.LeftButton == ButtonState.Pressed && before.LeftButton == ButtonState.Released;
        bool right = position is not null && state.RightButton == ButtonState.Pressed && before.RightButton == ButtonState.Released;
        // Notches of 120; smaller steps (touchpads, free-spinning wheels) accumulate until they make one.
        wheelRemainder = position is null ? 0 : wheelRemainder + state.ScrollWheelValue - before.ScrollWheelValue;
        int wheel = wheelRemainder / 120;
        wheelRemainder -= wheel * 120;
        return new(position, moved && position is not null, left, right, wheel);
    }
}
