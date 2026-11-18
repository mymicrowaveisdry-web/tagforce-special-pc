namespace TagForceSpecial.Import.Sources;

/// <param name="Format">Short format identifier, or "unknown".</param>
/// <param name="Decoder">Repository component that reads this format, or null when none exists.</param>
/// <param name="Reason">Why the format was assigned (the matched signature) or why it stayed unknown.</param>
public sealed record FileClass(string Format, string Category, string? Decoder, string Reason);

/// <summary>Classifies original files by leading signature bytes, never by file name alone. Extensions only annotate an unknown result.</summary>
public static class FileClassifier
{
    public const int HeaderBytes = 32;

    private static readonly (byte[] Magic, int At, FileClass Class)[] Signatures =
    [
        ("\0PSF"u8.ToArray(), 0, new("psf", "metadata", "ParamSfo", "PSF table signature")),
        ("\u007fELF"u8.ToArray(), 0, new("elf", "executable", null, "ELF signature")),
        ("~PSP"u8.ToArray(), 0, new("psp-encrypted", "executable", null, "~PSP encrypted module signature; decryption is outside the importer")),
        ("~SCE"u8.ToArray(), 0, new("sce-wrapped", "executable", null, "~SCE signed module header; the wrapped module is not unpacked by the importer")),
        ("\0PBP"u8.ToArray(), 0, new("pbp", "executable", null, "PBP container signature")),
        ("MIG.00.1PSP"u8.ToArray(), 0, new("gim", "texture", "GimTextureDecoder", "GIM texture signature")),
        ("CIP\u001a"u8.ToArray(), 0, new("cip", "archive", "CardImageIndexReader", "CIP card image archive signature")),
        ("CPJ\u001a"u8.ToArray(), 0, new("cpj", "archive", "CpjArtworkDecoder", "CPJ card artwork archive signature")),
        ("CPM\u001a"u8.ToArray(), 0, new("cpm", "archive", "CardImageIndexReader", "CPM card image archive signature (index only)")),
        ([0x1f, 0x8b], 0, new("gzip", "compressed", null, "gzip signature; inner content needs separate classification")),
        ("PSMF"u8.ToArray(), 0, new("pmf", "movie", null, "PSMF movie signature")),
        ("RIFF"u8.ToArray(), 0, new("riff", "audio", "PcmWaveReader", "RIFF signature (WAV/AT3 container)")),
        ("@UTF"u8.ToArray(), 0, new("cri-utf", "audio", "CriUtfTable", "CRI @UTF table signature (ACB cue sheet)")),
        ("AFS2"u8.ToArray(), 0, new("afs2", "audio", "Adx2AudioReader", "AFS2 waveform bank signature")),
        ("EHP\u0003"u8.ToArray(), 0, new("ehp", "archive", "EhpReader", "EHP version 3 signature")),
        ("PGF0"u8.ToArray(), 4, new("pgf", "font", "PgfFont", "PGF font signature at offset 4")),
        ([0x89, (byte)'P', (byte)'N', (byte)'G'], 0, new("png", "image", null, "PNG signature")),
        ([0xff, 0xd8, 0xff], 0, new("jpeg", "image", null, "JPEG signature")),
    ];

    public static FileClass Classify(string path, ReadOnlySpan<byte> header, long length)
    {
        if (length == 0) return new("empty", "unknown", null, "Zero-length file");
        foreach (var (magic, at, value) in Signatures)
            if (header.Length >= at + magic.Length && header.Slice(at, magic.Length).SequenceEqual(magic)) return value;
        if (header.Length >= 8 && header.ToArray().All(b => b is 0x09 or 0x0a or 0x0d or >= 0x20 and <= 0x7e))
            return new("text", "text", null, "Printable ASCII header; the text's structure is not decoded");
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return new("unknown", "unknown", null, extension.Length == 0
            ? "No known signature"
            : $"No known signature (extension .{extension} is not used as evidence)");
    }
}
