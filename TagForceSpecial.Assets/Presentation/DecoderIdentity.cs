using System.Security.Cryptography;

namespace TagForceSpecial.Assets.Presentation;

/// <summary>SHA-256 of the decoder assembly that import manifests and presentation packages record.
/// Hosts that load assemblies from memory, such as Godot, have no file to hash and receive the value from the launching Workbench.</summary>
public static class DecoderIdentity
{
    private static readonly Lazy<string?> fromFile = new(() =>
    {
        string path = typeof(GimTextureDecoder).Assembly.Location;
        if (string.IsNullOrEmpty(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    });
    private static string? supplied;

    /// <summary>The decoder hash, or throws when this deployment has no assembly file and no launcher supplied one.</summary>
    public static string Sha256 => TryGetSha256(out string? hash) ? hash
        : throw new InvalidOperationException("Decoder assembly identity is unavailable in this deployment.");

    public static bool TryGetSha256([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? hash)
    {
        hash = fromFile.Value ?? Volatile.Read(ref supplied);
        return hash is not null;
    }

    /// <summary>Accepts the launcher's decoder hash. When this process can hash its own decoder file, the two must agree.</summary>
    public static void Supply(string sha256)
    {
        if (sha256 is not { Length: 64 } || !sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("--decoder-sha256 requires a lowercase SHA-256.");
        if (fromFile.Value is { } actual && actual != sha256)
            throw new InvalidOperationException("The launcher's decoder differs from the decoder this game loaded. Rebuild both from the same source.");
        string? previous = Interlocked.CompareExchange(ref supplied, sha256, null);
        if (previous is not null && previous != sha256) throw new InvalidOperationException("A different decoder identity was already supplied.");
    }
}
