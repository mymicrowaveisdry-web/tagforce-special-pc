using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Assets.Presentation;

internal sealed record SelectedPresentationSource(string SourceSha256, long Offset, byte[] Stored,
    IReadOnlyList<string> Diagnostics, byte[]? CpjHeader = null, CpjArtworkProvenance? Artwork = null);

/// <summary>Bounded source selection only; format decoding and screen mappings are separate.</summary>
internal static class PresentationSourceReader
{
    public static SelectedPresentationSource Read(ValidatedGameSource source, IGameFiles files, PresentationResourceRequest request)
    {
        if (request.Conversion is PresentationConversion.CpjRgba or PresentationConversion.CpjReferenceRgba) return CpjArtworkSourceReader.Read(source, files, request);
        var entry = files.Find(request.SourcePath);
        if (entry is not { Directory: false } || entry.Length > PresentationPackage.MaximumResourceBytes)
            throw new InvalidDataException("Missing or oversized source: " + request.AssetKey);
        byte[] raw = new byte[checked((int)entry.Length)];
        using (var input = files.OpenRead(entry)) input.ReadExactly(raw);
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
        // Inspection reads only in-bounds members: an archive whose index also lists a payload past its end (database/charalist.ehp,
        // strTbl_s.bin) still yields every other member; a requested member that was set aside is reported missing.
        var index = request.ArchiveMode == EhpReadMode.InspectStoredRanges
            ? new EhpReader().InspectBoundedMembers(archive).BoundedIndex
            : new EhpReader().ReadIndex(archive, request.ArchiveMode);
        var member = index.Entries.SingleOrDefault(e => e.Name == request.MemberName)
            ?? throw new InvalidDataException("Required EHP member is missing: " + request.AssetKey);
        if (member.Length > PresentationPackage.MaximumResourceBytes)
            throw new InvalidDataException("Oversized EHP member.");
        archive.Position = member.Offset;
        byte[] stored = new byte[checked((int)member.Length)]; archive.ReadExactly(stored);
        return new(sourceHash, member.Offset, stored, index.Diagnostics);
    }
}
