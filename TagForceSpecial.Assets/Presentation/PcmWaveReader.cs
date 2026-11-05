using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TagForceSpecial.Assets.Presentation;

public sealed record PcmWaveInfo(int Channels, int SampleRate, int BitsPerSample, long SampleFrames, string PcmSha256);

/// <summary>Read-only RIFF PCM16 structure and sample-count validation; not an audio-quality verdict.</summary>
public static class PcmWaveReader
{
    public static PcmWaveInfo Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Readable seekable WAV stream required.", nameof(stream));
        long original = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[12]; stream.ReadExactly(header);
            Adx2AudioReader.Require(header[..4].SequenceEqual("RIFF"u8) && header[8..].SequenceEqual("WAVE"u8) &&
                BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) + 8L == stream.Length, "Unsupported/truncated RIFF WAV.");
            int channels = 0, rate = 0, alignment = 0; long dataAt = -1, dataBytes = 0;
            Span<byte> chunk = stackalloc byte[8]; Span<byte> format = stackalloc byte[16];
            while (stream.Position < stream.Length)
            {
                Adx2AudioReader.Require(stream.Length - stream.Position >= 8, "Truncated WAV chunk header.");
                stream.ReadExactly(chunk); long length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                long start = stream.Position, padded = length + (length & 1);
                Adx2AudioReader.Require(padded <= stream.Length - start, "WAV chunk exceeds RIFF bounds.");
                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    Adx2AudioReader.Require(channels == 0 && length >= 16, "Missing/duplicate WAV format.");
                    stream.ReadExactly(format);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]); rate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
                    alignment = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
                    Adx2AudioReader.Require(BinaryPrimitives.ReadUInt16LittleEndian(format) == 1 && channels is > 0 and <= 16 &&
                        rate is > 0 and <= 384000 && alignment == channels * 2 && BinaryPrimitives.ReadUInt16LittleEndian(format[14..]) == 16 &&
                        BinaryPrimitives.ReadUInt32LittleEndian(format[8..]) == (long)rate * alignment, "Only consistent PCM16 WAV is supported.");
                }
                if (chunk[..4].SequenceEqual("data"u8))
                { Adx2AudioReader.Require(dataAt == -1, "Duplicate WAV data chunk."); dataAt = start; dataBytes = length; }
                stream.Position = start + padded;
            }
            Adx2AudioReader.Require(channels > 0 && dataAt >= 0 && dataBytes % alignment == 0, "Missing/misaligned PCM data.");
            stream.Position = dataAt;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536];
            for (long left = dataBytes; left > 0;)
            {
                int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (count == 0) throw new InvalidDataException("Truncated PCM data.");
                hash.AppendData(buffer, 0, count); left -= count;
            }
            return new(channels, rate, 16, dataBytes / alignment, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Truncated WAV.", error); }
        finally { stream.Position = original; }
    }
}
