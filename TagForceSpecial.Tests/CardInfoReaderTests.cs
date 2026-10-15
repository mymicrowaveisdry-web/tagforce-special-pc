using System.Buffers.Binary;
using System.Text;
using TagForceSpecial.Assets;

namespace TagForceSpecial.Tests;

public class CardInfoReaderTests
{
    private static void Put(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
    private static Dictionary<string, byte[]> Tables()
    {
        var props = new byte[24]; Put(props, 8, 100u | 0x80000000); Put(props, 12, 0x12345678);
        Put(props, 16, 102); Put(props, 20, 0x87654321);
        var index = new byte[32]; Put(index, 8, 2); Put(index, 12, 123); Put(index, 16, 6);
        return new() { ["CARD_Prop.bin"] = props, ["CARD_Indx_R.bin"] = index,
            ["CARD_Name_R.bin"] = Encoding.Unicode.GetBytes("\0A\0B\0"),
            ["CARD_IntID.bin"] = [1, 0, 0, 0, 2, 0] };
    }

    private static byte[] Pack(Dictionary<string, byte[]> tables)
    {
        int meta = 16 + tables.Count * 8;
        int data = (meta + tables.Sum(t => t.Key.Length + 5) + 15) & ~15;
        var b = new byte[data + tables.Sum(t => t.Value.Length)];
        "EHP\x03"u8.CopyTo(b); Put(b, 4, (uint)b.Length); Put(b, 8, 0x20544f4e); Put(b, 12, (uint)tables.Count);
        int i = 0;
        foreach (var (name, bytes) in tables)
        {
            Put(b, 16 + i * 8, (uint)meta); Put(b, 20 + i * 8, (uint)data);
            Encoding.ASCII.GetBytes(name).CopyTo(b, meta); Put(b, meta + name.Length + 1, (uint)bytes.Length);
            bytes.CopyTo(b, data); meta += name.Length + 5; data += bytes.Length; i++;
        }
        return b;
    }

    [Fact]
    public void CrossLinksIdsAndNamesAndPreservesUnknownBits()
    {
        var b = Pack(Tables()); var before = (byte[])b.Clone();
        using var stream = new MemoryStream(b, writable: false); stream.Position = 9;
        var result = new CardInfoReader().Read(stream, 100, 102);
        Assert.Equal(9, stream.Position); Assert.Equal(before, b); Assert.Equal(1, result.MissingIds);
        Assert.Equal(new CardInfoRow(1, 100, "A", 100u | 0x80000000, 0x12345678, 123), result.Rows[0]);
        Assert.Equal("B", result.Rows[1].Name); Assert.NotEmpty(result.Limitations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-length")]
    [InlineData("bad-row")]
    [InlineData("duplicate-row")]
    [InlineData("id-mismatch")]
    [InlineData("bad-name-offset")]
    [InlineData("odd-name-offset")]
    [InlineData("unterminated-name")]
    [InlineData("bad-utf16")]
    public void RejectsBrokenTableLinksAndStrings(string scenario)
    {
        var t = Tables();
        switch (scenario)
        {
            case "missing": t.Remove("CARD_Prop.bin"); break;
            case "wrong-length": t["CARD_Prop.bin"] = new byte[23]; break;
            case "bad-row": t["CARD_IntID.bin"][0] = 99; break;
            case "duplicate-row": t["CARD_IntID.bin"][4] = 1; break;
            case "id-mismatch": Put(t["CARD_Prop.bin"], 8, 101); break;
            case "bad-name-offset": Put(t["CARD_Indx_R.bin"], 8, uint.MaxValue); break;
            case "odd-name-offset": Put(t["CARD_Indx_R.bin"], 8, 3); break;
            case "unterminated-name": Array.Fill(t["CARD_Name_R.bin"], (byte)65); break;
            case "bad-utf16": t["CARD_Name_R.bin"][2] = 0; t["CARD_Name_R.bin"][3] = 0xd8; break;
        }
        using var stream = new MemoryStream(Pack(t)); stream.Position = 3;
        Assert.Throws<InvalidDataException>(() => new CardInfoReader().Read(stream, 100, 102));
        Assert.Equal(3, stream.Position);
    }
}
