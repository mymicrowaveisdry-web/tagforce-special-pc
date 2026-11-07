using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Effects;

/// <summary>
/// Code-owned reviewed card metadata, overlaid when a catalog is opened. Converted packages keep the frozen
/// <see cref="TfspCardMetadata.Reviewed"/> baseline that the importer bakes in, so review batches never change
/// pinned cache bytes. This lives outside the Cards assembly on purpose: presentation packages pin the Assets
/// assembly hash, which depends on Cards, so review batches must not change Cards.
/// </summary>
public static class ReviewedCardMetadata
{
    /// <summary>The frozen converter baseline, every reviewed effect-free Normal Monster and the 10C-2/10C-3 batches.</summary>
    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } =
        TfspCardMetadata.Reviewed
            .Concat(TfspNormalMonsters.Ids.Where(id => !TfspCardMetadata.Reviewed.ContainsKey(id))
                .Select(id => new KeyValuePair<uint, CardStaticMetadata>(id, new(CardKind.Monster, CardEffectBinding.None))))
            .Concat(ChainCardFamily.Reviewed)
            .Concat(LifePointFamily.Reviewed)
            .ToFrozenDictionary();

    /// <summary>Full reviewed overlay for the reviewed build; throws for any other source.</summary>
    public static CardCatalog ApplyReviewed(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("Reviewed card metadata requires the reviewed cardinfo source hash.");
        return new(catalog.Source, catalog.Cards.Select(c => Reviewed.TryGetValue(c.GameCardId, out var metadata)
            ? c with { Kind = metadata.Kind, Effects = metadata.Effects } : c));
    }

    /// <summary>Full overlay for the reviewed build; any other source is returned unchanged (its stored metadata stays).</summary>
    public static CardCatalog Overlay(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Source.ContainerSha256 == TfspCardMetadata.CardInfoSha256 ? ApplyReviewed(catalog) : catalog;
    }
}
