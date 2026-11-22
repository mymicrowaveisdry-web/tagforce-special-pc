namespace TagForceSpecial.PC.App.Input;

/// <summary>Turns the actions held this frame into presses, for any device sampler. Refocus primes held actions without triggering them:
/// the first active frame after an inactive one reports nothing pressed, as the MonoGame host's mapper did.</summary>
public sealed class InputEdges
{
    private InputAction previous;
    private bool previousUtility;
    private bool ready;

    /// <summary>The previous frame was active, so this frame's presses count (samplers with state of their own, such as the mouse, read it).</summary>
    public bool Ready => ready;

    /// <param name="held">Actions held now, from keys (through the bindings) and gamepad buttons.</param>
    /// <param name="utilityDown">The PC utility key (F12) is down.</param>
    /// <param name="active">The window has focus.</param>
    /// <param name="pointer">This frame's mouse sample; dropped unless ready and active.</param>
    public InputFrame Next(InputAction held, bool utilityDown, bool active, PointerInput pointer = default)
    {
        var frame = new InputFrame(ready && active ? held & ~previous : InputAction.None, active ? held : InputAction.None,
            ready && active && utilityDown && !previousUtility, ready && active ? pointer : default);
        previousUtility = utilityDown;
        previous = held; ready = active;
        return frame;
    }
}
