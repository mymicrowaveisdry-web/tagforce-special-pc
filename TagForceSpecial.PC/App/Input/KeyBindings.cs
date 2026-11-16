using Microsoft.Xna.Framework.Input;

namespace TagForceSpecial.PC.App.Input;

/// <summary>Keyboard keys for each PSP action: up to <see cref="Slots"/> keys per action, a key bound to one action at most. The first
/// default key of each action is the one the retained presentation routes press, so defaults never change it. Escape always backs out
/// (it also cancels a rebind) and cannot be moved. Gamepad buttons are fixed and not part of this map.</summary>
public sealed class KeyBindings
{
    public const int Slots = 2;

    /// <summary>Rebindable actions in Controls-screen order with their PC names (saved under these names). PSP originals: Confirm =
    /// Cross, Back = Circle, Menu = Triangle, Switch = Square, Previous/Next page = L/R shoulder buttons.</summary>
    public static readonly (InputAction Action, string Name)[] Actions =
    [
        (InputAction.Up, "Up"), (InputAction.Down, "Down"), (InputAction.Left, "Left"), (InputAction.Right, "Right"),
        (InputAction.Confirm, "Confirm"), (InputAction.Back, "Back"), (InputAction.Menu, "Menu"), (InputAction.Switch, "Switch"),
        (InputAction.PreviousPage, "Previous page"), (InputAction.NextPage, "Next page"), (InputAction.Start, "Start"), (InputAction.Select, "Select")
    ];

    private static readonly Dictionary<InputAction, Keys[]> DefaultKeys = new()
    {
        [InputAction.Up] = [Keys.Up, Keys.W], [InputAction.Down] = [Keys.Down, Keys.S],
        [InputAction.Left] = [Keys.Left, Keys.A], [InputAction.Right] = [Keys.Right, Keys.D],
        [InputAction.Confirm] = [Keys.Enter, Keys.Z], [InputAction.Back] = [Keys.Escape, Keys.Back],
        [InputAction.Menu] = [Keys.Tab, Keys.C], [InputAction.Switch] = [Keys.Space, Keys.V],
        [InputAction.PreviousPage] = [Keys.Q, Keys.PageUp], [InputAction.NextPage] = [Keys.E, Keys.PageDown],
        [InputAction.Start] = [Keys.F1, Keys.Home], [InputAction.Select] = [Keys.F2, Keys.End]
    };

    /// <summary>Keys the Controls screen and host keep for themselves (F12 opens PC Tools).</summary>
    public static readonly Keys[] Reserved = [Keys.Escape, Keys.F12];

    /// <summary>Names saved before the rename, when these actions were the PSP shoulder buttons L and R.</summary>
    private static readonly Dictionary<string, string> LegacyNames = new() { ["Previous page"] = "L", ["Next page"] = "R" };

    private readonly Dictionary<InputAction, Keys?[]> keys;

    private KeyBindings(Dictionary<InputAction, Keys?[]> keys) => this.keys = keys;

    public static KeyBindings Default => new(DefaultKeys.ToDictionary(p => p.Key, p => p.Value.Select(k => (Keys?)k).ToArray()));

    public Keys? this[InputAction action, int slot] => keys[action][slot];

    /// <param name="textEntry">A text field has focus: keys that type characters do not act.</param>
    public InputAction Held(KeyboardState state, bool textEntry = false)
    {
        var held = InputAction.None;
        foreach (var (action, slots) in keys)
            if (slots.Any(k => k is { } key && state.IsKeyDown(key) && !(textEntry && Types(key)))) held |= action;
        return held;
    }

    /// <summary>Keys that type a character (or erase one) in a text field.</summary>
    public static bool Types(Keys key) => key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.NumPad0 and <= Keys.Divide
        or Keys.Space or Keys.Back or >= Keys.OemSemicolon and <= Keys.OemBackslash;

    /// <summary>A copy with <paramref name="key"/> in this slot. The key leaves any other slot that held it; a slot it leaves empty takes the
    /// replaced key, so no action loses a key by a swap. Escape stays on Back slot 0; reserved keys are refused.</summary>
    public KeyBindings With(InputAction action, int slot, Keys key)
    {
        if (Array.IndexOf(Reserved, key) >= 0 || action == InputAction.Back && slot == 0) return this;
        var next = keys.ToDictionary(p => p.Key, p => (Keys?[])p.Value.Clone());
        Keys? replaced = next[action][slot];
        foreach (var (other, slots) in next)
            for (int i = 0; i < Slots; i++)
                if (slots[i] == key && (other != action || i != slot)) slots[i] = replaced;
        next[action][slot] = key;
        return next[InputAction.Confirm].All(k => k is null) ? this : new(next);
    }

    /// <summary>Saved form: action name to key names ("" for an empty slot).</summary>
    public Dictionary<string, string[]> ToDocument() => Actions.ToDictionary(a => a.Name, a => keys[a.Action].Select(k => k?.ToString() ?? "").ToArray());

    public static KeyBindings FromDocument(IReadOnlyDictionary<string, string[]>? document)
    {
        if (document is null) return Default;
        var result = Default.keys.ToDictionary(p => p.Key, p => (Keys?[])p.Value.Clone());
        foreach (var (action, name) in Actions)
        {
            if (!document.TryGetValue(name, out var saved) && !(LegacyNames.TryGetValue(name, out var legacy) && document.TryGetValue(legacy, out saved))) continue;
            if (saved.Length != Slots) throw new InvalidDataException($"Key binding '{name}' needs {Slots} slots.");
            for (int i = 0; i < Slots; i++)
                result[action][i] = saved[i].Length == 0 ? null
                    : Enum.TryParse<Keys>(saved[i], out var key) && Enum.IsDefined(key) && key != Keys.None ? key
                    : throw new InvalidDataException($"Unknown key '{saved[i]}' for '{name}'.");
        }
        if (document.Keys.Any(k => Actions.All(a => a.Name != k) && !LegacyNames.ContainsValue(k))) throw new InvalidDataException("Unknown key binding action.");
        var bindings = new KeyBindings(result);
        bindings.Validate();
        return bindings;
    }

    private void Validate()
    {
        var all = keys.Values.SelectMany(s => s).OfType<Keys>().ToArray();
        if (all.Length != all.Distinct().Count()) throw new InvalidDataException("A key is bound to two actions.");
        if (keys[InputAction.Back][0] != Keys.Escape || all.Count(k => k == Keys.Escape) != 1 || all.Contains(Keys.F12))
            throw new InvalidDataException("Escape must stay on Back and F12 stays reserved.");
        if (keys[InputAction.Confirm].All(k => k is null)) throw new InvalidDataException("Confirm needs a key.");
    }

    public static string KeyName(Keys? key) => key switch
    {
        null => "-",
        Keys.Back => "Backspace", Keys.PageUp => "Page Up", Keys.PageDown => "Page Down",
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString()!
    };
}
