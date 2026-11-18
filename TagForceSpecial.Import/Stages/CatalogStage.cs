using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Import;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Converts cardinfo and the thumbnail index into a card catalog package and checks it against the trusted profile pins.</summary>
public static class CatalogStage
{
    public const string Id = "catalog";
    public const string Version = "catalog-stage-v1;cards-json-v1";
    private const string CardInfo = "source/cardinfo.ehp", CardImages = "assets/cardh_j.cip";

    public static StageResult Run(StageCache cache, ValidatedGameSource source, string decoderSha256, bool reuse, CancellationToken cancellation)
    {
        var profile = source.Profile;
        if (Mapped(profile, CardInfo) is not { } cardInfo || Mapped(profile, CardImages) is not { } cardImages)
            return new(StageStatus.NotSupported, "This build profile does not map cardinfo and the card image index.");
        string key = StageCache.Key(Id, Version, decoderSha256, profile.Fingerprint);
        var checks = new List<ValidationCheck>();
        if (reuse && cache.TryReuse(Id, key, dir => TryOpen(dir, profile) is not null))
        {
            checks.Add(Pinned(profile, true));
            return new(StageStatus.Reused, "Card catalog reused from a verified earlier import.", Output(cache, key, decoderSha256), checks);
        }
        string directory = cache.Publish(Id, key, staging =>
        {
            using var images = Copy(source, cardImages.Path, cancellation);
            var index = new CardImageIndexReader().ReadIndex(images);
            using var cards = Copy(source, cardInfo.Path, cancellation);
            var catalog = PspCardCatalogImporter.Read(cards, index.MinimumCardId, index.MaximumCardId);
            CardCatalogPackage.Write(Path.Combine(staging, "catalog"), catalog,
                new("tfsp-original", "1", "en", $"cardinfo-sha256:{catalog.Source.ContainerSha256}"));
            if (!PinsMatch(staging, profile))
                throw new InvalidDataException("The converted catalog differs from the trusted profile's pinned output.");
        }, cancellation);
        checks.Add(Pinned(profile, true));
        var opened = Open(directory, profile);
        return new(StageStatus.Succeeded, $"Card catalog converted: {opened.Catalog.Count} records.", Output(cache, key, decoderSha256), checks,
            opened.Catalog.Count, opened.Catalog.Count);
    }

    /// <summary>Opens a stage (or legacy cache) directory holding <c>catalog/</c>, verifies pinned bytes and applies the reviewed overlay.</summary>
    public static LoadedCardCatalog Open(string directory, GameBuildProfile profile)
    {
        if (!PinsMatch(directory, profile)) throw new InvalidDataException("Catalog files differ from the trusted profile pins.");
        var loaded = CardCatalogPackage.Load(Path.Combine(directory, "catalog"));
        if (loaded.Catalog.Source.ContainerSha256 != profile.CatalogSourceSha256)
            throw new InvalidDataException("Catalog belongs to a different original source.");
        // Pinned bytes are verified above; reviewed card metadata is code-owned and overlaid after validation.
        return loaded with { Catalog = Effects.ReviewedCardMetadata.Overlay(loaded.Catalog) };
    }

    public static LoadedCardCatalog? TryOpen(string directory, GameBuildProfile profile)
    {
        try { return Open(directory, profile); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return null; }
    }

    private static bool PinsMatch(string directory, GameBuildProfile profile)
    {
        var pins = profile.CacheFiles.Where(f => f.Path.StartsWith("catalog/", StringComparison.Ordinal)).ToArray();
        return pins.Length > 0 && pins.All(pin =>
        {
            string file = Path.Combine(directory, pin.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file) || new FileInfo(file).Length != pin.Length) return false;
            using var stream = File.OpenRead(file);
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)) == pin.Sha256;
        });
    }

    private static SourceFileIdentity? Mapped(GameBuildProfile profile, string cachePath) =>
        profile.SourceFiles.SingleOrDefault(f => f.CachePath == cachePath);

    private static MemoryStream Copy(ValidatedGameSource source, string path, CancellationToken cancellation)
    {
        var memory = new MemoryStream();
        source.CopyMappedFile(path, memory, cancellation);
        memory.Position = 0;
        return memory;
    }

    private static StageOutput Output(StageCache cache, string key, string decoder)
    {
        string directory = cache.Directory(Id, key);
        return new(Id, cache.RelativeDirectory(Id, key), key, Version + ";assets-" + decoder[..12], StageCache.Inventory(directory));
    }

    private static ValidationCheck Pinned(GameBuildProfile profile, bool passed) => new("catalog.matches-profile-pins", CheckCategory.Normalized,
        passed ? CheckSeverity.Info : CheckSeverity.Error, passed ? CheckStatus.Passed : CheckStatus.Failed, "catalog/cards.json",
        "The converted catalog's bytes equal the trusted profile's pinned output, so conversion is deterministic for this build.",
        $"Profile {profile.BuildId}");
}
