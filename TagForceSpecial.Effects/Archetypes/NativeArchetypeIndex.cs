namespace TagForceSpecial.Effects.Archetypes;

public sealed record NativeArchetypeGroup(int NativeGroupId, int MemberOffset, IReadOnlyList<uint> CardIds);
public sealed record NativeArchetypeTableDocument(int SchemaVersion, string SourceSha256, string CatalogSha256,
    int MembershipCount, IReadOnlyList<uint> TokenCardIds, IReadOnlyList<NativeArchetypeGroup> Groups);
public sealed record NativeArchetypeLabel(int NativeGroupId, string Name, ArchetypeId? Archetype,
    IReadOnlyList<string> Aliases, ArchetypeReviewStatus Review, ArchetypeEvidence Evidence);
public sealed record NativeArchetypeLabelsDocument(int SchemaVersion, IReadOnlyList<NativeArchetypeLabel> Labels);

public interface INativeTfsArchetypeIndex
{
    int GroupCount { get; }
    int MembershipCount { get; }
    bool Contains(int nativeGroupId, uint originalCardId);
    IReadOnlyList<uint> GetMembers(int nativeGroupId);
    bool IsToken(uint originalCardId);
}

/// <summary>Immutable historical membership, including empty slots and Tokens, without semantic-name inference.</summary>
public sealed class NativeArchetypeIndex : INativeTfsArchetypeIndex
{
    private readonly uint[][] members;
    private readonly HashSet<uint> tokens;
    public NativeArchetypeTableDocument Document { get; }
    public int GroupCount => members.Length;
    public int MembershipCount => Document.MembershipCount;

    public NativeArchetypeIndex(NativeArchetypeTableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireHash(document.SourceSha256); RequireHash(document.CatalogSha256);
        if (document.SchemaVersion != ArchetypePackJson.SchemaVersion || document.Groups is null ||
            document.Groups.Count is <= 0 or > short.MaxValue || document.MembershipCount is < 0 or > short.MaxValue ||
            document.TokenCardIds is null)
            throw new InvalidDataException("Invalid native table schema or signed-halfword dimensions.");
        var tokenIds = document.TokenCardIds.ToArray();
        RequireSortedIds(tokenIds);
        tokens = tokenIds.ToHashSet();
        members = new uint[document.Groups.Count][];
        var groups = new NativeArchetypeGroup[members.Length];
        int offset = 0;
        for (int i = 0; i < members.Length; i++)
        {
            var group = document.Groups[i];
            if (group is null || group.NativeGroupId != i || group.MemberOffset != offset || group.CardIds is null)
                throw new InvalidDataException("Native slots must be complete, ordered and contiguous.");
            var ids = group.CardIds.ToArray();
            RequireSortedIds(ids);
            if (ids.Length > document.MembershipCount - offset)
                throw new InvalidDataException("Native members exceed the declared count.");
            members[i] = ids;
            groups[i] = new(i, offset, Array.AsReadOnly(ids));
            offset += ids.Length;
        }
        if (offset != document.MembershipCount) throw new InvalidDataException("Native membership count differs from the header.");
        Document = document with
        {
            SourceSha256 = document.SourceSha256.ToLowerInvariant(), CatalogSha256 = document.CatalogSha256.ToLowerInvariant(),
            TokenCardIds = Array.AsReadOnly(tokenIds), Groups = Array.AsReadOnly(groups)
        };
    }

    public bool Contains(int nativeGroupId, uint originalCardId) => nativeGroupId >= 0 && nativeGroupId < members.Length &&
        originalCardId is > 0 and <= (uint)short.MaxValue && Array.BinarySearch(members[nativeGroupId], originalCardId) >= 0;

    public IReadOnlyList<uint> GetMembers(int nativeGroupId)
    {
        if (nativeGroupId < 0 || nativeGroupId >= members.Length) throw new ArgumentOutOfRangeException(nameof(nativeGroupId));
        return Document.Groups[nativeGroupId].CardIds;
    }

    public bool IsToken(uint originalCardId) => tokens.Contains(originalCardId);

    private static void RequireSortedIds(uint[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] is 0 or > (uint)short.MaxValue || i > 0 && ids[i - 1] >= ids[i])
                throw new InvalidDataException("Native IDs must be sorted, unique, positive signed halfwords.");
    }

    private static void RequireHash(string? hash)
    {
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("A native table and catalog SHA-256 are required.");
    }
}
