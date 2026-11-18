using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.PC.Game;

public static class SupportedDuelFactory
{
    public static DuelController Create(InstalledGame cache, int deckVariant, uint seed)
        => Create(cache, InitialOpponentDecks.Create(deckVariant), seed);

    /// <summary>Validates each deck under the regulation it names, then plays them with every confirmed card family loaded.</summary>
    public static DuelController Create(InstalledGame cache, DeckDefinition deck, uint seed)
    {
        var opponent = InitialOpponentDecks.Create(0, "local-opponent");
        var catalog = cache.Catalog.Catalog;
        var selected = SelectedDecks.Validate(cache, ConfirmedCardPool.PolicyFor(deck, catalog), deck, ConfirmedCardPool.CollectionFor(deck),
            ConfirmedCardPool.PolicyFor(opponent, catalog), opponent, ConfirmedCardPool.CollectionFor(opponent));
        if (selected.Selection is not { } decks)
            throw new InvalidDataException("The selected deck or collection does not match this cache and regulation.");
        var engine = ConfirmedCardPool.CreateEngine(cache.Catalog.Catalog, decks.First, decks.Second, seed);
        return new(engine, cache.Catalog.Catalog, seed: seed);
    }
}
