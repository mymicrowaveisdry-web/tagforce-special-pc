using System.Buffers.Binary;
using System.Text;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class IndexedTextReaderTests
{
    private static byte[] Index(params uint[] offsets)
    {
        byte[] bytes = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), offsets[i]);
        return bytes;
    }
    [Fact]
    public void BackwardAliasesAndTrailingZeroArePreservedWithoutAssumingSortedOffsets()
    {
        byte[] text = Encoding.Unicode.GetBytes("Alpha\0Beta\0");
        var rows = IndexedTextReader.ReadPair(Index(0, 6, 0, 11, 0), text).Records;
        Assert.Equal(5, rows.Count); Assert.Equal("Alpha", rows[0].Text); Assert.Equal("Beta", rows[1].Text);
        Assert.Equal("Alpha", rows[2].Text); Assert.True(rows[3].IsEndMarker); Assert.Equal("Alpha", rows[4].Text);
        Assert.Equal(4, rows[4].Index); Assert.All(rows, r => Assert.Null(r.Diagnostic));
    }
    [Fact]
    public void LastIndexMayPointToARealStringInsteadOfASentinel()
    {
        var rows = IndexedTextReader.ReadPair(Index(0, 4), Encoding.Unicode.GetBytes("one\0two\0")).Records;
        Assert.Equal(2, rows.Count); Assert.Equal("two", rows[1].Text); Assert.False(rows[1].IsEndMarker);
    }
    [Fact]
    public void InvalidPointerDoesNotHideOtherRowsOrFabricateAString()
    {
        var rows = IndexedTextReader.ReadPair(Index(0, uint.MaxValue, 2), Encoding.Unicode.GetBytes("a\0b\0")).Records;
        Assert.Equal("a", rows[0].Text); Assert.NotNull(rows[1].Diagnostic); Assert.Null(rows[1].Text); Assert.Equal("b", rows[2].Text);
    }
    [Fact]
    public void StringTableUsesByteOffsetsAndPreservesUnmodifiedControlText()
    {
        byte[] text = Encoding.Unicode.GetBytes("$Rraw\n{{PLAYER}}\0x\0");
        byte[] file = new byte[24 + text.Length]; Index(3, 12, 24, 0, (uint)text.Length - 4, (uint)text.Length).CopyTo(file, 0);
        text.CopyTo(file, 24);
        var rows = IndexedTextReader.ReadStringTable(file).Records;
        Assert.Equal("$Rraw\n{{PLAYER}}", rows[0].Text); Assert.Equal("x", rows[1].Text); Assert.True(rows[2].IsEndMarker);
    }
    [Fact]
    public void TruncatedDirectoryAndMalformedUtfAreExplicitlyRejected()
    {
        Assert.Throws<InvalidDataException>(() => IndexedTextReader.ReadStringTable(Index(378, 12, 1524)));
        Assert.Throws<InvalidDataException>(() => IndexedTextReader.ReadPair([0], [0, 0]));
        Assert.Throws<InvalidDataException>(() => IndexedTextReader.ReadPair(Index(0), [0]));
        var bad = IndexedTextReader.ReadPair(Index(0), [0, 0xd8, 0, 0]).Records[0];
        Assert.Null(bad.Text); Assert.Contains("Invalid UTF", bad.Diagnostic!);
    }
    [Fact]
    public void UnterminatedAndInteriorPointersRemainUnverified()
    {
        Assert.NotNull(IndexedTextReader.ReadPair(Index(0), Encoding.Unicode.GetBytes("no terminator")).Records[0].Diagnostic);
        Assert.NotNull(IndexedTextReader.ReadPair(Index(1), Encoding.Unicode.GetBytes("abc\0")).Records[0].Diagnostic);
    }
}
