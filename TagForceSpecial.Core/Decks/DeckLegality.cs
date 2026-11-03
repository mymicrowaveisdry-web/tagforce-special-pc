using System.Collections.Frozen;
using TagForceSpecial.Cards;

namespace TagForceSpecial.Core.Decks;

public enum DeckLegality { Legal, Illegal, Indeterminate }
public sealed record DeckDiagnostic(string Code, DeckSection? Section, uint? CardId, bool Unknown, string Detail);
public sealed record DeckValidation(DeckLegality Legality, IReadOnlyList<DeckDiagnostic> Diagnostics)
{
    public bool CanPlay => Legality == DeckLegality.Legal;
}

/// <summary>Null fields mean unreviewed, never unrestricted. Alias groups are explicit across artwork/card IDs.</summary>
public sealed record DeckCardRule(uint CardId, DeckPlacements? Placements, string? CopyGroup, int? BanlistLimit);

public sealed class DeckLegalityPolicy
{
    public string Id { get; }
    public string Revision { get; }
    public string SourceBuild { get; }
    public string CatalogId { get; }
    public string CatalogRevision { get; }
    public string? CatalogSourceSha256 { get; }
    public string Evidence { get; }
    public IReadOnlyDictionary<uint, DeckCardRule> Cards { get; }
    public int MinimumMain { get; }
    public int MaximumMain { get; }
    public int MaximumExtra { get; }
    public int MaximumSide { get; }

    public DeckLegalityPolicy(string id, string revision, string sourceBuild, string catalogId, string catalogRevision,
        string? catalogSourceSha256, string evidence, IEnumerable<DeckCardRule> cards,
        int minimumMain = 40, int maximumMain = 60, int maximumExtra = 15, int maximumSide = 15)
    {
        var snapshot = cards.ToArray();
        if (new[] { id, revision, sourceBuild, catalogId, catalogRevision, evidence }.Any(string.IsNullOrWhiteSpace) ||
            minimumMain < 0 || maximumMain < minimumMain || maximumExtra < 0 || maximumSide < 0 ||
            snapshot.Any(c => c is null || c.CardId == 0 || c.BanlistLimit is < 0 or > 3 ||
                c.CopyGroup is { } key && string.IsNullOrWhiteSpace(key) ||
                c.Placements is { } placements && (placements & ~(DeckPlacements.Main | DeckPlacements.Extra | DeckPlacements.Side)) != 0) ||
            snapshot.Select(c => c.CardId).Distinct().Count() != snapshot.Length ||
            snapshot.Where(c => c.CopyGroup is not null).GroupBy(c => c.CopyGroup).Any(g => g.Select(c => c.BanlistLimit).Distinct().Count() != 1))
            throw new ArgumentException("Explicit consistent policy identity, bounds, alias groups and restrictions are required.");
        Id = id; Revision = revision; SourceBuild = sourceBuild; CatalogId = catalogId; CatalogRevision = catalogRevision;
        CatalogSourceSha256 = catalogSourceSha256; Evidence = evidence; MinimumMain = minimumMain; MaximumMain = maximumMain;
        MaximumExtra = maximumExtra; MaximumSide = maximumSide; Cards = snapshot.ToFrozenDictionary(c => c.CardId);
    }
}

public static class DeckValidator
{
    public static DeckValidation Validate(DeckDefinition deck, CardCatalog catalog, DeckLegalityPolicy policy, PlayerCollection? collection)
    {
        List<DeckDiagnostic> issues = [];
        if (deck.SchemaVersion != DeckDefinition.CurrentSchema) return Unknown("UnknownDeckSchema", "Deck schema is not supported.");
        if (deck.PolicyId != policy.Id || deck.PolicyRevision != policy.Revision) return Unknown("UnknownPolicy", "Requested regulation/version is not the selected reviewed policy.");
        if (deck.SourceBuild != policy.SourceBuild || deck.CatalogId != policy.CatalogId || deck.CatalogRevision != policy.CatalogRevision ||
            policy.CatalogSourceSha256 is { } hash && catalog.Source.ContainerSha256 != hash)
            return Unknown("CatalogOrBuildMismatch", "Deck, catalog and policy must identify the same reviewed build/version.");
        if (deck.Main.Count < policy.MinimumMain || deck.Main.Count > policy.MaximumMain) Add("MainDeckSize", DeckSection.Main, null, false, $"Main requires {policy.MinimumMain}–{policy.MaximumMain} cards; found {deck.Main.Count}.");
        if (deck.Extra.Count > policy.MaximumExtra) Add("ExtraDeckSize", DeckSection.Extra, null, false, $"Extra permits at most {policy.MaximumExtra} cards.");
        if (deck.Side.Count > policy.MaximumSide) Add("SideDeckSize", DeckSection.Side, null, false, $"Side permits at most {policy.MaximumSide} cards.");
        bool ownershipKnown = collection is not null && collection.SchemaVersion == 1 && collection.OwnerId == deck.OwnerId &&
            collection.CatalogId == deck.CatalogId && collection.CatalogRevision == deck.CatalogRevision && collection.SourceBuild == deck.SourceBuild;
        if (!ownershipKnown) Add("OwnershipUnknown", null, null, true, "A matching versioned owner collection is required.");
        var knownIds = catalog.Cards.Select(c => c.GameCardId).ToHashSet();
        var groups = new Dictionary<string, (int Count, int? Limit)>(StringComparer.Ordinal);
        foreach (var group in deck.Entries().GroupBy(e => e.CardId).OrderBy(g => g.Key))
        {
            uint id = group.Key; int count = group.Count();
            if (id == 0 || !knownIds.Contains(id)) Add("UnknownCardId", null, id, false, "Card ID is not in the selected catalog.");
            if (count > 3) Add("CopyLimitExceeded", null, id, false, "At most three copies of an ID may appear across Main/Extra/Side.");
            if (ownershipKnown && collection!.Counts.GetValueOrDefault(id) < count) Add("NotOwned", null, id, false, "Collection does not contain the total selected quantity.");
            if (!policy.Cards.TryGetValue(id, out var rule))
            { Add("CardRestrictionsUnknown", null, id, true, "Placement, aliases and banlist restriction are not reviewed for this policy."); continue; }
            if (rule.CopyGroup is null) Add("CopyGroupUnknown", null, id, true, "Original-era same-card/alias accounting is unresolved.");
            else
            {
                var current = groups.GetValueOrDefault(rule.CopyGroup);
                groups[rule.CopyGroup] = (current.Count + count, rule.BanlistLimit);
            }
            if (rule.BanlistLimit is null) Add("BanlistRestrictionUnknown", null, id, true, "No unrestricted default is assumed for an unknown restriction.");
            else if (count > rule.BanlistLimit) Add("BanlistLimitExceeded", null, id, false, $"Selected policy permits {rule.BanlistLimit} copies across all sections.");
            foreach (var section in group.Select(e => e.Section).Distinct().Order())
            {
                if (rule.Placements is null) Add("PlacementUnknown", section, id, true, "Initial deck placement is unresolved.");
                else if (!rule.Placements.Value.HasFlag(section switch { DeckSection.Main => DeckPlacements.Main, DeckSection.Extra => DeckPlacements.Extra, _ => DeckPlacements.Side }))
                    Add("WrongSection", section, id, false, "Card is not permitted in this initial deck section.");
            }
        }
        foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            if (group.Value.Count > Math.Min(3, group.Value.Limit ?? 3)) Add("AliasGroupLimitExceeded", null, null, false, $"Copy group '{group.Key}' exceeds its combined limit.");
        var sorted = issues.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Section).ThenBy(i => i.CardId).ThenBy(i => i.Detail, StringComparer.Ordinal).ToArray();
        return new(sorted.Any(i => !i.Unknown) ? DeckLegality.Illegal : sorted.Length != 0 ? DeckLegality.Indeterminate : DeckLegality.Legal, Array.AsReadOnly(sorted));

        void Add(string code, DeckSection? section, uint? id, bool unknown, string detail) => issues.Add(new(code, section, id, unknown, detail));
        DeckValidation Unknown(string code, string detail) => new(DeckLegality.Indeterminate,
            Array.AsReadOnly(new[] { new DeckDiagnostic(code, null, null, true, detail) }));
    }
}
