using System.Security.Cryptography;

namespace TagForceSpecial.Assets.Presentation;

public sealed record PgfGlyph(int Width, int Height, int Left, int Top, int AdvanceH26_6, byte[] Coverage4);

/// <summary>Bounded reader for revision 2 PSP PGF fonts. No font data ships with the application:
/// the file comes from the user's private storage. Format facts: PPSSPP v1.20.4 Core/Font/PGF.{h,cpp}.</summary>
public sealed class PgfFont
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private const int FlagHorizontalRows = 1, FlagVerticalRows = 2;
    private const int FlagDimensionIndex = 4, FlagXAdjustIndex = 8, FlagYAdjustIndex = 16, FlagAdvanceIndex = 32;
    private readonly byte[] glyphData;
    private readonly int[] charMap, pointers, advances;
    private readonly int firstGlyph, dimensionCount, xAdjustCount, yAdjustCount, advanceCount;
    private readonly Dictionary<int, PgfGlyph?> cache = [];
    public string FamilyName { get; }
    public string StyleName { get; }
    public string Sha256 { get; }

    public PgfFont(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Require(file.Length is >= 0x188 and <= MaximumBytes, "Unsupported PGF size.");
        Require(file[4] == 'P' && file[5] == 'G' && file[6] == 'F' && file[7] == '0', "Not a PGF font.");
        Require(Int32(file, 8) == 2, "Only revision 2 PGF fonts are supported.");
        int mapLength = Int32(file, 16), pointerLength = Int32(file, 20), mapBpe = Int32(file, 24), pointerBpe = Int32(file, 28);
        Require(mapLength is > 0 and <= 0x20000 && pointerLength is > 0 and <= 0x20000 &&
            mapBpe is > 0 and <= 32 && pointerBpe is > 0 and <= 32, "Invalid PGF table size.");
        FamilyName = Text(file, 53, 64); StyleName = Text(file, 117, 64);
        firstGlyph = BitConverter.ToUInt16(file, 182);
        int at = 212 + 24 + 16 + 4 + 2;
        dimensionCount = file[at]; xAdjustCount = file[at + 1]; yAdjustCount = file[at + 2]; advanceCount = file[at + 3];
        at += 4 + 102;
        int shadowLength = Int32(file, at), shadowBpe = Int32(file, at + 4);
        Require(shadowLength >= 0 && shadowBpe is >= 0 and <= 32, "Invalid PGF shadow table.");
        at += 28;
        at += (dimensionCount + xAdjustCount + yAdjustCount) * 8;
        Require(at + advanceCount * 8 <= file.Length, "Truncated PGF metric tables.");
        advances = new int[advanceCount];
        for (int i = 0; i < advanceCount; i++) advances[i] = Int32(file, at + i * 8);
        at += advanceCount * 8;
        at += checked((int)((((long)shadowLength * shadowBpe + 31) & ~31L) / 8));
        int mapBytes = checked((int)(((long)mapLength * mapBpe + 31) & ~31L) / 8);
        int pointerBytes = checked((int)(((long)pointerLength * pointerBpe + 31) & ~31L) / 8);
        Require(at >= 0 && at + mapBytes + pointerBytes < file.Length, "Truncated PGF tables.");
        charMap = new int[mapLength];
        for (int i = 0; i < mapLength; i++) charMap[i] = Bits(file, at, (long)i * mapBpe, mapBpe);
        at += mapBytes;
        pointers = new int[pointerLength];
        for (int i = 0; i < pointerLength; i++) pointers[i] = Bits(file, at, (long)i * pointerBpe, pointerBpe);
        at += pointerBytes;
        glyphData = file[at..];
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(file));
    }

    public PgfGlyph? Glyph(int codePoint)
    {
        if (cache.TryGetValue(codePoint, out var known)) return known;
        var glyph = Decode(codePoint);
        cache[codePoint] = glyph;
        return glyph;
    }

    private PgfGlyph? Decode(int codePoint)
    {
        int mapIndex = codePoint - firstGlyph;
        if (mapIndex < 0 || mapIndex >= charMap.Length) return null;
        int index = charMap[mapIndex];
        if (index >= pointers.Length) return null;
        long bit = pointers[index] * 32L + 14;
        int Take(int count) { int value = Bits(glyphData, 0, bit, count); bit += count; return value; }
        int width = Take(7), height = Take(7);
        int left = Take(7); if (left >= 64) left -= 128;
        int top = Take(7); if (top >= 64) top -= 128;
        int flags = Take(6);
        bit += 2 + 2 + 3 + 9; // Shadow flags and identifier are not rendered here.
        void SkipMetric(int flag) => bit += (flags & flag) != 0 ? 8 : 64;
        SkipMetric(FlagDimensionIndex); SkipMetric(FlagXAdjustIndex); SkipMetric(FlagYAdjustIndex);
        int advance = 0;
        if ((flags & FlagAdvanceIndex) != 0)
        {
            int advanceIndex = Take(8);
            if (advanceIndex < advanceCount) advance = advances[advanceIndex];
        }
        else { advance = Take(32); bit += 32; }
        if (width == 0 || height == 0) return new(0, 0, left, top, advance, []);
        int layout = flags & 3;
        if (layout is not (FlagHorizontalRows or FlagVerticalRows)) throw new InvalidDataException("Invalid PGF glyph layout.");
        long limit = glyphData.Length * 8L;
        byte[] decoded = new byte[checked(width * height)];
        int written = 0;
        while (written < decoded.Length)
        {
            if (bit + 8 > limit) throw new InvalidDataException("Truncated PGF glyph data.");
            int nibble = Take(4);
            if (nibble < 8)
            {
                int value = Take(4);
                for (int i = 0; i <= nibble && written < decoded.Length; i++) decoded[written++] = (byte)value;
            }
            else
            {
                for (int i = 0; i < 16 - nibble && written < decoded.Length; i++) decoded[written++] = (byte)Take(4);
            }
        }
        if (layout == FlagVerticalRows)
        {
            var rows = new byte[decoded.Length];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) rows[y * width + x] = decoded[x * height + y];
            decoded = rows;
        }
        return new(width, height, left, top, advance, decoded);
    }

    private static int Bits(byte[] data, int origin, long position, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            long at = position + i, index = origin + (at >> 3);
            if (index >= data.Length) throw new InvalidDataException("Truncated PGF bit field.");
            value |= ((data[index] >> (int)(at & 7)) & 1) << i;
        }
        return value;
    }
    private static int Int32(byte[] data, int at)
    {
        Require(at >= 0 && at + 4 <= data.Length, "Truncated PGF header.");
        return BitConverter.ToInt32(data, at);
    }
    private static string Text(byte[] data, int at, int length)
    {
        int end = Array.IndexOf(data, (byte)0, at, length);
        return System.Text.Encoding.Latin1.GetString(data, at, (end < 0 ? at + length : end) - at);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
