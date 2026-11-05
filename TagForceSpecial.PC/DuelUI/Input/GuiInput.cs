using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.DuelUI.Input;

public readonly record struct GuiButtons(bool Up, bool Down, bool Left, bool Right, bool Confirm, bool Back, bool Inspect);

public sealed class GuiInput
{
    private readonly PspInputMapper mapper = new();

    public GuiButtons Poll(bool active)
        => mapper.Poll(active).ToGuiButtons();

    public GuiButtons Poll(KeyboardState keys, GamePadState pad, bool active)
        => mapper.Poll(keys, pad, active).ToGuiButtons();
}
