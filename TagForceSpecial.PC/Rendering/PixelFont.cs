using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.Rendering;

/// <summary>Draws the shared <see cref="App.Visual.PixelFont"/> cells with a SpriteBatch.</summary>
internal sealed class PixelFont
{
    public void Draw(SpriteBatch batch, Texture2D pixel, string text, int x, int y, Color color, int scale, float layer)
    {
        foreach (var (cx, cy) in App.Visual.PixelFont.Cells(text, x, y, scale))
            batch.Draw(pixel, new Rectangle(cx, cy, scale, scale), null, color, 0, Vector2.Zero, SpriteEffects.None, layer);
    }
}
