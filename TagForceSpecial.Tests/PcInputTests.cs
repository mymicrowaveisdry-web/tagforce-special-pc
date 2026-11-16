using Microsoft.Xna.Framework.Input;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.Tests;

/// <summary>PC input: rebindable keys, text-entry muting, the mouse mapped through the letterboxed picture, and the Controls screen.</summary>
public class PcInputTests
{
    private static readonly CanvasRect Picture = new(80, 0, 960, 544); // 2x picture centred in a 1120 x 544 window

    private static MouseState Mouse(int x, int y, bool left = false, bool right = false, int wheel = 0) =>
        new(x, y, wheel, left ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released,
            right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static InputFrame Poll(InputMapper mapper, MouseState mouse, params Keys[] keys) =>
        mapper.Poll(new KeyboardState(keys), new GamePadState(), true, (mouse, Picture));

    [Fact]
    public void DefaultsKeepTheRetainedPrimaryKeysAndAddPcAlternatives()
    {
        var b = KeyBindings.Default;
        Assert.Equal(Keys.Enter, b[InputAction.Confirm, 0]);
        Assert.Equal(Keys.Escape, b[InputAction.Back, 0]);
        Assert.Equal(Keys.Tab, b[InputAction.Menu, 0]);
        Assert.Equal(Keys.Space, b[InputAction.Switch, 0]);
        Assert.Equal(InputAction.Up | InputAction.Confirm, b.Held(new KeyboardState(Keys.W, Keys.Z)));
    }

    [Fact]
    public void RebindingMovesAKeyAndSwapsTheReplacedOne()
    {
        var b = KeyBindings.Default.With(InputAction.Confirm, 1, Keys.W); // W was Up's second key
        Assert.Equal(Keys.W, b[InputAction.Confirm, 1]);
        Assert.Equal(Keys.Z, b[InputAction.Up, 1]); // Up takes Confirm's old Z, so no action loses a key
        Assert.Same(b, b.With(InputAction.Up, 0, Keys.Escape)); // Escape is reserved
        Assert.Same(b, b.With(InputAction.Back, 0, Keys.X)); // Back keeps Escape in slot 0
        var round = KeyBindings.FromDocument(b.ToDocument());
        Assert.Equal(b.ToDocument(), round.ToDocument());
    }

    [Fact]
    public void SavedBindingsRejectDuplicatesAndUnknownKeys()
    {
        var document = KeyBindings.Default.ToDocument();
        document["Up"] = ["Up", "Enter"];
        Assert.Throws<InvalidDataException>(() => KeyBindings.FromDocument(document));
        document = KeyBindings.Default.ToDocument();
        document["Down"] = ["Down", "NotAKey"];
        Assert.Throws<InvalidDataException>(() => KeyBindings.FromDocument(document));
        Assert.Equal(KeyBindings.Default.ToDocument(), KeyBindings.FromDocument(null).ToDocument());
    }

    [Fact]
    public void TextEntryMutesTypingKeysOnly()
    {
        var mapper = new InputMapper { TextEntry = true };
        mapper.Poll(new KeyboardState(), new GamePadState(), true);
        var frame = mapper.Poll(new KeyboardState(Keys.Z, Keys.W, Keys.Back, Keys.Down), new GamePadState(), true);
        Assert.Equal(InputAction.Down, frame.Pressed);
    }

    [Fact]
    public void MouseMapsThroughTheLetterboxAndDropsClicksOutsideIt()
    {
        var mapper = new InputMapper();
        Poll(mapper, Mouse(0, 0));
        Poll(mapper, Mouse(0, 0));
        var inside = Poll(mapper, Mouse(80 + 200, 100, left: true));
        Assert.Equal(new CanvasPoint(100, 50), inside.Pointer.Position);
        Assert.True(inside.Pointer.LeftPressed && inside.Pointer.Moved);
        Poll(mapper, Mouse(10, 100));
        var letterbox = Poll(mapper, Mouse(10, 100, left: true));
        Assert.Null(letterbox.Pointer.Position);
        Assert.False(letterbox.Pointer.LeftPressed);
        var wheel = Poll(mapper, Mouse(300, 100, wheel: 60));
        Assert.Equal(0, wheel.Pointer.Wheel);
        Assert.Equal(1, Poll(mapper, Mouse(300, 100, wheel: 120)).Pointer.Wheel); // half notches add up
    }

    [Fact]
    public void PointerTargetsHoverFocusesClickConfirmsRightClickBacks()
    {
        int focus = 0;
        var hover = PointerTargets.Apply(new(InputAction.None, InputAction.None, false, new(new(5, 5), true, false, false, 0)), 2, focus, i => focus = i);
        Assert.Equal(2, focus);
        Assert.Equal(InputAction.None, hover.Pressed);
        var click = PointerTargets.Apply(new(InputAction.None, InputAction.None, false, new(new(5, 5), false, true, false, 0)), 3, focus, i => focus = i);
        Assert.Equal(3, focus);
        Assert.True(click.IsPressed(InputAction.Confirm));
        var back = PointerTargets.Apply(new(InputAction.None, InputAction.None, false, new(new(5, 5), false, false, true, -1)), null, focus, i => focus = i);
        Assert.True(back.IsPressed(InputAction.Back) && back.IsPressed(InputAction.Down) && (back.Held & InputAction.Down) != 0);
    }

    [Fact]
    public void ControlsCapturesTheNextKeyAndCancelsWithEscape()
    {
        var c = new ControlsEditor(KeyBindings.Default);
        var none = new KeyboardState();
        c.Handle(default, none);
        Assert.Equal(ControlsOutcome.None, c.Handle(new(InputAction.Confirm, InputAction.Confirm), new KeyboardState(Keys.Enter)));
        Assert.True(c.Capturing);
        Assert.Equal(ControlsOutcome.None, c.Handle(default, new KeyboardState(Keys.Enter))); // Enter still held: not a new key
        Assert.Equal(ControlsOutcome.Changed, c.Handle(default, new KeyboardState(Keys.I)));
        Assert.Equal(Keys.I, c.Bindings[InputAction.Up, 0]);
        c.Handle(new(InputAction.Confirm, InputAction.Confirm), none);
        Assert.Equal(ControlsOutcome.None, c.Handle(default, new KeyboardState(Keys.Escape)));
        Assert.False(c.Capturing);
        Assert.Equal(Keys.I, c.Bindings[InputAction.Up, 0]);
    }

    [Fact]
    public void ControlsMouseClickOnASlotStartsCaptureForIt()
    {
        var c = new ControlsEditor(KeyBindings.Default);
        float y = ControlsEditor.RowY(4) + 4; // Confirm
        c.Handle(new(InputAction.None, InputAction.None, false, new(new(ControlsEditor.SlotX1 + 10, y), true, true, false, 0)), new KeyboardState());
        Assert.Equal((4, 1), (c.Row, c.Slot));
        Assert.True(c.Capturing);
    }
}
