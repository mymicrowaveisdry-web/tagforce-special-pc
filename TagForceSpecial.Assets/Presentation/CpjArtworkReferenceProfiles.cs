using System.Security.Cryptography;

namespace TagForceSpecial.Assets.Presentation;

/// <summary>Text-only pins for traced CPJ decoder output; not a JPEG codec.</summary>
public static class CpjArtworkReferenceProfiles
{
    public const uint CardId = 9863;
    public const string EvidenceId = "phase2.card-art.alexandrite-9863.native-v1";
    public const string ArchiveSha256 = "28db1caa6b8effa1603fdfbc199db1d8a47116b69e63a42cbdd671b9b9379c5e";
    public const string SlotSha256 = "08e4597fdae50344b6ec19c3cfa0c246aaf2a234d478d0f244fe96a9869128fa";
    public const string JpegSha256 = "e5096d2d3889c0574225e356af9e08f5fcb425d8471c93a5048b445c5bbb4c91";
    public const string RgbaSha256 = "4478e1577353b38b1a7bf7e308a976e3501a7f86ddb0f54d081cb2eb9104bc01";
    public const string EmulatorSha256 = "775a0ded7d0c0602f46c3aeed659228d629e98ed77d9e2bd3818c44eb30de85a";
    public const string FileName = "card-9863.native.rgba";

    public static byte[] Read(uint cardId, string archiveHash, string slotHash, string jpegHash, string? privateRoot)
    {
        if (cardId != CardId || archiveHash != ArchiveSha256 || slotHash != SlotSha256 || jpegHash != JpegSha256)
            throw new KeyNotFoundException("No matching native full-art reference; no decoder or thumbnail fallback is permitted.");
        if (string.IsNullOrWhiteSpace(privateRoot) || !Path.IsPathFullyQualified(privateRoot))
            throw new InvalidDataException("An explicit private native-reference directory is required.");
        string root = Path.GetFullPath(privateRoot);
        string path = Path.Combine(root, FileName);
        for (string? at = path; at is not null; at = Path.GetDirectoryName(at))
        {
            if ((File.Exists(at) || Directory.Exists(at)) && (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0 ||
                File.Exists(Path.Combine(at, ".git")) || Directory.Exists(Path.Combine(at, ".git")))
                throw new InvalidDataException("Native reference files must remain outside Git and linked paths.");
        }
        using var file = File.OpenRead(path);
        if (file.Length != 256 * 256 * 4) throw new InvalidDataException("Native full-art reference length mismatch.");
        byte[] data = new byte[256 * 256 * 4]; file.ReadExactly(data);
        if (Convert.ToHexStringLower(SHA256.HashData(data)) != RgbaSha256)
            throw new InvalidDataException("Native full-art reference pixel hash mismatch.");
        return data;
    }

    internal static bool Matches(PresentationResource resource) => resource.Artwork is { } artwork &&
        artwork.CardId == CardId && artwork.ReferenceEvidenceId == EvidenceId &&
        resource.SourcePath == "PSP_GAME/USRDIR/card/cardj256_j.cip" &&
        resource.SourceSha256 == ArchiveSha256 && resource.StoredSha256 == SlotSha256 &&
        resource.InputSha256 == JpegSha256 && resource.ContentSha256 == RgbaSha256 &&
        resource.Offset == 204126208 && resource.StoredLength == 28672 &&
        artwork.PixelDecoderSha256 == EmulatorSha256;
}
