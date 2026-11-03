using TagForceSpecial.Effects;
using TagForceSpecial.PC.Decks;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Import;
using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.PC.Game;

public static class SupportedDuelFactory
{
    public static DuelController Create(ValidatedGameCache cache, int deckVariant, uint seed)
        => Create(cache, InitialOpponentDecks.Create(deckVariant), seed);

    public static DuelController Create(ValidatedGameCache cache, DeckDefinition deck, uint seed)
    {
        var selected = SelectedDecks.Validate(cache, InitialDeckPolicy.Create(), deck,
            InitialDeckPolicy.ExampleCollection(), InitialOpponentDecks.Create(0, "local-opponent"),
            InitialDeckPolicy.ExampleCollection("local-opponent"));
        if (selected.Selection is not { } decks)
            throw new InvalidDataException("The selected deck or collection does not match this cache and regulation.");
        var engine = PlayablePreview.CreateEngine(cache.Catalog.Catalog, decks.First.Main, decks.Second.Main, stagedBattle: true);
        return new(engine, cache.Catalog.Catalog, seed: seed);
    }
}
