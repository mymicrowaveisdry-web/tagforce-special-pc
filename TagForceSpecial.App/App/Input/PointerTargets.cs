using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Input;

/// <summary>Turns the mouse into the same actions the keys give, so every screen keeps one input path: hovering a target focuses it,
/// a left click focuses it and confirms, a right click backs out, and the wheel steps up or down. Screens pass their own hit test.</summary>
public static class PointerTargets
{
    /// <param name="hit">Target under the pointer (null for none).</param>
    /// <param name="focus">Currently focused target.</param>
    /// <param name="setFocus">Moves focus to a target without confirming it.</param>
    /// <param name="wheel">Actions for wheel away from / toward the user (default Up / Down; null ignores the wheel).</param>
    public static InputFrame Apply(InputFrame input, int? hit, int focus, Action<int> setFocus,
        (InputAction Away, InputAction Toward)? wheel = null)
    {
        var p = input.Pointer;
        if (!p.Any) return input;
        var pressed = InputAction.None;
        if (hit is int target && (p.Moved || p.LeftPressed) && target != focus) setFocus(target);
        if (p.LeftPressed && hit is not null) pressed |= InputAction.Confirm;
        if (p.RightPressed) pressed |= InputAction.Back;
        var (away, toward) = wheel ?? (InputAction.Up, InputAction.Down);
        if (p.Wheel > 0) pressed |= away;
        else if (p.Wheel < 0) pressed |= toward;
        return input.With(pressed);
    }

    /// <summary>Clicks only (no focus targets): left click anywhere confirms, right click backs out.</summary>
    public static InputFrame Clicks(InputFrame input, InputAction left = InputAction.Confirm) =>
        input.Pointer.LeftPressed ? input.With(left) : input.Pointer.RightPressed ? input.With(InputAction.Back) : input;

    public static bool Inside(CanvasPoint point, float x0, float y0, float x1, float y1) =>
        point.X >= x0 && point.X < x1 && point.Y >= y0 && point.Y < y1;

    /// <summary>Index of the first rectangle [x0, y0, x1, y1] containing the point.</summary>
    public static int? Find(CanvasPoint? point, IReadOnlyList<float[]> rects)
    {
        if (point is not { } at) return null;
        for (int i = 0; i < rects.Count; i++)
            if (Inside(at, rects[i][0], rects[i][1], rects[i][2], rects[i][3])) return i;
        return null;
    }
}
