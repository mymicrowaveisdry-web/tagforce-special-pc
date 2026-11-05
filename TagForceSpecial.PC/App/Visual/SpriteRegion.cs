using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Visual;

public enum SpriteSizing { Native, PreserveAspect, MeasuredStretch }
public sealed record NineSliceCaps(int Left, int Top, int Right, int Bottom, string EvidenceId);

/// <summary>An explicit atlas rectangle with a declared sizing policy.</summary>
public sealed record SpriteRegion(Rectangle Source, SpriteSizing Sizing, string? StretchEvidenceId = null)
{
    public void Validate(int textureWidth, int textureHeight)
    {
        if (Source.X < 0 || Source.Y < 0 || Source.Width <= 0 || Source.Height <= 0 ||
            (long)Source.X + Source.Width > textureWidth || (long)Source.Y + Source.Height > textureHeight)
            throw new InvalidDataException("Sprite region exceeds the validated texture.");
        if (Sizing == SpriteSizing.MeasuredStretch && string.IsNullOrWhiteSpace(StretchEvidenceId))
            throw new InvalidDataException("Stretch requires measured layout evidence.");
    }
    public Rectangle Fit(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || Source.Width <= 0 || Source.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if (Sizing == SpriteSizing.MeasuredStretch)
        {
            if (string.IsNullOrWhiteSpace(StretchEvidenceId)) throw new InvalidDataException("Unmeasured stretch.");
            return bounds;
        }
        double scale = Sizing == SpriteSizing.Native ? 1 : Math.Min((double)bounds.Width / Source.Width, (double)bounds.Height / Source.Height);
        int width = Math.Max(1, (int)Math.Floor(Source.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(Source.Height * scale));
        if (width > bounds.Width || height > bounds.Height) throw new InvalidDataException("Native sprite does not fit.");
        return new(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }
    public void Draw(SpriteBatch batch, Texture2D texture, Rectangle bounds, Color tint)
    {
        Validate(texture.Width, texture.Height);
        batch.Draw(texture, Fit(bounds), Source, tint);
    }
    public void DrawNineSlice(SpriteBatch batch, Texture2D texture, Rectangle bounds, NineSliceCaps caps, Color tint)
    {
        Validate(texture.Width, texture.Height);
        if (string.IsNullOrWhiteSpace(caps.EvidenceId) || caps.Left < 0 || caps.Top < 0 || caps.Right < 0 || caps.Bottom < 0 ||
            caps.Left + caps.Right >= Source.Width || caps.Top + caps.Bottom >= Source.Height ||
            bounds.Width < caps.Left + caps.Right || bounds.Height < caps.Top + caps.Bottom)
            throw new InvalidDataException("Nine-slice caps/bounds require valid measurements.");
        int[] sx = [Source.X, Source.X + caps.Left, Source.Right - caps.Right, Source.Right];
        int[] sy = [Source.Y, Source.Y + caps.Top, Source.Bottom - caps.Bottom, Source.Bottom];
        int[] dx = [bounds.X, bounds.X + caps.Left, bounds.Right - caps.Right, bounds.Right];
        int[] dy = [bounds.Y, bounds.Y + caps.Top, bounds.Bottom - caps.Bottom, bounds.Bottom];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                if (sx[x + 1] > sx[x] && sy[y + 1] > sy[y] && dx[x + 1] > dx[x] && dy[y + 1] > dy[y])
                    batch.Draw(texture, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]),
                        new Rectangle(sx[x], sy[y], sx[x + 1] - sx[x], sy[y + 1] - sy[y]), tint);
    }
}
