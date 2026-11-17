using System.Buffers.Binary;
using TagForceSpecial.Import.Sources;

namespace TagForceSpecial.Tests;

public sealed class ImportSourceMetadataTests
{
    [Fact]
    public void ParamSfoReadsIdentityAndRejectsTruncatedValueRanges()
    {
        byte[] data = new byte[64]; "\0PSF"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 44);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 0x0204);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 10);
        "DISC_ID\0"u8.CopyTo(data.AsSpan(36)); "NPJH00142\0"u8.CopyTo(data.AsSpan(44));
        Assert.Equal("NPJH00142", ParamSfo.Read(data)["DISC_ID"]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 100);
        Assert.Throws<InvalidDataException>(() => ParamSfo.Read(data));
    }
    [Fact]
    public void ClassificationUsesBytesRatherThanAConvincingExtension()
    {
        Assert.Equal("unknown", FileClassifier.Classify("art.gim", [1, 2, 3, 4], 4).Format);
        Assert.Equal("gim", FileClassifier.Classify("unknown.bin", "MIG.00.1PSP"u8, 100).Format);
        Assert.Equal("empty", FileClassifier.Classify("archive.ehp", [], 0).Format);
        Assert.Null(FileClassifier.Classify("encrypted.bin", "~PSP"u8, 100).Decoder);
    }
}
