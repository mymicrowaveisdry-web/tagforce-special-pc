using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace TagForceSpecial.PC.DuelUI.Input;

public readonly record struct GuiButtons(bool Up, bool Down, bool Left, bool Right, bool Confirm, bool Back, bool Inspect);

public sealed class GuiInput
{
    private KeyboardState previous;
    private GamePadState previousPad;
    private bool ready;

    public GuiButtons Poll(bool active)
        => Poll(Keyboard.GetState(), GamePad.GetState(PlayerIndex.One), active);

    public GuiButtons Poll(KeyboardState keys, GamePadState pad, bool active)
    {
        bool Edge(Keys key, Buttons button) => ready && active &&
            (keys.IsKeyDown(key) && previous.IsKeyUp(key) || pad.IsButtonDown(button) && previousPad.IsButtonUp(button));
        var value = new GuiButtons(Edge(Keys.Up, Buttons.DPadUp), Edge(Keys.Down, Buttons.DPadDown),
            Edge(Keys.Left, Buttons.DPadLeft), Edge(Keys.Right, Buttons.DPadRight),
            Edge(Keys.Enter, Buttons.A), Edge(Keys.Escape, Buttons.B), Edge(Keys.Tab, Buttons.Y));
        previous = keys;
        previousPad = pad;
        ready = active;
        return value;
    }
}
