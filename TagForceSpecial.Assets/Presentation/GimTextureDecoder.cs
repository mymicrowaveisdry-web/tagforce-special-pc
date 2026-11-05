using System.Buffers.Binary;

namespace TagForceSpecial.Assets.Presentation;

public sealed record GimTexture(int Width, int Height, int Format, bool Swizzled,
    byte[] Rgba, IReadOnlyList<string> Diagnostics);

/// <summary>Bounded PSP little-endian GIM decoder. Returns straight RGBA; never forces alpha.
/// Supports one picture/frame/image level. Multiple CLUTs require explicit palette selection.
/// Does not infer sprite rectangles.</summary>
public sealed class GimTextureDecoder
{
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    public const int MaximumDimension = 4096;
    private sealed record Plane(int Format, int Order, int Width, int Height, int Pitch, int Rows,
        int Start, int Length);

    public GimTexture Decode(Stream source, int? paletteLevel = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Readable seekable stream required.", nameof(source));
        long original = source.Position;
        try
        {
            Require(source.Length <= MaximumFileBytes, "GIM exceeds the file-size limit.");
            source.Position = 0;
            byte[] bytes = new byte[checked((int)source.Length)];
            source.ReadExactly(bytes);
            return Decode(bytes, paletteLevel);
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Truncated GIM.", error); }
        finally { source.Position = original; }
    }

    public GimTexture Decode(ReadOnlySpan<byte> bytes, int? paletteLevel = null)
    {
        if (paletteLevel < 0) throw new ArgumentOutOfRangeException(nameof(paletteLevel));
        Require(bytes.Length <= MaximumFileBytes && bytes.Length >= 32, "Invalid GIM length.");
        Require(bytes[..16].SequenceEqual("MIG.00.1PSP\0\0\0\0\0"u8), "Unsupported GIM signature/endian/version.");
        // Use a bounded owned buffer so traversal cannot outlive or mutate caller data.
        byte[] data = bytes.ToArray();
        int U16(int at) { Bounds(at, 2, data.Length); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)); }
        int U32(int at)
        {
            Bounds(at, 4, data.Length);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
            Require(value <= int.MaxValue, "Unsupported unsigned offset/size.");
            return (int)value;
        }
        Plane? image = null, palette = null;
        var diagnostics = new List<string>();
        int pictures = 0, blocks = 0;
        Plane ReadPlane(int at, int end, bool isPalette)
        {
            Bounds(at, 52, end);
            Require(U16(at) == 48, "Unsupported image header size.");
            int format = U16(at + 4), order = U16(at + 6), width = U16(at + 8), height = U16(at + 10);
            Require(width > 0 && height > 0 && width <= MaximumDimension && height <= MaximumDimension,
                "Invalid or oversized texture dimensions.");
            Require(order is 0 or 1, "Unsupported pixel order.");
            int bits = Bits(format);
            Require(U16(at + 12) == bits, "Pixel format/bit-depth mismatch.");
            int pitchAlign = U16(at + 14), heightAlign = U16(at + 16);
            Require(IsPowerOfTwo(pitchAlign) && pitchAlign <= 256 && IsPowerOfTwo(heightAlign) && heightAlign <= 256,
                "Unsupported texture alignment.");
            int levels = U16(at + 42);
            Require(U16(at + 46) == 1 && levels >= 1 && levels <= 256,
                "Invalid level count or multiple frames are unsupported.");
            Require(isPalette || levels == 1, "Multiple image mip levels are unsupported.");
            Require(!isPalette || levels == 1 || (U16(at + 40) == 2 && paletteLevel.HasValue),
                "Multiple palettes require an explicit paletteLevel selection.");
            int selected = isPalette ? paletteLevel ?? 0 : 0;
            Require(selected < levels, "Selected palette level is out of range.");
            int table = U32(at + 24), start = U32(at + 28), stop = U32(at + 32);
            Require(table >= 48 && table <= start - levels * 4, "Invalid frame-offset table.");
            Bounds(at, start, end);
            Require(U32(at + table) == start, "Unsupported first-frame offset.");
            Require(stop >= start, "Reversed image data range.");
            Bounds(at, stop, end);
            int pitch = Align((width * bits + 7) / 8, pitchAlign), rows = Align(height, heightAlign);
            if (format == 8)
            {
                Require(order == 0, "Swizzled DXT storage is unsupported.");
                pitch = ((width + 3) / 4) * 8;
                rows = (height + 3) / 4;
            }
            else if (order == 1)
                Require(pitch % 16 == 0 && rows % 8 == 0, "Swizzle requires complete 16-byte by 8-row tiles.");
            for (int level = 0; level < levels; level++)
            {
                int levelStart = U32(at + table + level * 4);
                int levelEnd = level + 1 < levels ? U32(at + table + (level + 1) * 4) : stop;
                Require(levelStart >= start && levelEnd <= stop && levelEnd >= levelStart &&
                    (long)pitch * rows <= levelEnd - levelStart, "Truncated pixel storage; no padding is synthesized.");
            }
            int chosenStart = U32(at + table + selected * 4);
            int chosenEnd = selected + 1 < levels ? U32(at + table + (selected + 1) * 4) : stop;
            if (isPalette && levels > 1)
                diagnostics.Add($"Palette level {selected} of {levels} explicitly selected; runtime palette choice is unverified.");
            return new Plane(format, order, width, height, pitch, rows, at + chosenStart, chosenEnd - chosenStart);
        }
        int Walk(int at, int boundary, int depth)
        {
            Require(depth <= 8 && ++blocks <= 4096, "GIM block-tree limit exceeded.");
            Bounds(at, 16, boundary);
            int type = U16(at), size = U32(at + 4), next = U32(at + 8), payload = U32(at + 12);
            Require(size >= 16 && payload >= 16 && payload <= size, "Invalid block header.");
            Bounds(at, size, boundary);
            int end = at + size;
            if (type is 2 or 3)
            {
                Require(next == payload && payload == 16, "Unsupported container traversal.");
                if (type == 2) Require(depth == 0, "Nested GIM root is unsupported.");
                if (type == 3) Require(depth == 1 && ++pictures == 1, "Multiple/nested pictures are unsupported.");
                int child = at + payload;
                while (child < end) child = Walk(child, end, depth + 1);
            }
            else
            {
                Require(next == size, "Invalid leaf traversal offset.");
                switch (type)
                {
                    case 4:
                        Require(depth == 2 && image is null, "Multiple images or image outside picture.");
                        image = ReadPlane(at + payload, end, false); break;
                    case 5:
                        Require(depth == 2 && palette is null, "Multiple palettes or palette outside picture.");
                        palette = ReadPlane(at + payload, end, true); break;
                    case 255: break; // Bounded authoring metadata, never interpreted as pixels.
                    default: throw new InvalidDataException($"Unsupported GIM block type {type}.");
                }
            }
            return end;
        }
        Require(U16(16) == 2, "Missing root block.");
        int rootEnd = Walk(16, data.Length, 0);
        Require(pictures == 1 && image is not null, "Missing picture/image.");
        if (rootEnd != data.Length)
            diagnostics.Add($"{data.Length - rootEnd} trailing bytes outside GIM root were not decoded; resource boundaries remain unverified.");
        Plane img = image!;
        byte[] pixels = Linear(data, img);
        byte[]? clut = null;
        int colors = 0;
        if (img.Format is >= 4 and <= 7)
        {
            Require(palette is not null && palette.Format <= 3, "Indexed image requires a direct-color palette.");
            Plane pal = palette!;
            colors = pal.Width * pal.Height;
            byte[] stored = Linear(data, pal);
            clut = new byte[colors * 4];
            for (int y = 0; y < pal.Height; y++)
                for (int x = 0; x < pal.Width; x++)
                    Direct(stored.AsSpan(y * pal.Pitch + x * Bits(pal.Format) / 8), pal.Format,
                        clut.AsSpan((y * pal.Width + x) * 4, 4));
        }
        byte[] rgba = new byte[img.Width * img.Height * 4];
        if (img.Format == 8) DecodeDxt1(pixels, img, rgba);
        else
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    Span<byte> color = rgba.AsSpan((y * img.Width + x) * 4, 4);
                    int at = y * img.Pitch + x * Bits(img.Format) / 8;
                    if (clut is null) Direct(pixels.AsSpan(at), img.Format, color);
                    else
                    {
                        uint index = img.Format switch
                        {
                            4 => (uint)((pixels[at] >> ((x & 1) * 4)) & 15),
                            5 => pixels[at],
                            6 => BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(at)),
                            7 => BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan(at)),
                            _ => throw new InvalidDataException("Unsupported index format.")
                        };
                        Require(index < colors, "Palette index exceeds available colors.");
                        clut.AsSpan((int)index * 4, 4).CopyTo(color);
                    }
                }
        return new GimTexture(img.Width, img.Height, img.Format, img.Order == 1, rgba, diagnostics.AsReadOnly());
    }

    private static byte[] Linear(byte[] file, Plane plane)
    {
        int length = plane.Pitch * plane.Rows;
        byte[] result = new byte[length];
        if (plane.Order == 0) file.AsSpan(plane.Start, length).CopyTo(result);
        else
            for (int y = 0; y < plane.Rows; y++)
                for (int x = 0; x < plane.Pitch; x++)
                {
                    int tile = (y / 8) * (plane.Pitch / 16) + x / 16;
                    result[y * plane.Pitch + x] = file[plane.Start + tile * 128 + (y % 8) * 16 + x % 16];
                }
        return result;
    }

    private static void Direct(ReadOnlySpan<byte> data, int format, Span<byte> rgba)
    {
        if (format == 3) { data[..4].CopyTo(rgba); return; }
        int word = BinaryPrimitives.ReadUInt16LittleEndian(data);
        switch (format)
        {
            case 0: rgba[0] = Expand5(word & 31); rgba[1] = Expand6((word >> 5) & 63);
                rgba[2] = Expand5((word >> 11) & 31); rgba[3] = 255; break;
            case 1: rgba[0] = Expand5(word & 31); rgba[1] = Expand5((word >> 5) & 31);
                rgba[2] = Expand5((word >> 10) & 31); rgba[3] = (byte)((word >> 15) * 255); break;
            case 2: for (int i = 0; i < 4; i++) rgba[i] = (byte)(((word >> (i * 4)) & 15) * 17); break;
            default: throw new InvalidDataException("Unsupported direct-color format.");
        }
    }

    private static void DecodeDxt1(byte[] pixels, Plane img, byte[] rgba)
    {
        Span<byte> colors = stackalloc byte[16];
        for (int by = 0; by < (img.Height + 3) / 4; by++)
            for (int bx = 0; bx < (img.Width + 3) / 4; bx++)
            {
                ReadOnlySpan<byte> block = pixels.AsSpan(by * img.Pitch + bx * 8, 8);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block);
                int a = BinaryPrimitives.ReadUInt16LittleEndian(block[4..]), b = BinaryPrimitives.ReadUInt16LittleEndian(block[6..]);
                for (int c = 0; c < 2; c++)
                {
                    int word = c == 0 ? a : b;
                    // PPSSPP 1.20.4's DXT path uses left-shifted RGB565 components,
                    // unlike direct-color PSP textures. Keep these policies separate.
                    colors[c * 4] = (byte)(((word >> 11) & 31) * 8);
                    colors[c * 4 + 1] = (byte)(((word >> 5) & 63) * 4);
                    colors[c * 4 + 2] = (byte)((word & 31) * 8); colors[c * 4 + 3] = 255;
                }
                for (int c = 0; c < 3; c++)
                {
                    colors[8 + c] = (byte)(a > b ? (2 * colors[c] + colors[4 + c]) / 3 : (colors[c] + colors[4 + c]) / 2);
                    colors[12 + c] = (byte)(a > b ? (colors[c] + 2 * colors[4 + c]) / 3 : 0);
                }
                colors[11] = 255; colors[15] = (byte)(a > b ? 255 : 0);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int dx = bx * 4 + x, dy = by * 4 + y;
                        if (dx < img.Width && dy < img.Height)
                            colors.Slice((int)((indices >> (2 * (y * 4 + x))) & 3) * 4, 4)
                                .CopyTo(rgba.AsSpan((dy * img.Width + dx) * 4, 4));
                    }
            }
    }

    private static int Bits(int format) => format switch
    {
        0 or 1 or 2 or 6 => 16, 3 or 7 => 32, 4 or 8 => 4, 5 => 8,
        _ => throw new InvalidDataException($"Unsupported GIM pixel format {format}.")
    };
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));
    private static int Align(int value, int alignment) => (value + alignment - 1) & -alignment;
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static void Bounds(int at, int size, int boundary) =>
        Require(at >= 0 && size >= 0 && at <= boundary && size <= boundary - at, "GIM range exceeds containing block/file.");
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
