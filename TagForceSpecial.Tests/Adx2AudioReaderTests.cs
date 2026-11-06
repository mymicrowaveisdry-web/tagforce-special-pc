using System.Buffers.Binary;
using System.Text;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class Adx2AudioReaderTests
{
    private static void Le(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
    private static void Be(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
    private static byte[] Awb()
    {
        byte[] bytes = new byte[50]; "AFS2"u8.CopyTo(bytes); bytes[4] = 1; bytes[5] = 4; bytes[6] = 2;
        Le(bytes, 8, 2); bytes[12] = 16; bytes[16] = 4; bytes[18] = 9;
        Le(bytes, 20, 32); Le(bytes, 24, 35); Le(bytes, 28, 50); return bytes;
    }
    private static byte[] Hca()
    {
        byte[] bytes = new byte[72]; "HCA\0"u8.CopyTo(bytes); bytes[4] = 2; bytes[7] = 64;
        "fmt\0"u8.CopyTo(bytes.AsSpan(8)); bytes[12] = 2; bytes[14] = 125; // 32000 Hz
        Be(bytes, 16, 1); bytes[21] = 10; bytes[23] = 14;
        "comp"u8.CopyTo(bytes.AsSpan(24)); bytes[29] = 8;
        "ciph"u8.CopyTo(bytes.AsSpan(40)); "pad\0"u8.CopyTo(bytes.AsSpan(46)); return bytes;
    }

    [Fact]
    public void AwbUsesRealWaveIdsAndAlignsStartsWithoutClippingEnds()
    {
        byte[] bytes = Awb(); var rows = Adx2AudioReader.ReadAwb(bytes);
        Assert.Equal(new AwbEntry(4, 32, 3), rows[0]); Assert.Equal(new AwbEntry(9, 48, 2), rows[1]);
    }
    [Theory]
    [InlineData(8, uint.MaxValue)]
    [InlineData(20, 16u)]
    [InlineData(24, 31u)]
    [InlineData(28, 51u)]
    public void AwbRejectsUnsafeCountsAndRanges(int at, uint value)
    { byte[] bytes = Awb(); Le(bytes, at, value); Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadAwb(bytes)); }

    [Fact]
    public void AwbRejectsDuplicateIdsAndTruncatedTables()
    {
        byte[] bytes = Awb(); bytes[18] = 4;
        Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadAwb(bytes));
        Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadAwb(Awb()[..30]));
    }
    [Fact]
    public void HcaKeepsEncodedAndAudibleSampleCountsSeparate()
    {
        var header = Adx2AudioReader.ReadHca(Hca());
        Assert.Equal(32000, header.SampleRate); Assert.Equal(2, header.Channels);
        Assert.Equal(1024, header.EncodedSamples); Assert.Equal(1000, header.AudibleSamples); Assert.Equal(0, header.CipherType);
    }
    [Theory]
    [InlineData(7, 100)]
    [InlineData(12, 0)]
    [InlineData(29, 9)]
    [InlineData(45, 56)]
    public void HcaRejectsInvalidStorageAndEncryptedContent(int at, byte value)
    { byte[] bytes = Hca(); bytes[at] = value; Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadHca(bytes)); }

    [Fact]
    public void AcbRejectsMissingUtfSignatureAndTruncatedSchema()
    {
        Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadAcb(new byte[32], null));
        byte[] bytes = new byte[32]; "@UTF"u8.CopyTo(bytes); Be(bytes, 4, 24); Be(bytes, 8, 24);
        Be(bytes, 12, 24); Be(bytes, 16, 24); bytes[25] = 1;
        Assert.Throws<InvalidDataException>(() => Adx2AudioReader.ReadAcb(bytes, null));
    }
    [Fact]
    public void WaveCountsPcmFramesAndRestoresPositionOnSuccessAndFailure()
    {
        byte[] bytes = new byte[52]; "RIFF"u8.CopyTo(bytes); Le(bytes, 4, 44); "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        Le(bytes, 16, 16); bytes[20] = 1; bytes[22] = 2; Le(bytes, 24, 32000); Le(bytes, 28, 128000);
        bytes[32] = 4; bytes[34] = 16; "data"u8.CopyTo(bytes.AsSpan(36)); Le(bytes, 40, 8);
        using var stream = new MemoryStream(bytes); stream.Position = 3;
        Assert.Equal(2, PcmWaveReader.Read(stream).SampleFrames); Assert.Equal(3, stream.Position);
        bytes[40] = 9; using var bad = new MemoryStream(bytes); bad.Position = 3;
        Assert.Throws<InvalidDataException>(() => PcmWaveReader.Read(bad)); Assert.Equal(3, bad.Position);
    }

    private static byte[] Utf(string name, (string Name, int Type)[] columns, params object[][] rows)
    {
        using var strings = new MemoryStream(); using var values = new MemoryStream(); using var data = new MemoryStream();
        int Text(string text) { int at = (int)strings.Length; strings.Write(Encoding.UTF8.GetBytes(text + "\0")); return at; }
        int tableName = Text(name); int[] names = columns.Select(c => Text(c.Name)).ToArray();
        int rowWidth = columns.Sum(c => c.Type == 11 ? 8 : 4);
        void Write(MemoryStream s, uint value) { byte[] b = new byte[4]; Be(b, 0, value); s.Write(b); }
        foreach (object[] row in rows)
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i].Type == 11)
                {
                    byte[] blob = (byte[])row[i]; Write(values, (uint)data.Length); Write(values, (uint)blob.Length); data.Write(blob);
                }
                else Write(values, columns[i].Type == 10 ? (uint)Text((string)row[i]) : Convert.ToUInt32(row[i]));
            }
        int rowsAt = 32 + columns.Length * 5, stringsAt = rowsAt + (int)values.Length, dataAt = stringsAt + (int)strings.Length;
        byte[] result = new byte[dataAt + data.Length]; "@UTF"u8.CopyTo(result); Be(result, 4, (uint)result.Length - 8);
        Be(result, 8, (uint)rowsAt - 8); Be(result, 12, (uint)stringsAt - 8); Be(result, 16, (uint)dataAt - 8); Be(result, 20, (uint)tableName);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(24), (ushort)columns.Length);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(26), (ushort)rowWidth); Be(result, 28, (uint)rows.Length);
        for (int i = 0; i < columns.Length; i++) { result[32 + i * 5] = (byte)(0x50 | columns[i].Type); Be(result, 33 + i * 5, (uint)names[i]); }
        values.ToArray().CopyTo(result, rowsAt); strings.ToArray().CopyTo(result, stringsAt); data.ToArray().CopyTo(result, dataAt); return result;
    }
    private static (byte[] Acb, byte[] Awb) ReversedIdCue(bool embedded)
    {
        byte[] awb = new byte[184]; Awb()[..32].CopyTo(awb, 0); Le(awb, 24, 104); Le(awb, 28, 184);
        Hca().CopyTo(awb, 32); Hca().CopyTo(awb, 112);
        byte[] waves = Utf("WaveformTable", [("Id", 4), ("EncodeType", 4), ("Streaming", 4), ("NumChannels", 4), ("SamplingRate", 4), ("NumSamples", 4)],
            [9u, 2u, embedded ? 0u : 1u, 2u, 32000u, 1000u], [4u, 2u, embedded ? 0u : 1u, 2u, 32000u, 1000u]);
        byte[] cues = Utf("CueTable", [("CueId", 4), ("ReferenceType", 4), ("ReferenceIndex", 4)], [123u, 1u, 0u]);
        byte[] names = Utf("CueNameTable", [("CueIndex", 4), ("CueName", 10)], [0u, "synthetic-reversed-id"]);
        byte[] acb = Utf("Header", [("AwbFile", 11), ("WaveformTable", 11), ("CueTable", 11), ("CueNameTable", 11), ("SynthTable", 11)],
            [embedded ? awb : Array.Empty<byte>(), waves, cues, names, Array.Empty<byte>()]);
        return (acb, awb);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CueJoinsWaveformRowToActualBankIdAndRespectsEmbeddedBase(bool embedded)
    {
        var fixture = ReversedIdCue(embedded);
        var bank = Adx2AudioReader.ReadAcb(fixture.Acb, embedded ? null : fixture.Awb);
        var cue = Assert.Single(bank.Cues); Assert.Equal("synthetic-reversed-id", cue.Name);
        Assert.Equal(0, Assert.Single(cue.WaveformRows));
        var wave = bank.Waveforms[0]; Assert.Equal(9u, wave.WaveId); Assert.Equal(!embedded, wave.Streaming);
        if (!embedded) Assert.Equal(112, wave.Offset);
        Assert.Equal(Hca(), (embedded ? fixture.Acb : fixture.Awb).AsSpan((int)wave.Offset, (int)wave.Length).ToArray());
    }
}
