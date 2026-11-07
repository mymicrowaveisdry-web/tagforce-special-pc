using System.Buffers.Binary;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class CpjArtworkDecoderTests
{
    [Fact]
    public void NativeReferenceRejectsUnknownCardsWithoutFallback()
    {
        Assert.Throws<KeyNotFoundException>(() => CpjArtworkReferenceProfiles.Read(3459,
            CpjArtworkReferenceProfiles.ArchiveSha256, CpjArtworkReferenceProfiles.SlotSha256,
            CpjArtworkReferenceProfiles.JpegSha256, null));
    }

    [Fact]
    public void NativeReferenceRejectsChangedSourceIdentity()
    {
        Assert.Throws<KeyNotFoundException>(() => CpjArtworkReferenceProfiles.Read(CpjArtworkReferenceProfiles.CardId,
            CpjArtworkReferenceProfiles.ArchiveSha256, new string('0', 64),
            CpjArtworkReferenceProfiles.JpegSha256, null));
    }

    [Fact]
    public void NativeReferenceRejectsMissingAndTamperedPixelFiles()
    {
        using var fixture = new FoundationFixture();
        byte[] Read() => CpjArtworkReferenceProfiles.Read(CpjArtworkReferenceProfiles.CardId,
            CpjArtworkReferenceProfiles.ArchiveSha256, CpjArtworkReferenceProfiles.SlotSha256,
            CpjArtworkReferenceProfiles.JpegSha256, fixture.Root);
        Assert.Throws<FileNotFoundException>(() => Read());
        File.WriteAllBytes(Path.Combine(fixture.Root, CpjArtworkReferenceProfiles.FileName), new byte[256 * 256 * 4]);
        Assert.Throws<InvalidDataException>(() => Read());
        File.WriteAllBytes(Path.Combine(fixture.Root, CpjArtworkReferenceProfiles.FileName), new byte[4]);
        Assert.Throws<InvalidDataException>(() => Read());
    }

    [Fact]
    public void NativeReferenceRequiresExplicitPrivateRoot()
    {
        Assert.Throws<InvalidDataException>(() => CpjArtworkReferenceProfiles.Read(CpjArtworkReferenceProfiles.CardId,
            CpjArtworkReferenceProfiles.ArchiveSha256, CpjArtworkReferenceProfiles.SlotSha256,
            CpjArtworkReferenceProfiles.JpegSha256, "relative-reference-root"));
    }

    // Structural JPEG fixture; entropy decoding is checked by the pixel decoder.
    private static byte[] Jpeg()
    {
        var data = new List<byte>(new byte[160]);
        data[0] = 255; data[1] = 216; data[2] = 255; data[3] = 224; data[5] = 156;
        data.AddRange(new byte[] { 255, 192, 0, 17, 8, 0, 8, 0, 16, 3, 1, 34, 0, 2, 17, 1, 3, 17, 1 });
        data.AddRange(new byte[] { 255, 218, 0, 12, 3, 1, 0, 2, 17, 3, 17, 0, 63, 0 });
        data.AddRange(new byte[] { 4, 255, 0, 217, 255, 208, 9, 255, 217 });
        return data.ToArray();
    }

    private static byte[] Slot(byte[] jpeg, ushort seed)
    {
        byte[] slot = new byte[512]; jpeg.CopyTo(slot, 0);
        uint state = seed;
        for (int i = 160; i < slot.Length; i++)
        { state = unchecked((state * 50849u + 3343u) >> 1) & 65535; slot[i] ^= (byte)(state >> 2); }
        slot.AsSpan(0, 160).Clear(); BinaryPrimitives.WriteUInt16BigEndian(slot.AsSpan(158), seed);
        return slot;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(317)]
    public void ReconstructsBoundedJpegWithExplicitSeedAndIgnoresSlotPadding(int seed)
    {
        byte[] jpeg = Jpeg();
        var result = CpjArtworkDecoder.Reconstruct(Slot(jpeg, (ushort)seed), jpeg.AsSpan(0, 160));
        Assert.Equal(jpeg, result.Bytes); Assert.Equal(16, result.Width); Assert.Equal(8, result.Height);
    }

    [Fact]
    public void RejectsMissingHeaderOrShortSlot()
    {
        byte[] jpeg = Jpeg();
        Assert.Throws<InvalidDataException>(() => CpjArtworkDecoder.Reconstruct(new byte[160], jpeg.AsSpan(0, 160)));
        Assert.Throws<InvalidDataException>(() => CpjArtworkDecoder.Reconstruct(Slot(jpeg, 1), new byte[160]));
    }

    [Fact]
    public void RejectsProgressiveAndTruncatedImages()
    {
        byte[] jpeg = Jpeg(); jpeg[161] = 194;
        Assert.Throws<InvalidDataException>(() => CpjArtworkDecoder.Reconstruct(Slot(jpeg, 1), jpeg.AsSpan(0, 160)));
        jpeg = Jpeg(); jpeg[^1] = 0;
        Assert.Throws<InvalidDataException>(() => CpjArtworkDecoder.Reconstruct(Slot(jpeg, 1), jpeg.AsSpan(0, 160)));
    }
}
