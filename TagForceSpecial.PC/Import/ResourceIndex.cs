using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.PC.Import;

public sealed record ResourceMapping(uint CardId, string Source, long Offset, long Bytes);
public sealed record ResourceIndex(int SchemaVersion, string Build, string SourceSha256, string Converter,
    IReadOnlyList<ResourceMapping> Cards, IReadOnlyList<string> ContentGaps)
{
    public static ResourceIndex Create(string cache)
    {
        using var input = File.OpenRead(Path.Combine(cache, "assets/cardh_j.cip"));
        var slots = new CardImageIndexReader().ReadIndex(input);
        return new(1, GameBuildProfile.English21.BuildId, GameBuildProfile.English21.ImageSha256, "cip-slots-v1",
            Array.AsReadOnly(slots.Entries.Select(c => new ResourceMapping(c.CardId,
                "PSP_GAME/USRDIR/card/cardh_j.cip", c.Offset, c.StoredSlotBytes)).ToArray()),
            Array.AsReadOnly(new[] { "Card texture decoding is not yet available.", "Original fonts, audio and dialogue scenes are not imported." }));
    }
    public string Fingerprint() => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
}
