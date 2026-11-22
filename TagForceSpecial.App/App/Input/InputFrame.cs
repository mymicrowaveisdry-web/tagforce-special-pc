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
