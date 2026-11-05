using System.Buffers.Binary;

namespace TagForceSpecial.Assets.Presentation;

public sealed record CharacterVoiceGroup(int Index, int Offset, IReadOnlyList<ushort> References);
public sealed record CharacterVoiceIndex(string InternalId, int OffsetWords, int TrailingTerminalOffsets,
    IReadOnlyList<CharacterVoiceGroup> Groups);

/// <summary>Observed indexed voice-reference table, not a complete character roster or an audio-cue mapping.
/// Empty terminal slots are preserved; event labels and display names are not guessed.</summary>
public static class CharacterVoiceIndexReader
{
    public static CharacterVoiceIndex Read(string internalId, ReadOnlySpan<byte> bytes)
    {
        Require(!string.IsNullOrWhiteSpace(internalId) && internalId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'), "Invalid internal character identifier.");
        Require(bytes.Length >= 8 && bytes.Length <= 1024 * 1024 && bytes.Length % 2 == 0, "Invalid voice-table length.");
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        Require(first >= 8 && first <= bytes.Length && first % 4 == 0 && first / 4 <= 4096, "Unsupported voice-offset directory.");
        int count = (int)first / 4; var offsets = new int[count]; int previous = (int)first;
        for (int i = 0; i < count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
            Require(offset >= previous && offset <= bytes.Length && offset % 2 == 0, "Voice range exceeds declared member bounds or is not ordered/aligned.");
            offsets[i] = (int)offset; previous = (int)offset;
        }
        Require(offsets[^1] == bytes.Length, "Voice terminal offset differs from member length.");
        var groups = new List<CharacterVoiceGroup>();
        for (int i = 0; i < count - 1; i++)
        {
            var values = new ushort[(offsets[i + 1] - offsets[i]) / 2];
            for (int j = 0; j < values.Length; j++) values[j] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offsets[i] + j * 2)..]);
            groups.Add(new(i, offsets[i], Array.AsReadOnly(values)));
        }
        int length = bytes.Length;
        return new(internalId, count, offsets.Reverse().TakeWhile(v => v == length).Count(), groups.AsReadOnly());
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
