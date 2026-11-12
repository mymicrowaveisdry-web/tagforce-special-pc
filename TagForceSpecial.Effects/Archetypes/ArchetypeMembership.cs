using TagForceSpecial.Cards;

namespace TagForceSpecial.Effects.Archetypes;

public interface IArchetypeRegistry
{
    ArchetypeDefinition? Get(ArchetypeId id);
    IReadOnlyList<ArchetypeDefinition> GetAll();
}

public interface IArchetypeMembership
{
    bool IsMember(SourceCardId card, ArchetypeId archetype);
    IReadOnlyList<ArchetypeId> GetArchetypes(SourceCardId card);
    IReadOnlyList<SourceCardId> GetMembers(ArchetypeId archetype);
}

/// <summary>Explicit catalog facts; Level and Rank are separate and do not imply archetype membership.</summary>
public sealed record ArchetypeCardFacts(SourceCardId CardId, CardKind Kind,
    uint? Level = null, uint? Rank = null, bool IsToken = false);

/// <summary>Narrows a static member query without altering the underlying membership index.</summary>
public sealed record ArchetypeMemberFilter(CardKind? Kind = null, uint? MinLevel = null, uint? MaxLevel = null,
    uint? MinRank = null, uint? MaxRank = null, bool? IsToken = null)
{
    internal void Validate()
    {
        if (Kind is { } kind && !Enum.IsDefined(kind) || MinLevel > MaxLevel || MinRank > MaxRank)
            throw new ArgumentException("Member filters require a known kind and non-reversed Level/Rank bounds.");
    }

    internal bool Matches(ArchetypeCardFacts card) =>
        (Kind is null || card.Kind == Kind) && (IsToken is null || card.IsToken == IsToken) &&
        (MinLevel is null && MaxLevel is null || card.Level is { } level &&
            (MinLevel is null || level >= MinLevel) && (MaxLevel is null || level <= MaxLevel)) &&
        (MinRank is null && MaxRank is null || card.Rank is { } rank &&
            (MinRank is null || rank >= MinRank) && (MaxRank is null || rank <= MaxRank));
}
