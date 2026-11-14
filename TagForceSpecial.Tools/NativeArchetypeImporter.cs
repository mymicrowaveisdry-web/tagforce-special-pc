using System.Buffers.Binary;
using System.Security.Cryptography;
using TagForceSpecial.Cards;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Tools;

public sealed record NativeArchetypeImport(NativeArchetypeIndex NativeIndex, ArchetypePackDocument Pack,
    NativeArchetypeLabelsDocument CandidateLabels);

/// <summary>Imports a hash-pinned native table and explicitly reviewed labels without altering the catalog or gameplay.</summary>
public static class NativeArchetypeImporter
{
    public static NativeArchetypeImport Import(ReadOnlySpan<byte> bytes, string expectedSha256,
        CardCatalog catalog, string catalogSha256, NativeArchetypeLabelsDocument labels)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(labels);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (expectedSha256 is not { Length: 64 } || !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native table hash differs from the explicitly pinned source.");
        if (bytes.Length < 4) throw new InvalidDataException("Native header is truncated.");
        int count = BinaryPrimitives.ReadInt16LittleEndian(bytes);
        int total = BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]);
        if (count <= 0 || total < 0 || bytes.Length != 4 + count * 4 + total * 2)
            throw new InvalidDataException("Native header dimensions or file length are invalid.");
        var cards = catalog.Cards.ToDictionary(c => c.GameCardId);
        if (cards.Values.Any(c => c.Codes is null))
            throw new InvalidDataException("Native import requires decoded card-kind codes for explicit Token handling.");
        var groups = new NativeArchetypeGroup[count];
        int next = 0;
        for (int i = 0; i < count; i++)
        {
            int offset = BinaryPrimitives.ReadInt16LittleEndian(bytes[(4 + i * 4)..]);
            int length = BinaryPrimitives.ReadInt16LittleEndian(bytes[(6 + i * 4)..]);
            if (offset != next || length < 0 || offset + length > total)
                throw new InvalidDataException("Native group offset/count is negative, overlapping, noncontiguous or out of bounds.");
            var ids = new uint[length];
            for (int j = 0; j < length; j++)
            {
                int id = BinaryPrimitives.ReadInt16LittleEndian(bytes[(4 + count * 4 + (offset + j) * 2)..]);
                if (id <= 0 || !cards.ContainsKey((uint)id)) throw new InvalidDataException("Native member is not a known positive original catalog ID.");
                ids[j] = (uint)id;
            }
            groups[i] = new(i, offset, ids);
            next += length;
        }
        var tokens = catalog.Cards.Where(c => c.Codes!.GetterKind == 10).Select(c => c.GameCardId).Order().ToArray();
        var index = new NativeArchetypeIndex(new(1, hash, catalogSha256, total, tokens, groups));
        ValidateLabels(labels, index);
        var reviewed = labels.Labels.Where(l => l.Review == ArchetypeReviewStatus.Reviewed).OrderBy(l => l.NativeGroupId).ToArray();
        var byGroup = reviewed.ToDictionary(l => l.NativeGroupId);
        var evidence = new ArchetypeEvidence("CARD_Named.bin", hash, "Original numeric membership; semantic labels are reviewed separately.");
        var definitions = reviewed.Select(l => new ArchetypeDefinition(l.Archetype!.Value, l.Name,
            Array.AsReadOnly(l.Aliases.Order(StringComparer.Ordinal).ToArray()), [], l.Review, l.Evidence)).ToArray();
        var membershipIds = new SortedDictionary<uint, List<ArchetypeId>>();
        foreach (var label in reviewed)
            foreach (var id in index.GetMembers(label.NativeGroupId).Where(id => !index.IsToken(id)))
            {
                if (!membershipIds.TryGetValue(id, out var list)) membershipIds.Add(id, list = []);
                list.Add(label.Archetype!.Value);
            }
        var memberships = membershipIds.Select(p => new ArchetypeCardMembership(SourceId(p.Key),
            Array.AsReadOnly(p.Value.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray()), ArchetypeReviewStatus.Reviewed, evidence)).ToArray();
        var mappings = groups.Select(g => new NativeArchetypeMapping(g.NativeGroupId,
            byGroup.TryGetValue(g.NativeGroupId, out var label) ? label.Archetype : null, ArchetypeReviewStatus.Reviewed,
            byGroup.TryGetValue(g.NativeGroupId, out label) ? label.Evidence : evidence with { Reason = "Historical slot retained without asserting a semantic label." })).ToArray();
        var pack = new ArchetypePackDocument(
            new(1, new("tfs:native-archetypes"), hash, ArchetypePackKind.Native,
                [ArchetypeContentProfile.TfsClassic, ArchetypeContentProfile.Expanded], [], evidence),
            new(1, Array.AsReadOnly(definitions), []), new(1, Array.AsReadOnly(memberships)), new(1, []),
            new(1, Array.AsReadOnly(mappings)));
        var issues = ArchetypePackValidator.Validate(pack, new(catalog.Cards.Select(c => SourceId(c.GameCardId)).ToArray(), []));
        if (issues.Count != 0) throw new InvalidDataException(string.Join("; ", issues.Select(i => $"{i.Path}: {i.Code}: {i.Message}")));
        var candidates = labels.Labels.Where(l => l.Review == ArchetypeReviewStatus.Candidate)
            .OrderBy(l => l.NativeGroupId).ThenBy(l => l.Name, StringComparer.Ordinal)
            .Select(l => l with { Aliases = Array.AsReadOnly(l.Aliases.Order(StringComparer.Ordinal).ToArray()) }).ToArray();
        return new(index, pack, new(1, Array.AsReadOnly(candidates)));
    }

    private static SourceCardId SourceId(uint id) => new($"tfs:{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    private static void ValidateLabels(NativeArchetypeLabelsDocument document, NativeArchetypeIndex index)
    {
        if (document.SchemaVersion != 1 || document.Labels is null) throw new InvalidDataException("Invalid native label document.");
        var reviewedGroups = new HashSet<int>();
        var semanticIds = new HashSet<ArchetypeId>();
        var names = new HashSet<(int Group, string Name)>();
        foreach (var label in document.Labels)
        {
            if (label is null || label.NativeGroupId < 0 || label.NativeGroupId >= index.GroupCount ||
                string.IsNullOrWhiteSpace(label.Name) || label.Name != label.Name.Trim() || label.Aliases is null ||
                !Enum.IsDefined(label.Review) || label.Evidence is null || string.IsNullOrWhiteSpace(label.Evidence.Reference) ||
                string.IsNullOrWhiteSpace(label.Evidence.Reason) || label.Evidence.Sha256 is not { Length: 64 } ||
                !label.Evidence.Sha256.All(Uri.IsHexDigit) ||
                label.Aliases.Any(a => string.IsNullOrWhiteSpace(a) || a != a.Trim()) ||
                label.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != label.Aliases.Count ||
                !names.Add((label.NativeGroupId, label.Name)))
                throw new InvalidDataException("Native labels require valid slots, distinct names, review states, aliases and evidence.");
            if (label.Review == ArchetypeReviewStatus.Reviewed)
            {
                if (label.Archetype is not { } id || string.IsNullOrWhiteSpace(id.Value) ||
                    !reviewedGroups.Add(label.NativeGroupId) || !semanticIds.Add(id))
                    throw new InvalidDataException("Reviewed labels need one explicit unique semantic ID per native slot.");
            }
            else if (label.Archetype is not null)
                throw new InvalidDataException("Candidate labels must remain diagnostics without an assigned semantic ID.");
        }
    }
}
