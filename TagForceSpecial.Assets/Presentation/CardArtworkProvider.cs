using System.Globalization;

namespace TagForceSpecial.Assets.Presentation;

public sealed record CardArtwork(string AssetKey, int Width, int Height, byte[] Rgba, string AlphaUsage = "straight");

/// <summary>Exact artwork lookup; full artwork never substitutes a thumbnail.</summary>
public sealed class CardArtworkProvider(PresentationPackage package)
{
    public static string FullArtKey(uint cardId) => "card.art.full." + cardId.ToString(CultureInfo.InvariantCulture);
    public static string ThumbnailKey(uint cardId) => "card.thumbnail." + cardId.ToString(CultureInfo.InvariantCulture);

    public CardArtwork ReadFullArt(uint cardId)
    {
        string key = FullArtKey(cardId);
        if (!package.Resources.TryGetValue(key, out var resource))
            throw new KeyNotFoundException("Missing resource: " + key);
        if (resource.Conversion is not (CpjArtworkDecoder.ConversionId or CpjArtworkDecoder.NativeConversionId) || resource.Artwork?.CardId != cardId ||
            resource.Width is null || resource.Height is null)
            throw new InvalidDataException("Full-art resource lacks independent CPJ provenance: " + key);
        return new(key, resource.Width.Value, resource.Height.Value, package.ReadResource(key), "opaque");
    }

    public CardArtwork ReadThumbnail(uint cardId)
    {
        string key = ThumbnailKey(cardId);
        if (!package.Resources.TryGetValue(key, out var resource))
            throw new KeyNotFoundException("Missing resource: " + key);
        if (resource.Conversion is not ("gim-rgba-v1" or CardThumbnailDecoder.ConversionId) || resource.Width is null || resource.Height is null)
            throw new InvalidDataException("Thumbnail is not decoded RGBA: " + key);
        return new(key, resource.Width.Value, resource.Height.Value, package.ReadResource(key));
    }

    internal static bool IsUnsupportedArtKey(string key) =>
        key.StartsWith("card.art.", StringComparison.Ordinal);
}
