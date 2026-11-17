using System.Reflection;
using System.Security.Cryptography;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Import;

/// <summary>Versions recorded in manifests: the importer's own version and the SHA-256 of the decoder assembly, which invalidates caches when it changes.</summary>
public static class ToolIdentity
{
    private static readonly Lazy<string> decoder = new(() =>
    {
        string path = typeof(GimTextureDecoder).Assembly.Location;
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Decoder assembly identity is unavailable in this deployment.");
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    });

    public static string ImporterVersion { get; } =
        typeof(ToolIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static string DecoderSha256 => decoder.Value;
}
