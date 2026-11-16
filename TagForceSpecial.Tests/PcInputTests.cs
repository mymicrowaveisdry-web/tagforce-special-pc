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

    private static PspInputFrame Poll(PspInputMapper mapper, MouseState mouse, params Keys[] keys) =>
        mapper.Poll(new KeyboardState(keys), new GamePadState(), true, (mouse, Picture));

    [Fact]
    public void DefaultsKeepTheRetainedPrimaryKeysAndAddPcAlternatives()
    {
        var b = KeyBindings.Default;
        Assert.Equal(Keys.Enter, b[PspAction.Cross, 0]);
        Assert.Equal(Keys.Escape, b[PspAction.Circle, 0]);
        Assert.Equal(Keys.Tab, b[PspAction.Triangle, 0]);
        Assert.Equal(Keys.Space, b[PspAction.Square, 0]);
        Assert.Equal(PspAction.Up | PspAction.Cross, b.Held(new KeyboardState(Keys.W, Keys.Z)));
    }

    [Fact]
    public void RebindingMovesAKeyAndSwapsTheReplacedOne()
    {
        var b = KeyBindings.Default.With(PspAction.Cross, 1, Keys.W); // W was Up's second key
        Assert.Equal(Keys.W, b[PspAction.Cross, 1]);
        Assert.Equal(Keys.Z, b[PspAction.Up, 1]); // Up takes Confirm's old Z, so no action loses a key
        Assert.Same(b, b.With(PspAction.Up, 0, Keys.Escape)); // Escape is reserved
        Assert.Same(b, b.With(PspAction.Circle, 0, Keys.X)); // Back keeps Escape in slot 0
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
        var mapper = new PspInputMapper { TextEntry = true };
        mapper.Poll(new KeyboardState(), new GamePadState(), true);
        var frame = mapper.Poll(new KeyboardState(Keys.Z, Keys.W, Keys.Back, Keys.Down), new GamePadState(), true);
        Assert.Equal(PspAction.Down, frame.Pressed);
    }

    [Fact]
    public void MouseMapsThroughTheLetterboxAndDropsClicksOutsideIt()
    {
        var mapper = new PspInputMapper();
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
        var hover = PointerTargets.Apply(new(PspAction.None, PspAction.None, false, new(new(5, 5), true, false, false, 0)), 2, focus, i => focus = i);
        Assert.Equal(2, focus);
        Assert.Equal(PspAction.None, hover.Pressed);
        var click = PointerTargets.Apply(new(PspAction.None, PspAction.None, false, new(new(5, 5), false, true, false, 0)), 3, focus, i => focus = i);
        Assert.Equal(3, focus);
        Assert.True(click.IsPressed(PspAction.Cross));
        var back = PointerTargets.Apply(new(PspAction.None, PspAction.None, false, new(new(5, 5), false, false, true, -1)), null, focus, i => focus = i);
        Assert.True(back.IsPressed(PspAction.Circle) && back.IsPressed(PspAction.Down) && (back.Held & PspAction.Down) != 0);
    }

    [Fact]
    public void ControlsCapturesTheNextKeyAndCancelsWithEscape()
    {
        var c = new ControlsEditor(KeyBindings.Default);
        var none = new KeyboardState();
        c.Handle(default, none);
        Assert.Equal(ControlsOutcome.None, c.Handle(new(PspAction.Cross, PspAction.Cross), new KeyboardState(Keys.Enter)));
        Assert.True(c.Capturing);
        Assert.Equal(ControlsOutcome.None, c.Handle(default, new KeyboardState(Keys.Enter))); // Enter still held: not a new key
        Assert.Equal(ControlsOutcome.Changed, c.Handle(default, new KeyboardState(Keys.I)));
        Assert.Equal(Keys.I, c.Bindings[PspAction.Up, 0]);
        c.Handle(new(PspAction.Cross, PspAction.Cross), none);
        Assert.Equal(ControlsOutcome.None, c.Handle(default, new KeyboardState(Keys.Escape)));
        Assert.False(c.Capturing);
        Assert.Equal(Keys.I, c.Bindings[PspAction.Up, 0]);
    }

    [Fact]
    public void ControlsMouseClickOnASlotStartsCaptureForIt()
    {
        var c = new ControlsEditor(KeyBindings.Default);
        float y = ControlsEditor.RowY(4) + 4; // Confirm
        c.Handle(new(PspAction.None, PspAction.None, false, new(new(ControlsEditor.SlotX1 + 10, y), true, true, false, 0)), new KeyboardState());
        Assert.Equal((4, 1), (c.Row, c.Slot));
        Assert.True(c.Capturing);
    }
}
