using System.Globalization;
using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects.Archetypes;

/// <summary>Embedded reviewed semantic content for existing effects, independent of the historical native table.</summary>
public static class ClassicArchetypes
{
    private sealed record Content(ArchetypeContentSnapshot Snapshot, ArchetypeIdentityResolver Resolver);
    private static readonly Lazy<Content> Selected = new(Load);
    public static ArchetypeContentSnapshot Snapshot => Selected.Value.Snapshot;
    public static ArchetypeIdentityResolver Resolver => Selected.Value.Resolver;

    public static CardFilter Filter(string archetypeId, bool monstersOnly = false) => monstersOnly
        ? new AllOfFilter(new AnyMonsterFilter(), new ArchetypeFilter(archetypeId, Resolver))
        : new ArchetypeFilter(archetypeId, Resolver);

    public static IReadOnlyList<uint> GetMembers(string archetypeId, ArchetypeMemberFilter? filter = null)
    {
        var id = new ArchetypeId(archetypeId);
        var members = filter is null ? Snapshot.GetMembers(id) : Snapshot.GetMembers(id, filter);
        return Array.AsReadOnly(members.Select(c => uint.Parse(c.Value.AsSpan(4), CultureInfo.InvariantCulture)).Order().ToArray());
    }

    private static Content Load()
    {
        string Read(string name)
        {
            using var stream = typeof(ClassicArchetypes).Assembly.GetManifestResourceStream(
                "TagForceSpecial.Effects.Archetypes.Classic." + name + ".json")
                ?? throw new InvalidDataException("Missing embedded archetype document: " + name);
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var inventory = JsonSerializer.Deserialize<uint[]>(Read("identity-ids")) ?? throw new InvalidDataException("Missing identity inventory.");
        if (inventory.Any(id => id == 0) || inventory.Distinct().Count() != inventory.Length)
            throw new InvalidDataException("Classic identity inventory requires distinct nonzero original catalog IDs.");
        SourceCardId Source(uint id) => new("tfs:" + id.ToString(CultureInfo.InvariantCulture));
        var mappings = inventory.ToDictionary(id => id, Source);
        var catalog = ArchetypePackJson.ReadDocument<ArchetypeCardCatalogDocument>(Read("catalog"));
        if (catalog.SchemaVersion != 1 || catalog.Cards.Any(c => c.Review != ArchetypeReviewStatus.Reviewed ||
            !mappings.TryGetValue(c.CatalogCardId, out var id) || c.Facts.CardId != id))
            throw new InvalidDataException("Classic member metadata must be reviewed and use original catalog identities.");
        var metadata = catalog.Cards.ToDictionary(c => c.Facts.CardId, c => c.Facts);
        // Unrelated identities are known, while their metadata is deliberately unspecified in this semantic projection.
        var facts = mappings.Values.Select(id => metadata.GetValueOrDefault(id) ?? new ArchetypeCardFacts(id, CardKind.Unknown)).ToArray();
        var pack = ArchetypePackJson.Read(Read("manifest"), Read("definitions"), Read("memberships"),
            Read("identity-rules"), Read("native-mappings"), new(facts.Select(c => c.CardId).ToArray(), []));
        var snapshot = ArchetypeContentSnapshot.Compile(ArchetypeContentProfile.TfsClassic, [pack], facts);
        return new(snapshot, new(snapshot, mappings));
    }
}
