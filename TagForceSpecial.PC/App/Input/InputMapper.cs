using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Input;

/// <summary>What the player asks for, whatever the device. Formerly <c>PspAction</c>: each member was named after the PSP button the
/// original game reads for it (noted per member). Values are unchanged, so recorded traces and saved data keep their meaning.</summary>
[Flags]
public enum InputAction
{
    None = 0,
    Up = 1,       // PSP D-pad Up
    Down = 2,     // PSP D-pad Down
    Left = 4,     // PSP D-pad Left
    Right = 8,    // PSP D-pad Right
    Confirm = 16, // was Cross (PSP × button); does what the original does with Circle (○): confirm
    Back = 32,    // was Circle (PSP ○ button); does what the original does with Cross (×): back out
    Menu = 64,    // was Triangle (PSP △ button); opens screen menus such as the Deck Edit Menu
    Switch = 128, // was Square (PSP □ button); switches tabs or lists
    PreviousPage = 256, // was L (PSP L shoulder button)
    NextPage = 512,     // was R (PSP R shoulder button)
    Start = 1024, // PSP START button
    Select = 2048 // PSP SELECT button
}

/// <summary>Mouse in 480 x 272 canvas coordinates. <see cref="Position"/> is null outside the picture (letterbox or outside the window);
/// presses there are dropped. <see cref="Wheel"/> counts notches, positive away from the user.</summary>
public readonly record struct PointerInput(CanvasPoint? Position, bool Moved, bool LeftPressed, bool RightPressed, int Wheel)
{
    public bool Any => Moved || LeftPressed || RightPressed || Wheel != 0;
}

/// <summary>One frame of player input. Formerly <c>PspInputFrame</c>.</summary>
public readonly record struct InputFrame(InputAction Pressed, InputAction Held, bool UtilityRequested = false, PointerInput Pointer = default)
{
    public bool IsPressed(InputAction action) => (Pressed & action) != 0;
    /// <summary>Adds presses (held for this frame too, as a key tap would be) from a non-key source such as the mouse.</summary>
    public InputFrame With(InputAction pressed) => this with { Pressed = Pressed | pressed, Held = Held | pressed };
    public GuiButtons ToGuiButtons() => new(IsPressed(InputAction.Up), IsPressed(InputAction.Down),
        IsPressed(InputAction.Left), IsPressed(InputAction.Right), IsPressed(InputAction.Confirm),
        IsPressed(InputAction.Back), IsPressed(InputAction.Menu));
}

/// <summary>Formerly <c>PspInputMapper</c>. The sole physical input sampler. Refocus primes held actions without triggering them. Keys follow <see cref="Bindings"/>;
/// gamepad buttons are fixed; the mouse is sampled only when a picture rectangle is given.</summary>
public sealed class InputMapper
{
    private InputAction previous;
    private bool previousUtility;
    private bool ready;
    private MouseState? previousMouse;
    private int wheelRemainder;
    public KeyBindings Bindings { get; set; } = KeyBindings.Default;
    /// <summary>A text field has focus; typing keys do not act (see <see cref="KeyBindings.Types"/>).</summary>
    public bool TextEntry { get; set; }
    /// <summary>The keyboard state of the last poll (the Controls screen captures the next key from it).</summary>
    public KeyboardState LastKeys { get; private set; }

    public InputFrame Poll(bool active, CanvasRect? picture = null) =>
        Poll(Keyboard.GetState(), GamePad.GetState(PlayerIndex.One), active, picture is { } rect ? (Mouse.GetState(), rect) : null);

    public InputFrame Poll(KeyboardState keys, GamePadState pad, bool active, (MouseState State, CanvasRect Picture)? mouse = null)
    {
        LastKeys = keys;
        InputAction held = Bindings.Held(keys, TextEntry);
        void Map(InputAction action, Buttons button) { if (pad.IsButtonDown(button)) held |= action; }
        Map(InputAction.Up, Buttons.DPadUp); Map(InputAction.Down, Buttons.DPadDown);
        Map(InputAction.Left, Buttons.DPadLeft); Map(InputAction.Right, Buttons.DPadRight);
        Map(InputAction.Confirm, Buttons.A); Map(InputAction.Back, Buttons.B);
        Map(InputAction.Menu, Buttons.Y); Map(InputAction.Switch, Buttons.X);
        Map(InputAction.PreviousPage, Buttons.LeftShoulder); Map(InputAction.NextPage, Buttons.RightShoulder);
        Map(InputAction.Start, Buttons.Start); Map(InputAction.Select, Buttons.Back);
        var pointer = mouse is { } m && ready && active ? Sample(m.State, m.Picture) : default;
        if (mouse is { } current) previousMouse = current.State;
        var frame = new InputFrame(ready && active ? held & ~previous : InputAction.None, active ? held : InputAction.None,
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
