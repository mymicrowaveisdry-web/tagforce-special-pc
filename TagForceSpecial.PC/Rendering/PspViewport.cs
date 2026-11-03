namespace TagForceSpecial.PC.Rendering;

public readonly record struct CanvasPoint(float X, float Y);
public readonly record struct CanvasRect(int X, int Y, int Width, int Height);

public static class PspViewport
{
    public const int Width = 480;
    public const int Height = 272;

    public static CanvasRect Fit(int width, int height, bool integerScale = false)
    {
        if (width <= 0 || height <= 0) return default;
        double scale = Math.Min((double)width / Width, (double)height / Height);
        if (integerScale && scale >= 1) scale = Math.Floor(scale);
        int w = Math.Max(1, (int)Math.Floor(Width * scale));
        int h = Math.Max(1, (int)Math.Floor(Height * scale));
        return new((width - w) / 2, (height - h) / 2, w, h);
    }

    public static CanvasPoint? ToCanvas(int x, int y, CanvasRect target) =>
        target.Width <= 0 || target.Height <= 0 || x < target.X || y < target.Y ||
        x >= target.X + target.Width || y >= target.Y + target.Height ? null :
            new((x - target.X) * (float)Width / target.Width, (y - target.Y) * (float)Height / target.Height);
}
