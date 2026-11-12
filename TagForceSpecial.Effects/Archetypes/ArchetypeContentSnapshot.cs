using System.Collections.Frozen;

namespace TagForceSpecial.Effects.Archetypes;

/// <summary>Immutable reviewed static content compiled from an explicit pack selection, without duel-state identity effects.</summary>
public sealed class ArchetypeContentSnapshot : IArchetypeRegistry, IArchetypeMembership
{
    private readonly FrozenDictionary<ArchetypeId, ArchetypeDefinition> definitions;
    private readonly FrozenDictionary<SourceCardId, ArchetypeCardFacts> cards;
    private readonly FrozenDictionary<SourceCardId, FrozenSet<ArchetypeId>> membership;
    private readonly FrozenDictionary<SourceCardId, IReadOnlyList<ArchetypeId>> forward;
    private readonly FrozenDictionary<SourceCardId, IReadOnlyList<ArchetypeId>> direct;
    private readonly FrozenDictionary<ArchetypeId, IReadOnlyList<SourceCardId>> reverse;
    private readonly IReadOnlyList<ArchetypeDefinition> orderedDefinitions;
    public ArchetypeContentProfile Profile { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<ArchetypePackDocument> Packs { get; }
    public IReadOnlyList<ArchetypeCardFacts> Cards { get; }

    internal ArchetypeContentSnapshot(ArchetypeContentProfile profile, string fingerprint,
        IReadOnlyList<ArchetypePackDocument> packs, IReadOnlyList<ArchetypeCardFacts> cardFacts,
        IReadOnlyList<ArchetypeDefinition> archetypes, Dictionary<SourceCardId, HashSet<ArchetypeId>> declared,
        Dictionary<SourceCardId, HashSet<ArchetypeId>> expanded)
    {
        Profile = profile; Fingerprint = fingerprint; Packs = packs; Cards = cardFacts;
        orderedDefinitions = archetypes;
        definitions = archetypes.ToFrozenDictionary(d => d.Id);
        cards = cardFacts.ToFrozenDictionary(c => c.CardId);
        membership = expanded.ToFrozenDictionary(p => p.Key, p => p.Value.ToFrozenSet());
        static IReadOnlyList<ArchetypeId> Ordered(IEnumerable<ArchetypeId> ids) =>
            Array.AsReadOnly(ids.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray());
        forward = expanded.ToFrozenDictionary(p => p.Key, p => Ordered(p.Value));
        direct = declared.ToFrozenDictionary(p => p.Key, p => Ordered(p.Value));
        var members = archetypes.ToDictionary(d => d.Id, _ => new List<SourceCardId>());
        foreach (var (card, ids) in expanded)
            foreach (var id in ids) members[id].Add(card);
        reverse = members.ToFrozenDictionary(p => p.Key, p => (IReadOnlyList<SourceCardId>)
            Array.AsReadOnly(p.Value.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray()));
    }

    public static ArchetypeContentSnapshot Compile(ArchetypeContentProfile profile,
        IEnumerable<ArchetypePackDocument> packs, IEnumerable<ArchetypeCardFacts> cards) =>
        ArchetypeSnapshotCompiler.Compile(profile, packs, cards);

    public ArchetypeDefinition? Get(ArchetypeId id) => definitions.GetValueOrDefault(id);
    public IReadOnlyList<ArchetypeDefinition> GetAll() => orderedDefinitions;
    public ArchetypeCardFacts? GetCard(SourceCardId id) => cards.GetValueOrDefault(id);
    public bool IsMember(SourceCardId card, ArchetypeId archetype) => membership.TryGetValue(card, out var ids) && ids.Contains(archetype);
    public IReadOnlyList<ArchetypeId> GetArchetypes(SourceCardId card) => forward.GetValueOrDefault(card) ?? Array.Empty<ArchetypeId>();
    public IReadOnlyList<ArchetypeId> GetDirectArchetypes(SourceCardId card) => direct.GetValueOrDefault(card) ?? Array.Empty<ArchetypeId>();
    public IReadOnlyList<SourceCardId> GetMembers(ArchetypeId archetype) => reverse.GetValueOrDefault(archetype) ?? Array.Empty<SourceCardId>();

    public IReadOnlyList<SourceCardId> GetMembers(ArchetypeId archetype, ArchetypeMemberFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.Validate();
        return Array.AsReadOnly(GetMembers(archetype).Where(id => filter.Matches(cards[id])).ToArray());
    }
}
