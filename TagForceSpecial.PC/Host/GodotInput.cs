using Godot;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.Host;

/// <summary>Samples Godot's keyboard and first gamepad once per fixed step into the shared <see cref="InputFrame"/>. Keys go through the saved
/// <see cref="KeyBindings"/> (layout-dependent keycodes, as the MonoGame host read them); gamepad buttons are fixed.</summary>
internal sealed class GodotInput
{
    private readonly InputEdges edges = new();
    public KeyBindings Bindings { get; set; } = KeyBindings.Default;
    /// <summary>A text field has focus; typing keys do not act.</summary>
    public bool TextEntry { get; set; }

    private static readonly (InputAction Action, JoyButton Button)[] Pad =
    [
        (InputAction.Up, JoyButton.DpadUp), (InputAction.Down, JoyButton.DpadDown), (InputAction.Left, JoyButton.DpadLeft),
        (InputAction.Right, JoyButton.DpadRight), (InputAction.Confirm, JoyButton.A), (InputAction.Back, JoyButton.B),
        (InputAction.Menu, JoyButton.Y), (InputAction.Switch, JoyButton.X), (InputAction.PreviousPage, JoyButton.LeftShoulder),
        (InputAction.NextPage, JoyButton.RightShoulder), (InputAction.Start, JoyButton.Start), (InputAction.Select, JoyButton.Back)
    ];

    public InputFrame Next(bool active, PointerInput pointer = default)
    {
        var held = Bindings.Held(IsDown, TextEntry);
        foreach (var (action, button) in Pad) if (Input.IsJoyButtonPressed(0, button)) held |= action;
        return edges.Next(held, IsDown(PhysicalKey.F12), active, pointer);
    }

    private static bool IsDown(PhysicalKey key) => GodotKeys.ToGodot(key) is { } godot && Input.IsKeyPressed(godot);

    public static void TypeName(InputEventKey key, TitleFlow flow)
    {
        if (key.Keycode == Key.Backspace) flow.TypeCharacter('\b');
        else if (key.Unicode is > 0 and <= char.MaxValue && !char.IsSurrogate((char)key.Unicode))
            flow.TypeCharacter((char)key.Unicode);
    }
}

/// <summary>Physical keys (Windows virtual-key codes) as Godot keycodes.</summary>
internal static class GodotKeys
{
    private static readonly Dictionary<PhysicalKey, Key> Special = new()
    {
        [PhysicalKey.Back] = Key.Backspace, [PhysicalKey.Tab] = Key.Tab, [PhysicalKey.Enter] = Key.Enter, [PhysicalKey.Escape] = Key.Escape,
        [PhysicalKey.Space] = Key.Space, [PhysicalKey.PageUp] = Key.Pageup, [PhysicalKey.PageDown] = Key.Pagedown, [PhysicalKey.End] = Key.End,
        [PhysicalKey.Home] = Key.Home, [PhysicalKey.Left] = Key.Left, [PhysicalKey.Up] = Key.Up, [PhysicalKey.Right] = Key.Right,
        [PhysicalKey.Down] = Key.Down, [PhysicalKey.Insert] = Key.Insert, [PhysicalKey.Delete] = Key.Delete,
        [PhysicalKey.Multiply] = Key.KpMultiply, [PhysicalKey.Add] = Key.KpAdd, [PhysicalKey.Subtract] = Key.KpSubtract,
        [PhysicalKey.Decimal] = Key.KpPeriod, [PhysicalKey.Divide] = Key.KpDivide,
        [PhysicalKey.LeftShift] = Key.Shift, [PhysicalKey.RightShift] = Key.Shift, [PhysicalKey.LeftControl] = Key.Ctrl,
        [PhysicalKey.RightControl] = Key.Ctrl, [PhysicalKey.LeftAlt] = Key.Alt, [PhysicalKey.RightAlt] = Key.Alt,
        [PhysicalKey.OemSemicolon] = Key.Semicolon, [PhysicalKey.OemPlus] = Key.Equal, [PhysicalKey.OemComma] = Key.Comma,
        [PhysicalKey.OemMinus] = Key.Minus, [PhysicalKey.OemPeriod] = Key.Period, [PhysicalKey.OemQuestion] = Key.Slash,
        [PhysicalKey.OemTilde] = Key.Quoteleft, [PhysicalKey.OemOpenBrackets] = Key.Bracketleft, [PhysicalKey.OemPipe] = Key.Backslash,
        [PhysicalKey.OemCloseBrackets] = Key.Bracketright, [PhysicalKey.OemQuotes] = Key.Apostrophe
    };

    public static Key? ToGodot(PhysicalKey key) => key switch
    {
        >= PhysicalKey.A and <= PhysicalKey.Z => (Key)(int)key,
        >= PhysicalKey.D0 and <= PhysicalKey.D9 => (Key)(int)key,
        >= PhysicalKey.NumPad0 and <= PhysicalKey.NumPad9 => Key.Kp0 + (key - PhysicalKey.NumPad0),
        >= PhysicalKey.F1 and <= PhysicalKey.F24 => Key.F1 + (key - PhysicalKey.F1),
        _ => Special.TryGetValue(key, out var mapped) ? mapped : null
    };
}
