using Godot;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.Host;

/// <summary>The game's 480 × 272 picture, as the MonoGame host drew it: screens render into a transparent canvas target, which is shown
/// over black with point sampling at <see cref="PspViewport.Fit"/> (integer scaling when preferred, letterboxed). Mouse positions map
/// through the same rectangle; outside the picture they are dropped.</summary>
internal sealed partial class PspCanvas : Control
{
    private readonly SubViewport canvas = new()
    {
        Size = new Vector2I(PspViewport.Width, PspViewport.Height), TransparentBg = true, Disable3D = true,
        CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Always
    };
    private readonly TextureRect picture = new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore, Material = CanvasMaterials.Premultiplied
    };
    private PointerInput pointer;
    private Vector2? lastMouse;

    public bool IntegerScaling { get; set; }
    /// <summary>Where the picture is in the window, in window pixels.</summary>
    public CanvasRect Picture { get; private set; }

    public PspCanvas()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        AddChild(canvas);
        picture.Texture = canvas.GetTexture();
        AddChild(picture);
    }

    /// <summary>Adds a screen to the 480 × 272 canvas.</summary>
    public void AddScreen(Node screen) => canvas.AddChild(screen);

    /// <summary>The canvas exactly as rendered (premultiplied alpha), for frame captures at native size.</summary>
    public Image Capture() => canvas.GetTexture().GetImage();

    public override void _Process(double delta)
    {
        var size = GetViewportRect().Size;
        Picture = PspViewport.Fit((int)size.X, (int)size.Y, IntegerScaling);
        picture.Position = new Vector2(Picture.X, Picture.Y);
        picture.Size = new Vector2(Picture.Width, Picture.Height);
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                lastMouse = motion.Position;
                pointer = pointer with { Position = ToCanvas(motion.Position), Moved = ToCanvas(motion.Position) is not null };
                break;
            case InputEventMouseButton { Pressed: true } button:
                var at = ToCanvas(button.Position);
                lastMouse = button.Position;
                pointer = button.ButtonIndex switch
                {
                    MouseButton.Left when at is not null => pointer with { Position = at, LeftPressed = true },
                    MouseButton.Right when at is not null => pointer with { Position = at, RightPressed = true },
                    MouseButton.WheelUp when at is not null => pointer with { Position = at, Wheel = pointer.Wheel + 1 },
                    MouseButton.WheelDown when at is not null => pointer with { Position = at, Wheel = pointer.Wheel - 1 },
                    _ => pointer
                };
                break;
        }
    }

    /// <summary>The mouse since the last call: position (null outside the picture), movement, clicks and wheel notches.</summary>
    public PointerInput TakePointer()
    {
        var taken = pointer with { Position = lastMouse is { } m ? ToCanvas(m) : null };
        pointer = default;
        return taken;
    }

    private CanvasPoint? ToCanvas(Vector2 window) => PspViewport.ToCanvas((int)window.X, (int)window.Y, Picture);
}
