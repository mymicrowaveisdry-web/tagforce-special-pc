using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.DuelUI.Input;

namespace TagForceSpecial.PC.App.Input;

[Flags]
public enum PspAction { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Cross = 16, Circle = 32, Triangle = 64, Square = 128, L = 256, R = 512, Start = 1024, Select = 2048 }

public readonly record struct PspInputFrame(PspAction Pressed, PspAction Held, bool UtilityRequested = false)
{
    public bool IsPressed(PspAction action) => (Pressed & action) != 0;
    public GuiButtons ToGuiButtons() => new(IsPressed(PspAction.Up), IsPressed(PspAction.Down),
        IsPressed(PspAction.Left), IsPressed(PspAction.Right), IsPressed(PspAction.Cross),
        IsPressed(PspAction.Circle), IsPressed(PspAction.Triangle));
}

/// <summary>The sole physical input sampler. Refocus primes held actions without triggering them.</summary>
public sealed class PspInputMapper
{
    private PspAction previous;
    private bool previousUtility;
    private bool ready;
    public PspInputFrame Poll(bool active) => Poll(Keyboard.GetState(), GamePad.GetState(PlayerIndex.One), active);
    public PspInputFrame Poll(KeyboardState keys, GamePadState pad, bool active)
    {
        PspAction held = PspAction.None;
        void Map(PspAction action, Keys key, Buttons button)
        { if (keys.IsKeyDown(key) || pad.IsButtonDown(button)) held |= action; }
        Map(PspAction.Up, Keys.Up, Buttons.DPadUp); Map(PspAction.Down, Keys.Down, Buttons.DPadDown);
        Map(PspAction.Left, Keys.Left, Buttons.DPadLeft); Map(PspAction.Right, Keys.Right, Buttons.DPadRight);
        Map(PspAction.Cross, Keys.Enter, Buttons.A); Map(PspAction.Circle, Keys.Escape, Buttons.B);
        Map(PspAction.Triangle, Keys.Tab, Buttons.Y); Map(PspAction.Square, Keys.Space, Buttons.X);
        Map(PspAction.L, Keys.Q, Buttons.LeftShoulder); Map(PspAction.R, Keys.E, Buttons.RightShoulder);
        Map(PspAction.Start, Keys.F1, Buttons.Start); Map(PspAction.Select, Keys.F2, Buttons.Back);
        var frame = new PspInputFrame(ready && active ? held & ~previous : PspAction.None, active ? held : PspAction.None,
            ready && active && keys.IsKeyDown(Keys.F12) && !previousUtility);
        previousUtility = keys.IsKeyDown(Keys.F12);
        previous = held; ready = active;
        return frame;
    }
}
