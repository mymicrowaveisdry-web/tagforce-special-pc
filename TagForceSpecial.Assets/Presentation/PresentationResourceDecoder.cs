using System.IO.Compression;
using System.Security.Cryptography;

namespace TagForceSpecial.Assets.Presentation;

internal sealed record DecodedPresentationResource(byte[] Content, string InputSha256,
    int? Width, int? Height, IReadOnlyList<string> Diagnostics, CpjArtworkProvenance? Artwork = null);

/// <summary>CPU data only. Never creates or disposes GPU resources.</summary>
internal static class PresentationResourceDecoder
{
    public static DecodedPresentationResource Decode(SelectedPresentationSource selected, PresentationResourceRequest request,
        string? jpegDecoderExecutable, string? nativeReferenceRoot)
    {
        byte[] stored = selected.Stored;
        if (request.Conversion is PresentationConversion.CpjRgba or PresentationConversion.CpjReferenceRgba)
        {
            var jpeg = CpjArtworkDecoder.Reconstruct(stored, selected.CpjHeader);
            if (jpeg.Width != 256 || jpeg.Height != 256 || selected.Artwork is null)
                throw new InvalidDataException("Full-art dimensions/provenance do not match the verified TFSP source.");
            string jpegHash = Convert.ToHexStringLower(SHA256.HashData(jpeg.Bytes));
            if (request.Conversion == PresentationConversion.CpjReferenceRgba)
            {
                byte[] native = CpjArtworkReferenceProfiles.Read(selected.Artwork.CardId, selected.SourceSha256,
                    Convert.ToHexStringLower(SHA256.HashData(stored)), jpegHash, nativeReferenceRoot);
                return new(native, jpegHash, jpeg.Width, jpeg.Height, [], selected.Artwork with
                {
                    JpegSha256 = jpegHash, PixelDecoderSha256 = CpjArtworkReferenceProfiles.EmulatorSha256,
                    ReferenceEvidenceId = CpjArtworkReferenceProfiles.EvidenceId
                });
            }
            var (rgba, decoder) = CpjArtworkDecoder.DecodePixels(jpeg, jpegDecoderExecutable);
            return new(rgba, jpegHash, jpeg.Width, jpeg.Height, [],
                selected.Artwork with { JpegSha256 = jpegHash, PixelDecoderSha256 = decoder });
        }
        byte[] input = stored;
        if (request.Conversion == PresentationConversion.GimRgba &&
            request.MemberName?.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var compressed = new MemoryStream(stored, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream(); byte[] buffer = new byte[65536]; int count;
            while ((count = gzip.Read(buffer)) != 0)
            {
                if (output.Length + count > PresentationPackage.MaximumResourceBytes)
                    throw new InvalidDataException("Inflated resource exceeds limit.");
                output.Write(buffer, 0, count);
            }
            input = output.ToArray();
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(input));
        if (request.Conversion == PresentationConversion.Stored) return new(input, hash, null, null, []);
        var texture = request.Conversion == PresentationConversion.CardThumbnailRgba ? CardThumbnailDecoder.Decode(input)
            : request.Frame is { } frame ? new GimTextureDecoder().DecodeFrame(input, frame)
            : new GimTextureDecoder().Decode(input, request.PaletteLevel);
        return new(texture.Rgba, hash, texture.Width, texture.Height, texture.Diagnostics);
    }
}
