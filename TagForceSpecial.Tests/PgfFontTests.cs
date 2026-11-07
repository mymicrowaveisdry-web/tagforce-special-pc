using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

/// <summary>Font tests on generated font bytes.</summary>
public class PgfFontTests
{
    private sealed class BitWriter
    {
        private readonly List<byte> bytes = [];
        private int bit;
        public void Write(int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (bit % 8 == 0) bytes.Add(0);
                if (((value >> i) & 1) != 0) bytes[^1] |= (byte)(1 << (bit % 8));
                bit++;
            }
        }
        public byte[] ToArray() => bytes.ToArray();
    }

    /// <summary>One glyph 'A' (2x2, horizontal rows) with an indexed advance of 640/64.</summary>
    private static byte[] Font(int width = 2, int layout = 1, int revision = 2, bool truncate = false)
    {
        var glyph = new BitWriter();
        glyph.Write(0, 14); // Size is skipped.
        glyph.Write(width, 7); glyph.Write(2, 7); glyph.Write(1, 7); glyph.Write(5, 7);
        glyph.Write(layout | 4 | 8 | 16 | 32, 6);
        glyph.Write(0, 2 + 2 + 3); glyph.Write(0, 9);
        glyph.Write(0, 8); glyph.Write(0, 8); glyph.Write(0, 8); // Dimension, X and Y indices.
        glyph.Write(0, 8); // Advance index.
        // RLE literal run of four coverage values.
        glyph.Write(16 - 4, 4);
        foreach (int value in new[] { 1, 2, 3, 15 }) glyph.Write(value, 4);
        glyph.Write(0, 32);
        byte[] glyphBytes = glyph.ToArray();

        byte[] header = new byte[364 + 28];
        header[4] = (byte)'P'; header[5] = (byte)'G'; header[6] = (byte)'F'; header[7] = (byte)'0';
        void Put(int at, int value) => BitConverter.GetBytes(value).CopyTo(header, at);
        Put(8, revision);
        Put(16, 1); Put(20, 1); Put(24, 8); Put(28, 8); // One char map and pointer entry, 8 bits each.
        BitConverter.GetBytes((ushort)'A').CopyTo(header, 182);
        header[258] = 0; header[259] = 0; header[260] = 0; header[261] = 1; // One advance row.
        var output = new List<byte>(header);
        output.AddRange(BitConverter.GetBytes(640)); output.AddRange(BitConverter.GetBytes(0));
        output.AddRange(new byte[] { 0, 0, 0, 0 }); // Char map: code 'A' -> glyph 0.
        output.AddRange(new byte[] { 0, 0, 0, 0 }); // Pointer: glyph data offset 0.
        output.AddRange(glyphBytes);
        if (truncate) output.RemoveRange(output.Count - 8, 8);
        return output.ToArray();
    }

    [Fact]
    public void DecodesGeometryAdvanceAndCoverage()
    {
        var glyph = new PgfFont(Font()).Glyph('A');
        Assert.NotNull(glyph);
        Assert.Equal((2, 2, 1, 5, 640), (glyph!.Width, glyph.Height, glyph.Left, glyph.Top, glyph.AdvanceH26_6));
        Assert.Equal(new byte[] { 1, 2, 3, 15 }, glyph.Coverage4);
    }

    [Fact]
    public void VerticalRowsAreTransposedToRowOrder()
    {
        var glyph = new PgfFont(Font(layout: 2)).Glyph('A');
        Assert.Equal(new byte[] { 1, 3, 2, 15 }, glyph!.Coverage4);
    }

    [Fact]
    public void CharactersOutsideTheMapAreAbsent()
    {
        var font = new PgfFont(Font());
        Assert.Null(font.Glyph('@'));
        Assert.Null(font.Glyph('B'));
    }

    [Fact]
    public void RejectsWrongMagicRevisionAndSize()
    {
        byte[] bad = Font(); bad[4] = 0;
        Assert.Throws<InvalidDataException>(() => new PgfFont(bad));
        Assert.Throws<InvalidDataException>(() => new PgfFont(Font(revision: 3)));
        Assert.Throws<InvalidDataException>(() => new PgfFont(new byte[16]));
    }

    [Fact]
    public void TruncatedGlyphDataFailsInsteadOfReadingPastTheFile()
    {
        var font = new PgfFont(Font(width: 100, truncate: true));
        Assert.Throws<InvalidDataException>(() => font.Glyph('A'));
    }
}
