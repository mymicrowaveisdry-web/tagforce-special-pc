using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Copies of a card in the PC's current deck (Main including Extra, and Side) and in the Trunk (owned minus those).</summary>
public readonly record struct CardAlbumCounts(int Main, int Side, int Trunk);

public enum CardAlbumDetailTab { Description, Genre, Packs }

/// <summary>The card detail opened from the album (measured on the same build, EBOOT 0x18e74-0x19058): it appears at once; Left/Right
/// cycle CARD DESCRIPTION, CARD GENRE and Pack List (wrapping) and reset the scroll; held Up/Down scroll 2 px per frame within
/// 0..content-192 (Down wins when both are held); Back shows one blank frame and returns to the album with the cursor where it was.
/// L/R, Triangle and Square do nothing.</summary>
public sealed class CardAlbumDetail
{
    private readonly Func<CardAlbumDetailTab, float> contentHeight;
    private readonly float visible, step;
    public uint Card { get; }
    public CardAlbumCounts Counts { get; }
    public CardAlbumDetailTab Tab { get; private set; }
    /// <summary>Scroll in pixels and its limit for the current tab.</summary>
    public float Scroll { get; private set; }
    public float MaxScroll { get; private set; }
    /// <summary>Measured: Back draws one blank frame before the album returns.</summary>
    public bool Closing { get; private set; }

    /// <param name="contentHeight">Content height of a tab in pixels (description lines, genre rows, pack rows).</param>
    public CardAlbumDetail(uint card, CardAlbumCounts counts, Func<CardAlbumDetailTab, float> contentHeight, float visible = 192, float pixelsPerFrame = 2)
    {
        Card = card; Counts = counts; this.contentHeight = contentHeight; this.visible = visible; step = pixelsPerFrame;
        Reset();
    }

    private void Reset() { Scroll = 0; MaxScroll = Math.Max(0, contentHeight(Tab) - visible); }

    /// <summary>Handles one frame of input; true when anything drawn changed.</summary>
    public bool Handle(InputFrame input)
    {
        if (Closing) return false;
        if (input.IsPressed(InputAction.Back)) { Closing = true; return true; }
        if (input.IsPressed(InputAction.Left) || input.IsPressed(InputAction.Right))
        {
            int delta = input.IsPressed(InputAction.Right) ? 1 : 2;
            Tab = (CardAlbumDetailTab)(((int)Tab + delta) % 3);
            Reset();
            return true;
        }
        float move = (input.Held & InputAction.Down) != 0 ? step : (input.Held & InputAction.Up) != 0 ? -step : 0;
        float next = Math.Clamp(Scroll + move, 0, MaxScroll);
        if (next == Scroll) return false;
        Scroll = next;
        return true;
    }

    /// <summary>Thumb offset and length on the scroll track (EBOOT 0x198a8: offset 178*S/(M+192) truncated, length 178*192/(M+192)).</summary>
    public (float Offset, float Length) Thumb(float track)
    {
        float content = MaxScroll + visible;
        return (MathF.Floor(track * Scroll / content), track * visible / content);
    }
}
