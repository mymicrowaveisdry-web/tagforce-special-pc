using System.Reflection;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Import;

/// <summary>Versions recorded in manifests: the importer's own version and the SHA-256 of the decoder assembly, which invalidates caches when it changes.</summary>
public static class ToolIdentity
{
    public static string ImporterVersion { get; } =
        typeof(ToolIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static string DecoderSha256 => DecoderIdentity.Sha256;
}
