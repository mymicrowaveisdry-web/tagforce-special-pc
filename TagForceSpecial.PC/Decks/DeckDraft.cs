using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Effects;

namespace TagForceSpecial.PC.Decks;

public enum DeckFilter { All, Monster, Spell }

/// <summary>Editable ordered card IDs only; no live-duel instances or state references.</summary>
public sealed class DeckDraft
{
    private readonly DeckDefinition identity;
    private readonly Dictionary<DeckSection, List<uint>> sections;
    public CardCatalog Catalog { get; }
    public string Name { get; set; }
    public string Search { get; set; } = "";
    public DeckFilter Filter { get; set; }
    public DeckSection Section { get; set; }
    public DeckDraft(CardCatalog catalog, DeckDefinition deck)
    {
        Catalog = catalog; identity = deck; Name = deck.DeckId;
        sections = new() { [DeckSection.Main] = deck.Main.ToList(), [DeckSection.Extra] = deck.Extra.ToList(), [DeckSection.Side] = deck.Side.ToList() };
    }
    public IReadOnlyList<uint> Cards(DeckSection section) => sections[section].AsReadOnly();
    public int Count(uint id, DeckSection? section = null) => section is { } s ? sections[s].Count(c => c == id) : sections.Values.Sum(cards => cards.Count(c => c == id));
    public bool Add(uint id)
    {
        if (!PlayablePreview.Rules.ContainsKey(id) || sections[Section].Count >= 100) return false;
        sections[Section].Add(id); return true; // Invalid drafts are visible/saveable; launch remains gated.
    }
    public bool Remove(uint id) => sections[Section].Remove(id);
    public void Clear() { foreach (var section in sections.Values) section.Clear(); }
    public IReadOnlyList<CardDefinition> VisibleCards() => Array.AsReadOnly(PlayablePreview.Cards
        .Select(c => Catalog[c.CardId]).Where(c => c.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            c.GameCardId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(Search, StringComparison.Ordinal))
        .Where(c => Filter == DeckFilter.All || PlayablePreview.Rules[c.GameCardId].Category == (Filter == DeckFilter.Monster ? CardCategory.Monster : CardCategory.Spell))
        .OrderBy(c => c.Name, StringComparer.Ordinal).ToArray());
    public DeckDefinition Snapshot(string? revision = null)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 48 || Name.Any(char.IsControl)) throw new InvalidDataException("Use a deck name of 1 to 48 characters.");
        return new(Name, revision ?? identity.Revision, identity.OwnerId, identity.CatalogId, identity.CatalogRevision,
            identity.SourceBuild, identity.PolicyId, identity.PolicyRevision, sections[DeckSection.Main],
            sections[DeckSection.Extra], sections[DeckSection.Side], identity.SchemaVersion);
    }
    public DeckValidation Validate() => DeckValidator.Validate(Snapshot(), Catalog, InitialDeckPolicy.Create(), InitialDeckPolicy.ExampleCollection());
}
