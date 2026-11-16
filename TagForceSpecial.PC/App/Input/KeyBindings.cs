using Microsoft.Xna.Framework.Input;

namespace TagForceSpecial.PC.App.Input;

/// <summary>Keyboard keys for each PSP action: up to <see cref="Slots"/> keys per action, a key bound to one action at most. The first
/// default key of each action is the one the retained presentation routes press, so defaults never change it. Escape always backs out
/// (it also cancels a rebind) and cannot be moved. Gamepad buttons are fixed and not part of this map.</summary>
public sealed class KeyBindings
{
    public const int Slots = 2;

    /// <summary>Rebindable actions in Controls-screen order with their PC names (PSP Cross confirms, Circle backs out on the PC).</summary>
    public static readonly (PspAction Action, string Name)[] Actions =
    [
        (PspAction.Up, "Up"), (PspAction.Down, "Down"), (PspAction.Left, "Left"), (PspAction.Right, "Right"),
        (PspAction.Cross, "Confirm"), (PspAction.Circle, "Back"), (PspAction.Triangle, "Menu"), (PspAction.Square, "Switch"),
        (PspAction.L, "L"), (PspAction.R, "R"), (PspAction.Start, "Start"), (PspAction.Select, "Select")
    ];

    private static readonly Dictionary<PspAction, Keys[]> DefaultKeys = new()
    {
        [PspAction.Up] = [Keys.Up, Keys.W], [PspAction.Down] = [Keys.Down, Keys.S],
        [PspAction.Left] = [Keys.Left, Keys.A], [PspAction.Right] = [Keys.Right, Keys.D],
        [PspAction.Cross] = [Keys.Enter, Keys.Z], [PspAction.Circle] = [Keys.Escape, Keys.Back],
        [PspAction.Triangle] = [Keys.Tab, Keys.C], [PspAction.Square] = [Keys.Space, Keys.V],
        [PspAction.L] = [Keys.Q, Keys.PageUp], [PspAction.R] = [Keys.E, Keys.PageDown],
        [PspAction.Start] = [Keys.F1, Keys.Home], [PspAction.Select] = [Keys.F2, Keys.End]
    };

    /// <summary>Keys the Controls screen and host keep for themselves (F12 opens PC Tools).</summary>
    public static readonly Keys[] Reserved = [Keys.Escape, Keys.F12];

    private readonly Dictionary<PspAction, Keys?[]> keys;

    private KeyBindings(Dictionary<PspAction, Keys?[]> keys) => this.keys = keys;

    public static KeyBindings Default => new(DefaultKeys.ToDictionary(p => p.Key, p => p.Value.Select(k => (Keys?)k).ToArray()));

    public Keys? this[PspAction action, int slot] => keys[action][slot];

    /// <param name="textEntry">A text field has focus: keys that type characters do not act.</param>
    public PspAction Held(KeyboardState state, bool textEntry = false)
    {
        var held = PspAction.None;
        foreach (var (action, slots) in keys)
            if (slots.Any(k => k is { } key && state.IsKeyDown(key) && !(textEntry && Types(key)))) held |= action;
        return held;
    }

    /// <summary>Keys that type a character (or erase one) in a text field.</summary>
    public static bool Types(Keys key) => key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.NumPad0 and <= Keys.Divide
        or Keys.Space or Keys.Back or >= Keys.OemSemicolon and <= Keys.OemBackslash;

    /// <summary>A copy with <paramref name="key"/> in this slot. The key leaves any other slot that held it; a slot it leaves empty takes the
    /// replaced key, so no action loses a key by a swap. Escape stays on Back slot 0; reserved keys are refused.</summary>
    public KeyBindings With(PspAction action, int slot, Keys key)
    {
        if (Array.IndexOf(Reserved, key) >= 0 || action == PspAction.Circle && slot == 0) return this;
        var next = keys.ToDictionary(p => p.Key, p => (Keys?[])p.Value.Clone());
        Keys? replaced = next[action][slot];
        foreach (var (other, slots) in next)
            for (int i = 0; i < Slots; i++)
                if (slots[i] == key && (other != action || i != slot)) slots[i] = replaced;
        next[action][slot] = key;
        return next[PspAction.Cross].All(k => k is null) ? this : new(next);
    }

    /// <summary>Saved form: action name to key names ("" for an empty slot).</summary>
    public Dictionary<string, string[]> ToDocument() => Actions.ToDictionary(a => a.Name, a => keys[a.Action].Select(k => k?.ToString() ?? "").ToArray());

    public static KeyBindings FromDocument(IReadOnlyDictionary<string, string[]>? document)
    {
        if (document is null) return Default;
        var result = Default.keys.ToDictionary(p => p.Key, p => (Keys?[])p.Value.Clone());
        foreach (var (action, name) in Actions)
        {
            if (!document.TryGetValue(name, out var saved)) continue;
            if (saved.Length != Slots) throw new InvalidDataException($"Key binding '{name}' needs {Slots} slots.");
            for (int i = 0; i < Slots; i++)
                result[action][i] = saved[i].Length == 0 ? null
                    : Enum.TryParse<Keys>(saved[i], out var key) && Enum.IsDefined(key) && key != Keys.None ? key
                    : throw new InvalidDataException($"Unknown key '{saved[i]}' for '{name}'.");
        }
        if (document.Keys.Any(k => Actions.All(a => a.Name != k))) throw new InvalidDataException("Unknown key binding action.");
        var bindings = new KeyBindings(result);
        bindings.Validate();
        return bindings;
    }

    private void Validate()
    {
        var all = keys.Values.SelectMany(s => s).OfType<Keys>().ToArray();
        if (all.Length != all.Distinct().Count()) throw new InvalidDataException("A key is bound to two actions.");
        if (keys[PspAction.Circle][0] != Keys.Escape || all.Count(k => k == Keys.Escape) != 1 || all.Contains(Keys.F12))
            throw new InvalidDataException("Escape must stay on Back and F12 stays reserved.");
        if (keys[PspAction.Cross].All(k => k is null)) throw new InvalidDataException("Confirm needs a key.");
    }

    public static string KeyName(Keys? key) => key switch
    {
        null => "-",
        Keys.Back => "Backspace", Keys.PageUp => "Page Up", Keys.PageDown => "Page Down",
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString()!
    };
}
