namespace TagForceSpecial.Import.Sources;

/// <summary>Measured <see cref="SourceScan.TreeDigest"/> of each supported build's <c>PSP_GAME</c> tree. An extracted directory is supported
/// only when its digest equals the pin, so every file (not only the pinned ones) matches the original image.</summary>
public static class DirectoryPins
{
    /// <summary>Keyed by build profile fingerprint. A profile without an entry cannot be imported from a directory.</summary>
    public static IReadOnlyDictionary<string, string> TreeSha256 { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Measured from the inventory of the supported English v2.1 image (whole-image SHA-256 e99cff57...), 1,512 files.
        [Assets.GameSource.GameBuildProfile.English21.Fingerprint] = "c20e363902a7641c0a3e17fe2c51e56843df8fb07088300ef045789a50b1f20d"
    };
}
