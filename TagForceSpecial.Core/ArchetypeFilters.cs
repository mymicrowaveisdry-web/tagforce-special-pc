namespace TagForceSpecial.Core;

/// <summary>Immutable semantic content selected for an authored filter or a duel.</summary>
public interface ICardArchetypeResolver
{
    string Fingerprint { get; }
    bool Contains(string archetypeId);
    bool IsMember(CardIdentityState card, string archetypeId);
}

/// <summary>Matches effective membership; kind, Level and Rank restrictions belong in separate filters.</summary>
public sealed record ArchetypeFilter : CardFilter
{
    public string ArchetypeId { get; }
    public ICardArchetypeResolver Resolver { get; }
    public ArchetypeFilter(string archetypeId, ICardArchetypeResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (string.IsNullOrWhiteSpace(archetypeId) || !resolver.Contains(archetypeId))
            throw new ArgumentException("The selected content must define the archetype.", nameof(archetypeId));
        ArchetypeId = archetypeId; Resolver = resolver;
    }
}

public sealed partial class DuelEngine
{
    private readonly ICardArchetypeResolver? archetypeResolver;
    private bool MatchesArchetype(CardInstance card, ArchetypeFilter filter)
    {
        var resolver = archetypeResolver ?? filter.Resolver;
        if (!resolver.Contains(filter.ArchetypeId))
            throw new InvalidOperationException("The duel's selected content does not define an authored archetype.");
        return GetCardIdentityState(IdentityState, card) is { } context && resolver.IsMember(context, filter.ArchetypeId);
    }
}
