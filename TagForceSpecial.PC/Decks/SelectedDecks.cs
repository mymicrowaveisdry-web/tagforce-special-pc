using TagForceSpecial.Core.Decks;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.PC.Decks;

public sealed class ValidatedDeckSelection
{
    public ValidatedGameCache Cache { get; }
    public DeckDefinition First { get; }
    public DeckDefinition Second { get; }
    internal ValidatedDeckSelection(ValidatedGameCache cache, DeckDefinition first, DeckDefinition second)
        => (Cache, First, Second) = (cache, first, second);
}
public sealed record DeckSelectionValidation(ValidatedDeckSelection? Selection, DeckValidation First, DeckValidation Second)
{
    public bool IsValid => Selection is not null;
}

/// <summary>Cache and both selected decks must agree before an application can hand their IDs to the duel engine.</summary>
public static class SelectedDecks
{
    public static DeckSelectionValidation Validate(ValidatedGameCache cache, DeckLegalityPolicy policy,
        DeckDefinition first, PlayerCollection? firstCollection, DeckDefinition second, PlayerCollection? secondCollection)
    {
        var a = Check(first, firstCollection); var b = Check(second, secondCollection);
        return new(a.CanPlay && b.CanPlay ? new(cache, first, second) : null, a, b);

        DeckValidation Check(DeckDefinition deck, PlayerCollection? collection)
        {
            var result = DeckValidator.Validate(deck, cache.Catalog.Catalog, policy, collection);
            var identity = cache.Catalog.Manifest.Identity;
            if (deck.SourceBuild == cache.SourceSha256 && deck.CatalogId == identity.CatalogId && deck.CatalogRevision == identity.Revision)
                return result;
            return new(result.Legality == DeckLegality.Illegal ? DeckLegality.Illegal : DeckLegality.Indeterminate,
                Array.AsReadOnly(result.Diagnostics.Append(new DeckDiagnostic("SelectedCacheMismatch", null, null, true,
                    "The selected deck must use this validated cache's source and catalog revision.")).OrderBy(d => d.Code, StringComparer.Ordinal).ToArray()));
        }
    }
}
