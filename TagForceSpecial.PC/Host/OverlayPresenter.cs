using Godot;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.Host;

/// <summary>The PC's system-style title-flow screens (name keyboard, save prompt, save completed): an opaque fill and pixel-font lines.</summary>
internal sealed partial class OverlayPresenter : Control
{
    private IReadOnlyList<(string Text, int X, int Y, (int R, int G, int B) Color)> lines = [];

    public OverlayPresenter()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(480, 272);
    }

    /// <summary>Shows <paramref name="next"/>; null hides the overlay.</summary>
    public void Show(IReadOnlyList<(string Text, int X, int Y, (int R, int G, int B) Color)>? next)
    {
        Visible = next is not null;
        lines = next ?? [];
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 480, 272), Rgb(TitleFlowOverlay.Fill));
        foreach (var (text, x, y, color) in lines)
            foreach (var (cx, cy) in PixelFont.Cells(text, x, y))
                DrawRect(new Rect2(cx, cy, 1, 1), Rgb(color));
    }

    private static Color Rgb((int R, int G, int B) c) => Color.Color8((byte)c.R, (byte)c.G, (byte)c.B);
}
