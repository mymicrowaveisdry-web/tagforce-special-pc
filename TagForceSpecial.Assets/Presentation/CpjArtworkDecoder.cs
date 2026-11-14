using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace TagForceSpecial.Assets.Presentation;

public sealed record CpjArtworkProvenance(uint CardId, string HeaderSourcePath, string HeaderSourceSha256,
    long HeaderOffset, int HeaderLength, string HeaderSha256, string JpegSha256, string PixelDecoderSha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReferenceEvidenceId = null);
public sealed record CpjJpeg(byte[] Bytes, int Width, int Height);

/// <summary>Reconstructs CPJ artwork slots from private source header bytes.</summary>
public static class CpjArtworkDecoder
{
    public const string ConversionId = "cpj-rgba-v1";
    public const string NativeConversionId = "cpj-native-rgba-v1";
    public const int HeaderLength = 160;

    public static CpjJpeg Reconstruct(ReadOnlySpan<byte> slot, ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength || slot.Length <= HeaderLength || slot.Length > 1024 * 1024 ||
            header[0] != 255 || header[1] != 216)
            throw new InvalidDataException("Invalid CPJ slot/header bounds.");
        byte[] jpeg = slot.ToArray();
        uint state = BinaryPrimitives.ReadUInt16BigEndian(slot[158..]);
        header.CopyTo(jpeg);
        for (int at = HeaderLength; at < jpeg.Length; at++)
        {
            state = unchecked((state * 50849u + 3343u) >> 1) & 65535u;
            jpeg[at] ^= (byte)(state >> 2);
        }
        int width = 0, height = 0, end = 0;
        bool scan = false;
        for (int at = 2; at < jpeg.Length;)
        {
            if (jpeg[at++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
            while (at < jpeg.Length && jpeg[at] == 255) at++;
            if (at >= jpeg.Length) break;
            int marker = jpeg[at++];
            if (marker == 217) { end = at; break; }
            if (marker is 0 or 216 or >= 208 and <= 215 || at > jpeg.Length - 2)
                throw new InvalidDataException("Unexpected JPEG marker.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at));
            if (length < 2 || length > jpeg.Length - at) throw new InvalidDataException("Truncated JPEG segment.");
            if (marker == 192)
            {
                if (width != 0 || length < 17 || jpeg[at + 2] != 8 || jpeg[at + 7] != 3)
                    throw new InvalidDataException("Unsupported CPJ baseline frame.");
                height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 3));
                width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 5));
            }
            else if (marker is >= 193 and <= 195 or >= 197 and <= 199 or >= 201 and <= 203 or >= 205 and <= 207)
                throw new InvalidDataException("Only baseline JPEG is supported.");
            at += length;
            if (marker != 218) continue;
            if (scan || width <= 0 || height <= 0) throw new InvalidDataException("Invalid CPJ JPEG scan.");
            scan = true;
            // Entropy-coded FF00 escapes and restart markers are not end-of-image markers.
            while (at < jpeg.Length - 1)
            {
                if (jpeg[at] != 255) { at++; continue; }
                if (jpeg[at + 1] is 0 or >= 208 and <= 215) { at += 2; continue; }
                break;
            }
        }
        if (!scan || end == 0 || width is <= 0 or > 4096 || height is <= 0 or > 4096)
            throw new InvalidDataException("Missing bounded JPEG frame/scan/end marker.");
        return new(jpeg.AsSpan(0, end).ToArray(), width, height);
    }

    internal static (byte[] Rgba, string DecoderSha256) DecodePixels(CpjJpeg jpeg, string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidDataException("CPJ conversion requires an explicit existing independently licensed FFmpeg executable.");
        string FileHash() { using var file = File.OpenRead(executable); return Convert.ToHexStringLower(SHA256.HashData(file)); }
        string identity = FileHash();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-xerror", "-err_detect", "explode",
            "-f", "image2pipe", "-i", "pipe:0", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", "pipe:1" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Pixel decoder could not start.");
        int expected = checked(jpeg.Width * jpeg.Height * 4);
        var pixels = Task.Run(() =>
        {
            byte[] data = new byte[expected]; process.StandardOutput.BaseStream.ReadExactly(data);
            if (process.StandardOutput.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected extra decoded pixels.");
            return data;
        });
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.BaseStream.Write(jpeg.Bytes); process.StandardInput.Close();
            if (!process.WaitForExit(30000)) throw new IOException("Pixel decoder timed out.");
            if (process.ExitCode != 0 || errors.GetAwaiter().GetResult().Length != 0)
                throw new InvalidDataException("Independent JPEG decoding failed; no artwork published.");
            byte[] rgba = pixels.GetAwaiter().GetResult();
            if (FileHash() != identity) throw new InvalidDataException("Pixel decoder changed during conversion.");
            return (rgba, identity);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
