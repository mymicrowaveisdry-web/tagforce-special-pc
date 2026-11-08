using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Effects;

/// <summary>Card metadata overlaid when a catalog is opened, so converted packages and Cards stay unchanged.</summary>
public static class ReviewedCardMetadata
{
    /// <summary>The converter baseline plus every Effects family.</summary>
    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } =
        TfspCardMetadata.Reviewed
            .Concat(TfspNormalMonsters.Ids.Where(id => !TfspCardMetadata.Reviewed.ContainsKey(id))
                .Select(id => new KeyValuePair<uint, CardStaticMetadata>(id, new(CardKind.Monster, CardEffectBinding.None))))
            .Concat(ChainCardFamily.Reviewed)
            .Concat(LifePointFamily.Reviewed)
            .Concat(DeckOperationFamily.Reviewed)
            .Concat(RemovalFamily.Reviewed)
            .Concat(GraveyardFamily.Reviewed)
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
