using System.Buffers.Binary;
using TagForceSpecial.Assets;

namespace TagForceSpecial.Tests;

public class CardDetailReaderTests
{
    private static byte[] Tree()
    {
        // Root branches to leaf 'A' or terminator. Branches are byte offsets.
        var b = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 6);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), 'A');
        return b;
    }
    [Fact] public void DecodesMsbFirstBitsFromUnalignedOffset()
    { Assert.Equal("A", CardHuffmanDecoder.Decode(Tree(), [0b00001000], 3)); }
    [Fact] public void TraversalIsBounded()
    {
        Assert.Throws<InvalidDataException>(() => CardHuffmanDecoder.Decode(Tree(), [], 0));
        Assert.Throws<InvalidDataException>(() => CardHuffmanDecoder.Decode(Tree(), [0], 0, 1));
        var cyclic = Tree(); BinaryPrimitives.WriteUInt16LittleEndian(cyclic.AsSpan(6), 6);
        Assert.Throws<InvalidDataException>(() => CardHuffmanDecoder.Decode(cyclic, [0], 0));
        var unaligned = Tree(); unaligned[0] = 5;
        Assert.Throws<InvalidDataException>(() => CardHuffmanDecoder.Decode(unaligned, [0], 0));
    }
    [Fact] public void PreservesVariableStatsAndKnownKindException()
    {
        var fields = CardPropertyDecoder.Decode(4998, (511u << 14) | (511u << 23), 2);
        Assert.Null(fields.Attack); Assert.Null(fields.Defense); Assert.Equal(1u, fields.RawKindCode);
        Assert.Equal(11u, fields.GetterKindCode);
    }
    [Fact] public void DecodesIndependentSyntheticBitFields()
    {
        var f = CardPropertyDecoder.Decode(100, (123u << 14) | (456u << 23),
            (3u << 7) | (9u << 11) | (5u << 18) | (2u << 15) | (7u << 1));
        Assert.Equal(1230, f.Attack); Assert.Equal(4560, f.Defense);
        Assert.Equal(3u, f.AttributeCode); Assert.Equal(9u, f.Level); Assert.Equal(5u, f.TypeCode);
        Assert.Equal(2u, f.IconCode); Assert.Equal(7u, f.GetterKindCode);
    }
    [Fact] public void DictionaryThenNameSubstitutionPreservesOtherMarkup()
    {
        Assert.Equal("card $R", CardDescriptionFormatter.Expand("$d000 $R", ["$m123"], new Dictionary<uint,string>{{123,"card"}}));
        Assert.Throws<InvalidDataException>(() => CardDescriptionFormatter.Expand("$d999", ["word"], new Dictionary<uint,string>()));
        Assert.Throws<InvalidDataException>(() => CardDescriptionFormatter.Expand("$m123", [], new Dictionary<uint,string>()));
        Assert.Throws<InvalidDataException>(() => CardDescriptionFormatter.ReadDictionary(new byte[11]));
    }
}
