using System.Buffers.Binary;

namespace TagForceSpecial.Assets.Presentation;

public sealed record AwbEntry(uint WaveId, long Offset, long Length);
public sealed record HcaInfo(int Version, int Channels, int SampleRate, uint Blocks, int EncoderDelay,
    int EncoderPadding, int HeaderBytes, int BlockBytes, int CipherType, long EncodedSamples, long AudibleSamples);
public sealed record Adx2Waveform(int Row, uint WaveId, bool Streaming, long Offset, long Length,
    int DeclaredChannels, int DeclaredRate, uint DeclaredSamples);
public sealed record Adx2Cue(int CueIndex, uint CueId, string Name, int ReferenceType, int ReferenceIndex,
    IReadOnlyList<int> WaveformRows, IReadOnlyList<string> Diagnostics);
public sealed record Adx2Bank(IReadOnlyList<AwbEntry> EmbeddedEntries, IReadOnlyList<AwbEntry> StreamingEntries,
    IReadOnlyList<Adx2Waveform> Waveforms, IReadOnlyList<Adx2Cue> Cues);

/// <summary>Independent AFS2 directory and ACB waveform/direct/synth reference semantics.
/// Not an HCA sample codec, sequence player, mixer or cue-to-screen mapping.</summary>
public static class Adx2AudioReader
{
    public static IReadOnlyList<AwbEntry> ReadAwb(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length >= 16 && bytes[..4].SequenceEqual("AFS2"u8), "Missing AFS2 header.");
        Require(bytes[4] is 1 or 2, "Unsupported AFS2 version.");
        int offsetBytes = bytes[5], idBytes = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        int alignment = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        Require(offsetBytes is 2 or 4 or 8 && idBytes is 2 or 4 && alignment > 0 && count <= 100_000,
            "Unsupported AFS2 integer widths/alignment/count.");
        long tableEnd = 16 + count * (long)idBytes + (count + 1L) * offsetBytes;
        Require(tableEnd <= bytes.Length, "Truncated AFS2 tables.");
        int offsetTable = 16 + (int)count * idBytes;
        var entries = new List<AwbEntry>(); var ids = new HashSet<uint>();
        long previousEnd = tableEnd;
        for (int i = 0; i < count; i++)
        {
            uint id = (uint)Unsigned(bytes.Slice(16 + i * idBytes, idBytes));
            long raw = Offset(bytes.Slice(offsetTable + i * offsetBytes, offsetBytes));
            long next = Offset(bytes.Slice(offsetTable + (i + 1) * offsetBytes, offsetBytes));
            long start = checked(raw + (alignment - raw % alignment) % alignment);
            Require(ids.Add(id) && raw >= previousEnd && start <= next && next <= bytes.Length, "Invalid AFS2 ID/payload range.");
            entries.Add(new(id, start, next - start)); previousEnd = next;
        }
        Require(Offset(bytes.Slice(offsetTable + (int)count * offsetBytes, offsetBytes)) == bytes.Length,
            "AFS2 terminal offset differs from bank length.");
        return entries.AsReadOnly();
    }

    public static Adx2Bank ReadAcb(byte[] acb, byte[]? streamingAwb)
    {
        ArgumentNullException.ThrowIfNull(acb);
        Require(acb.Length <= 64 * 1024 * 1024, "ACB exceeds supported size.");
        var header = CriUtfTable.Read(acb, 0, acb.Length);
        Require(header.Name == "Header" && header.Rows.Count == 1, "Unsupported ACB header table.");
        CriUtfValue Blob(string name) => header.Rows[0].TryGetValue(name, out var value) && value.Type == 11
            ? value : throw new InvalidDataException("Missing ACB subtable: " + name);
        CriUtfTable Subtable(string name)
        {
            var value = Blob(name);
            return value.Length == 0 ? new(name, Array.Empty<IReadOnlyDictionary<string, CriUtfValue>>())
                : CriUtfTable.Read(acb, value.Offset, value.Offset + value.Length);
        }
        var memory = Blob("AwbFile");
        var embedded = memory.Length == 0 ? Array.Empty<AwbEntry>() :
            ReadAwb(acb.AsSpan(memory.Offset, memory.Length)).Select(e => e with { Offset = e.Offset + memory.Offset }).ToArray();
        var streamed = streamingAwb is null ? Array.Empty<AwbEntry>() : ReadAwb(streamingAwb).ToArray();
        var memoryIds = embedded.ToDictionary(e => e.WaveId); var streamIds = streamed.ToDictionary(e => e.WaveId);
        var waveformTable = Subtable("WaveformTable"); var waves = new List<Adx2Waveform>();
        for (int i = 0; i < waveformTable.Rows.Count; i++)
        {
            var row = waveformTable.Rows[i]; uint flag = Number(row, "Streaming");
            Require(flag is 0 or 1, "Unsupported streaming/prefetch mode.");
            bool streaming = flag == 1;
            uint id = row.ContainsKey("Id") ? Number(row, "Id") : Number(row, streaming ? "StreamAwbId" : "MemoryAwbId");
            Require((streaming ? streamIds : memoryIds).TryGetValue(id, out var entry), "Waveform references missing bank/ID.");
            Require(Number(row, "EncodeType") == 2, "Only HCA waveform encoding is supported.");
            waves.Add(new(i, id, streaming, entry!.Offset, entry.Length,
                checked((int)Number(row, "NumChannels")), checked((int)Number(row, "SamplingRate")), Number(row, "NumSamples")));
        }
        var cueTable = Subtable("CueTable"); var names = Subtable("CueNameTable"); var synth = Subtable("SynthTable");
        var cues = new List<Adx2Cue>();
        foreach (var name in names.Rows)
        {
            int index = checked((int)Number(name, "CueIndex")); Require(index < cueTable.Rows.Count, "Cue index out of bounds.");
            var cue = cueTable.Rows[index]; int type = checked((int)Number(cue, "ReferenceType")), target = checked((int)Number(cue, "ReferenceIndex"));
            var found = new SortedSet<int>(); var diagnostics = new List<string>(); var path = new HashSet<int>(); int visited = 0;
            void Resolve(int kind, int item, int depth)
            {
                Require(depth <= 32 && ++visited <= 10000, "Cue graph traversal limit exceeded.");
                if (kind == 1) { Require(item < waves.Count, "Waveform reference out of bounds."); found.Add(item); return; }
                if (kind != 2) { diagnostics.Add($"Reference type {kind} is unsupported; cue playback semantics remain unverified."); return; }
                Require(item < synth.Rows.Count && path.Add(item), "Invalid/cyclic synth reference.");
                var refs = synth.Rows[item]["ReferenceItems"];
                Require(refs.Type == 11 && refs.Length % 4 == 0, "Invalid synth reference list.");
                for (int at = refs.Offset; at < refs.Offset + refs.Length; at += 4)
                    Resolve(BinaryPrimitives.ReadUInt16BigEndian(acb.AsSpan(at)), BinaryPrimitives.ReadUInt16BigEndian(acb.AsSpan(at + 2)), depth + 1);
                path.Remove(item);
            }
            Resolve(type, target, 0);
            Require(name.TryGetValue("CueName", out var cueName) && cueName.Type == 10, "Missing cue name.");
            cues.Add(new(index, Number(cue, "CueId"), cueName!.Text!, type, target, found.ToArray(), diagnostics.AsReadOnly()));
        }
        return new(Array.AsReadOnly(embedded), Array.AsReadOnly(streamed), waves.AsReadOnly(), cues.AsReadOnly());
    }

    public static HcaInfo ReadHca(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length >= 40 && Tag(bytes, "HCA\0") && Tag(bytes[8..], "fmt\0"), "Unsupported HCA header.");
        int header = BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]);
        int channels = bytes[12], rate = (bytes[13] << 16) | (bytes[14] << 8) | bytes[15];
        uint blocks = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        int delay = BinaryPrimitives.ReadUInt16BigEndian(bytes[20..]), padding = BinaryPrimitives.ReadUInt16BigEndian(bytes[22..]);
        Require(header >= 40 && header <= bytes.Length && channels is > 0 and <= 16 && rate is > 0 and <= 384000 && blocks > 0,
            "Invalid HCA dimensions/header.");
        Require(Tag(bytes[24..], "comp") || Tag(bytes[24..], "dec\0"), "Unsupported HCA compression header.");
        int blockBytes = BinaryPrimitives.ReadUInt16BigEndian(bytes[28..]);
        Require(blockBytes >= 8 && header + (long)blocks * blockBytes == bytes.Length, "HCA encoded block storage mismatch.");
        long samples = (long)blocks * 1024;
        Require(delay + padding <= samples, "Invalid HCA delay/padding.");
        int cipher = -1;
        for (int at = Tag(bytes[24..], "comp") ? 40 : 36; at < header - 2;)
        {
            Require(at + 4 <= header - 2, "Truncated HCA chunk tag.");
            if (Tag(bytes[at..], "pad\0")) break;
            int size;
            if (Tag(bytes[at..], "ciph")) { Require(at + 6 <= header - 2, "Truncated HCA cipher chunk."); cipher = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 4)..]); size = 6; }
            else if (Tag(bytes[at..], "loop")) size = 16;
            else if (Tag(bytes[at..], "ath\0")) size = 6;
            else if (Tag(bytes[at..], "rva\0") || Tag(bytes[at..], "vbr\0")) size = 8;
            else if (Tag(bytes[at..], "comm")) { Require(at + 5 <= header - 2, "Truncated comment chunk."); size = 5 + bytes[at + 4]; }
            else throw new InvalidDataException("Unsupported HCA chunk.");
            Require(at + size <= header - 2, "HCA chunk exceeds header."); at += size;
        }
        Require(cipher is 0, "Encrypted or unspecified HCA cipher is not signed off.");
        return new(BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]), channels, rate, blocks, delay, padding,
            header, blockBytes, cipher, samples, samples - delay - padding);
    }

    private static bool Tag(ReadOnlySpan<byte> data, string tag)
    {
        if (data.Length < 4) return false;
        for (int i = 0; i < 4; i++) if ((data[i] & 127) != tag[i]) return false;
        return true;
    }
    private static uint Number(IReadOnlyDictionary<string, CriUtfValue> row, string name)
    {
        Require(row.TryGetValue(name, out var value) && value.Type is 0 or 2 or 4 && value.Number <= uint.MaxValue,
            "Missing or unsupported unsigned ACB field: " + name);
        return (uint)value!.Number;
    }
    private static ulong Unsigned(ReadOnlySpan<byte> value)
    { ulong result = 0; for (int i = value.Length - 1; i >= 0; i--) result = (result << 8) | value[i]; return result; }
    private static long Offset(ReadOnlySpan<byte> value)
    { ulong n = Unsigned(value); Require(n <= long.MaxValue - 65536, "AFS2 offset exceeds signed bounds."); return (long)n; }
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
