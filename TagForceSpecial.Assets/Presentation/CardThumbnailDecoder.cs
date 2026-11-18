using System.Buffers.Binary;

namespace TagForceSpecial.Assets.Presentation;

/// <summary>card/cardh_j.cip slots: a GIM holding one 128x64 PSP-order DXT1 image (card thumbnail with its name baked in).
/// Each slot stores 4,096 bytes while the GIM declares 4,224, so the last 128 pixel bytes are absent on disc. The original game
/// uploads exactly those 128 bytes as zeros (Phase 6.2 GE dump of card 7411), so this decoder zero-fills that measured tail and
/// nothing else, then decodes with the strict <see cref="GimTextureDecoder"/>. Any other shape is rejected.</summary>
public static class CardThumbnailDecoder
{
    public const string ConversionId = "cardh-dxt1-rgba-v1";
    public const string SourcePath = "PSP_GAME/USRDIR/card/cardh_j.cip";
    public const int Width = 128, Height = 64, MaximumMissingTail = 128;
    private const int ImageBlock = 48, ImageHeader = ImageBlock + 16;

    public static GimTexture Decode(ReadOnlySpan<byte> slot)
    {
        Require(slot.Length >= ImageHeader + 48 && slot[..16].SequenceEqual("MIG.00.1PSP\0\0\0\0\0"u8), "Card thumbnail slot is not a GIM.");
        int rootEnd = 16 + (int)BinaryPrimitives.ReadUInt32LittleEndian(slot[20..]);
        int format = BinaryPrimitives.ReadUInt16LittleEndian(slot[(ImageHeader + 4)..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(slot[(ImageHeader + 8)..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(slot[(ImageHeader + 10)..]);
        int pixelStart = ImageHeader + (int)BinaryPrimitives.ReadUInt32LittleEndian(slot[(ImageHeader + 28)..]);
        Require(BinaryPrimitives.ReadUInt16LittleEndian(slot[ImageBlock..]) == 4 && format == 8 && width == Width && height == Height,
            "Card thumbnail is not the observed 128x64 DXT1 image.");
        // Trailing zero padding after the declared root is allowed; a shortfall must lie wholly inside the pixel range.
        int missing = Math.Max(0, rootEnd - slot.Length);
        Require(missing <= MaximumMissingTail && pixelStart < slot.Length && rootEnd > pixelStart,
            "Card thumbnail storage differs from the observed slot layout.");
        byte[] complete = new byte[rootEnd];
        slot[..Math.Min(slot.Length, rootEnd)].CopyTo(complete);
        var texture = new GimTextureDecoder().Decode(complete);
        Require(texture.Format == 8 && texture.Width == Width && texture.Height == Height, "Decoded card thumbnail has an unexpected shape.");
        if (missing == 0) return texture;
        var diagnostics = texture.Diagnostics.Append($"{missing} missing tail bytes zero-filled, as the original uploads them.").ToArray();
        return texture with { Diagnostics = Array.AsReadOnly(diagnostics) };
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
