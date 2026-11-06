using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

internal sealed record SelectedPresentationSource(string SourceSha256, long Offset, byte[] Stored,
    IReadOnlyList<string> Diagnostics, byte[]? CpjHeader = null, CpjArtworkProvenance? Artwork = null);

/// <summary>Bounded source selection only; format decoding and screen mappings are separate.</summary>
internal static class PresentationSourceReader
{
    public static SelectedPresentationSource Read(ValidatedGameSource source, Iso9660Source iso, PresentationResourceRequest request)
    {
        if (request.Conversion is PresentationConversion.CpjRgba or PresentationConversion.CpjReferenceRgba) return CpjArtworkSourceReader.Read(source, iso, request);
        var image = source.Image;
        var entry = iso.Find(request.SourcePath);
        if (entry is not { Directory: false } || entry.Length > PresentationPackage.MaximumResourceBytes)
            throw new InvalidDataException("Missing or oversized source: " + request.AssetKey);
        image.Position = entry.Offset;
        byte[] raw = new byte[checked((int)entry.Length)]; image.ReadExactly(raw);
        string sourceHash = Convert.ToHexStringLower(SHA256.HashData(raw));
        if (request.Conversion == PresentationConversion.CardThumbnailRgba)
        {
            // One indexed card slot of cardh_j.cip: stored slot bytes only, read through the existing CIP index reader.
            using var cip = new MemoryStream(raw, writable: false);
            var slot = new CardImageIndexReader().ReadIndex(cip).Entries.SingleOrDefault(e => e.CardId == request.CardId)
                ?? throw new InvalidDataException("Card has no thumbnail slot: " + request.AssetKey);
            return new(sourceHash, slot.Offset, raw.AsSpan(checked((int)slot.Offset), checked((int)slot.StoredSlotBytes)).ToArray(), []);
        }
        if (request.MemberName is null) return new(sourceHash, 0, raw, []);
        using var archive = new MemoryStream(raw, writable: false);
        var index = new EhpReader().ReadIndex(archive, request.ArchiveMode);
        var member = index.Entries.SingleOrDefault(e => e.Name == request.MemberName)
            ?? throw new InvalidDataException("Required EHP member is missing: " + request.AssetKey);
        if (member.Length > PresentationPackage.MaximumResourceBytes)
            throw new InvalidDataException("Oversized EHP member.");
        archive.Position = member.Offset;
        byte[] stored = new byte[checked((int)member.Length)]; archive.ReadExactly(stored);
        return new(sourceHash, member.Offset, stored, index.Diagnostics);
    }
}
