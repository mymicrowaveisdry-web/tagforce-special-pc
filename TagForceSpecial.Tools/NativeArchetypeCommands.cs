using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Effects.Archetypes;

namespace TagForceSpecial.Tools;

/// <summary>Creates or checks reproducible local native archetype packages from caller-supplied private inputs.</summary>
public static class NativeArchetypeCommands
{
    public static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        if (command is not ("native-archetype-import" or "native-archetype-check") || args.Length != 5)
        {
            error.WriteLine("Usage: native-archetype-import|native-archetype-check <CARD_Named.bin> <catalog-package> <labels.json> <expected-table-sha256> <package-directory>");
            return 2;
        }
        try
        {
            string tablePath = Path.GetFullPath(args[0]), labelsPath = Path.GetFullPath(args[2]);
            string catalogPath = Path.GetFullPath(args[1]);
            string[] inputs = [tablePath, labelsPath, Path.Combine(catalogPath, "manifest.json"), Path.Combine(catalogPath, "cards.json")];
            var before = inputs.ToDictionary(path => path, HashFile, StringComparer.OrdinalIgnoreCase);
            byte[] bytes = ReadBounded(tablePath, 4 + short.MaxValue * 6);
            var catalog = CardCatalogPackage.Load(catalogPath);
            string labelsJson = Encoding.UTF8.GetString(ReadBounded(labelsPath, ArchetypePackJson.MaximumDocumentBytes)).TrimStart('\ufeff');
            var labels = ArchetypePackJson.ReadDocument<NativeArchetypeLabelsDocument>(labelsJson);
            var imported = NativeArchetypeImporter.Import(bytes, args[3], catalog.Catalog, catalog.Manifest.CardsSha256, labels);
            long comparisons = VerifyParity(bytes, catalog.Catalog, imported.NativeIndex);
            var files = Documents(imported);
            var report = new
            {
                schemaVersion = 1, importer = "native-archetype-v1", scope = "Static numeric membership, not gameplay verification",
                sourceSha256 = imported.NativeIndex.Document.SourceSha256,
                catalogSha256 = catalog.Manifest.CardsSha256,
                catalogManifestSha256 = before[inputs[2]], catalogContainerSha256 = catalog.Catalog.Source.ContainerSha256,
                labelsSha256 = before[labelsPath], sourceUnchanged = true,
                groupCount = imported.NativeIndex.GroupCount, membershipCount = imported.NativeIndex.MembershipCount,
                emptyGroups = imported.NativeIndex.Document.Groups.Where(g => g.CardIds.Count == 0).Select(g => g.NativeGroupId).ToArray(),
                tokenCardCount = imported.NativeIndex.Document.TokenCardIds.Count,
                tokenMembershipCount = imported.NativeIndex.Document.Groups.Sum(g => g.CardIds.Count(imported.NativeIndex.IsToken)),
                reviewedLabelCount = imported.Pack.Definitions.Definitions.Count,
                candidateLabelCount = imported.CandidateLabels.Labels.Count,
                semanticMemberCardCount = imported.Pack.Memberships.Memberships.Count,
                parityComparisons = comparisons,
                documentHashes = files.ToDictionary(p => p.Key, p => Hash(Encoding.UTF8.GetBytes(p.Value)), StringComparer.Ordinal)
            };
            files.Add("import-report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            foreach (var input in inputs)
                if (before[input] != HashFile(input)) throw new InvalidDataException("An original input changed during import/check.");
            string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[4]));
            if (command == "native-archetype-import") Publish(destination, files);
            else Check(destination, files);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                status = "pass", command, directory = destination, report.groupCount, report.membershipCount,
                report.reviewedLabelCount, report.candidateLabelCount, report.tokenMembershipCount, report.parityComparisons,
                sourceUnchanged = true
            }));
            return 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static SortedDictionary<string, string> Documents(NativeArchetypeImport imported) => new(StringComparer.Ordinal)
    {
        ["native-table.json"] = ArchetypePackJson.WriteDocument(imported.NativeIndex.Document) + "\n",
        ["manifest.json"] = ArchetypePackJson.WriteDocument(imported.Pack.Manifest) + "\n",
        ["definitions.json"] = ArchetypePackJson.WriteDocument(imported.Pack.Definitions) + "\n",
        ["memberships.json"] = ArchetypePackJson.WriteDocument(imported.Pack.Memberships) + "\n",
        ["identity-rules.json"] = ArchetypePackJson.WriteDocument(imported.Pack.IdentityRules) + "\n",
        ["native-mappings.json"] = ArchetypePackJson.WriteDocument(imported.Pack.NativeMappings) + "\n",
        ["candidate-labels.json"] = ArchetypePackJson.WriteDocument(imported.CandidateLabels) + "\n"
    };

    private static long VerifyParity(byte[] source, CardCatalog catalog, NativeArchetypeIndex index)
    {
        int count = BinaryPrimitives.ReadInt16LittleEndian(source);
        long comparisons = 0;
        for (int group = 0; group < count; group++)
        {
            int offset = BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(4 + group * 4));
            int length = BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(6 + group * 4));
            var reference = new HashSet<uint>();
            for (int member = 0; member < length; member++)
                reference.Add((uint)BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(4 + count * 4 + (offset + member) * 2)));
            if (index.GetMembers(group).Count != length || !index.GetMembers(group).SequenceEqual(reference.Order()))
                throw new InvalidDataException("Native slot contents differ from the original source.");
            foreach (var card in catalog.Cards)
            {
                if (index.Contains(group, card.GameCardId) != reference.Contains(card.GameCardId))
                    throw new InvalidDataException("Native membership lookup differs from the source.");
                comparisons++;
            }
        }
        return comparisons;
    }

    private static void Check(string directory, SortedDictionary<string, string> documents)
    {
        if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Count() != documents.Count)
            throw new InvalidDataException("Native package is missing documents or contains unexpected entries.");
        foreach (var (name, content) in documents)
            if (!File.ReadAllBytes(Path.Combine(directory, name)).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(content)))
                throw new InvalidDataException($"Native package differs from reproducible source output: {name}");
    }

    private static void Publish(string destination, SortedDictionary<string, string> documents)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Package already exists; choose a new version directory.");
        if (Path.GetDirectoryName(destination) is not { } parent || Path.GetFileName(destination).Length == 0)
            throw new ArgumentException("A package directory below a parent is required.", nameof(destination));
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".native-archetypes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (name, content) in documents) File.WriteAllText(Path.Combine(staging, name), content, new UTF8Encoding(false));
            Check(staging, documents);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (string name in documents.Keys) File.Delete(Path.Combine(staging, name));
                Directory.Delete(staging);
            }
        }
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Native import input exceeds the size limit.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
