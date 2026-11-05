using System.Buffers.Binary;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class CharacterVoiceIndexReaderTests
{
    private static byte[] Fixture()
    {
        byte[] bytes = new byte[22];
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), new uint[] { 16, 20, 20, 22 }[i]);
        bytes[16] = 3; bytes[18] = 7; bytes[20] = 9; return bytes;
    }
    [Fact]
    public void PreservesInternalIdNumericReferencesAndEmptySlots()
    {
        var table = CharacterVoiceIndexReader.Read("synthetic_actor01", Fixture());
        Assert.Equal("synthetic_actor01", table.InternalId); Assert.Equal(4, table.OffsetWords);
        Assert.Equal(new ushort[] { 3, 7 }, table.Groups[0].References); Assert.Empty(table.Groups[1].References);
        Assert.Equal(new ushort[] { 9 }, table.Groups[2].References); Assert.Equal(1, table.TrailingTerminalOffsets);
    }
    [Theory]
    [InlineData(4, 15u)]
    [InlineData(8, 24u)]
    [InlineData(12, 20u)]
    public void RejectsUnalignedOutOfBoundsAndMissingTerminalPointers(int at, uint value)
    {
        byte[] bytes = Fixture(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        Assert.Throws<InvalidDataException>(() => CharacterVoiceIndexReader.Read("synthetic_actor01", bytes));
    }
}
