using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Input;

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
        InputAction held = Bindings.Held(key => keys.IsKeyDown((Keys)key), TextEntry);
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
