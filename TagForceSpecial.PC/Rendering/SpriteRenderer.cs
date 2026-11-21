using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.Rendering;

/// <summary>Sprite depth bands; <see cref="Panel"/> and <see cref="PanelHighlight"/> sit under text so window labels stay readable.</summary>
public enum SceneLayer { Background = 0, Field = 10, Cards = 30, Panel = 40, PanelHighlight = 50, Text = 60, Cursor = 80, Modal = 90 }
public readonly record struct AtlasRegion(Texture2D Texture, Rectangle Source);

public sealed class SpriteRenderer : IDisposable
{
    private readonly SpriteBatch batch;
    private readonly Texture2D pixel;
    private readonly PixelFont font = new();
    private readonly GameThreadGuard gameThread = new();
    public SpriteRenderer(GraphicsDevice device)
    {
        batch = new(device);
        pixel = new(device, 1, 1);
        pixel.SetData(new[] { Color.White });
    }
    public void Begin() => batch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);
    public void End() => batch.End();
    public void Fill(Rectangle rect, Color color, SceneLayer layer = SceneLayer.Field) =>
        batch.Draw(pixel, rect, null, color, 0, Vector2.Zero, SpriteEffects.None, (int)layer / 100f);
    /// <summary>A full-screen pre-rendered layer beneath every other sprite.</summary>
    public void Layer(Texture2D layer) => batch.Draw(layer, new Rectangle(0, 0, 480, 272), null, Color.White, 0, Vector2.Zero, SpriteEffects.None, 0f);
    public void Sprite(AtlasRegion sprite, Rectangle target) => Sprite(sprite, target, SceneLayer.Cards);
    internal void Sprite(AtlasRegion sprite, Rectangle target, SceneLayer layer) => batch.Draw(sprite.Texture, target, sprite.Source,
        Color.White, 0, Vector2.Zero, SpriteEffects.None, (int)layer / 100f);
    public void Border(Rectangle rect, Color color, SceneLayer layer = SceneLayer.Cursor)
    {
        Fill(new(rect.X, rect.Y, rect.Width, 1), color, layer);
        Fill(new(rect.X, rect.Bottom - 1, rect.Width, 1), color, layer);
        Fill(new(rect.X, rect.Y, 1, rect.Height), color, layer);
        Fill(new(rect.Right - 1, rect.Y, 1, rect.Height), color, layer);
    }
    public void Text(string text, int x, int y, Color? color = null, int scale = 1) =>
        font.Draw(batch, pixel, text, x, y, color ?? Color.White, scale, .6f);
    public void Wrapped(string text, int x, int y, int columns, int lines, Color? color = null) =>
        Wrapped(text, x, y, columns, lines, color, false);

    internal void Wrapped(string text, int x, int y, int columns, int lines, Color? color, bool ellipsis)
    {
        using var rows = WrapLines(text, columns).GetEnumerator();
        for (int row = 0; row < lines && rows.MoveNext(); row++)
        {
            string line = rows.Current;
            if (ellipsis && row == lines - 1 && rows.MoveNext())
            {
                string marker = new('.', Math.Min(3, columns));
                line = line[..Math.Min(line.Length, columns - marker.Length)].TrimEnd() + marker;
            }
            Text(line, x, y + row * 9, color);
        }
    }

    private static IEnumerable<string> WrapLines(string text, int columns)
    {
        var words = text.Replace('\n', ' ').Replace('\r', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = "";
        foreach (string word in words)
        {
            string pending = word;
            if (line.Length > 0 && line.Length + 1 + pending.Length > columns)
            {
                yield return line;
                line = "";
            }
            while (pending.Length > columns)
            {
                yield return pending[..columns];
                pending = pending[columns..];
            }
            line = line.Length == 0 ? pending : line + " " + pending;
        }
        yield return line;
    }
    public void Dispose() { gameThread.Check(); pixel.Dispose(); batch.Dispose(); }
}
