using System.Globalization;
using System.Security.Cryptography;

namespace TagForceSpecial.Tools;

// Only PSP compiler output with recorded comparison evidence counts towards this metric.
// C# test passes, decompiler pseudocode and extracted asset bytes never count here.
public sealed record NativeFunctionMatch(
    string Module, string Address, int Length, string Kind, string SourceSha256,
    string OriginalSha256, string RebuiltSha256, string EvidencePath, string EvidenceSha256);

public sealed record NativeMatchManifest(int SchemaVersion, NativeFunctionMatch[] Matches);

public sealed record NativeByteReport(
    long MatchedBytes,
    int MatchedFunctions,
    IReadOnlyDictionary<string, long> ModuleBytes,
    IReadOnlyDictionary<string, int> ModuleFunctions);

public static class NativeByteProgress
{
    public const string ManifestPath = "config/native-matches.json";

    public static NativeByteReport Load(string root, IReadOnlyList<ModuleInventory> inventories)
    {
        string path = Path.Combine(root, "config", "native-matches.json");
        var manifest = ResearchValidation.Read<NativeMatchManifest>(File.ReadAllText(path));
        if (manifest.SchemaVersion != 1 || manifest.Matches is null)
            throw new InvalidDataException("Unsupported native match manifest.");

        var report = Measure(inventories, manifest.Matches);
        ResearchQueue? queue = null;
        if (manifest.Matches.Length > 0)
        {
            queue = ResearchValidation.Read<ResearchQueue>(File.ReadAllText(Path.Combine(root, "config/research-queue.json")));
            ResearchValidation.ValidateQueue(queue, inventories);
        }
        string evidenceRoot = Path.GetFullPath(Path.Combine(root, "config", "match-evidence"))
            + Path.DirectorySeparatorChar;
        foreach (var match in manifest.Matches)
        {
            string relative = match.EvidencePath.Replace('/', Path.DirectorySeparatorChar);
            string evidence = Path.GetFullPath(Path.Combine(root, relative));
            if (!evidence.StartsWith(evidenceRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(evidence) || !ValidSha(match.EvidenceSha256))
                throw new InvalidDataException($"Missing or invalid match evidence: {match.EvidencePath}");
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidence)));
            if (!actual.Equals(match.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Match evidence digest mismatch: {match.EvidencePath}");
            NativeEvidenceValidation.ValidateReview(root, match, queue!);
        }
        return report;
    }

    // Public pure computation for deterministic unit tests. Load() additionally verifies evidence files.
    public static NativeByteReport Measure(IReadOnlyList<ModuleInventory> inventories,
        IReadOnlyList<NativeFunctionMatch> matches)
    {
        var modules = inventories.ToDictionary(i => i.Module.Name, StringComparer.Ordinal);
        var matchedBytes = inventories.ToDictionary(i => i.Module.Name, _ => 0L, StringComparer.Ordinal);
        var matchedFunctions = inventories.ToDictionary(i => i.Module.Name, _ => 0, StringComparer.Ordinal);
        var ranges = new Dictionary<string, List<(ulong Start, ulong End)>>(StringComparer.Ordinal);

        foreach (var match in matches)
        {
            if (!modules.TryGetValue(match.Module, out var module))
                throw new InvalidDataException($"Unknown match module: {match.Module}");
            if (!match.Address.StartsWith("0x", StringComparison.Ordinal) ||
                !uint.TryParse(match.Address.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint address))
                throw new InvalidDataException("Invalid matched function address.");
            if (match.Length <= 0 ||
                !module.Functions.Any(f => f.Address == address && f.BodyBytes == match.Length))
                throw new InvalidDataException($"Match must cover one complete inventoried candidate: {match.Module} {match.Address}");
            if (match.Kind is not ("game-c" or "game-asm") ||
                !ValidSha(match.SourceSha256) ||
                !match.SourceSha256.Equals(module.Module.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !ValidSha(match.OriginalSha256) || !ValidSha(match.RebuiltSha256) ||
                !match.OriginalSha256.Equals(match.RebuiltSha256, StringComparison.OrdinalIgnoreCase) ||
                !ResearchValidation.RelativePath(match.EvidencePath) || !ValidSha(match.EvidenceSha256))
                throw new InvalidDataException($"Unsubstantiated native match: {match.Module} {match.Address}");

            // No function-body overlap or double-counting, including on intentionally overlapping Ghidra candidates.
            ulong start = address;
            ulong end = start + (ulong)match.Length;
            var existing = ranges.GetValueOrDefault(match.Module);
            if (existing is null) ranges[match.Module] = existing = [];
            if (existing.Any(x => start < x.End && x.Start < end))
                throw new InvalidDataException($"Overlapping native match: {match.Module} {match.Address}");
            existing.Add((start, end));
            matchedBytes[match.Module] = checked(matchedBytes[match.Module] + match.Length);
            matchedFunctions[match.Module]++;
            if (matchedBytes[match.Module] > module.Module.ExecutableBytes)
                throw new InvalidDataException("Native match bytes exceed module's inventoried executable bytes.");
        }
        return new(matchedBytes.Values.Sum(), matchedFunctions.Values.Sum(), matchedBytes, matchedFunctions);
    }

    public static string Bar(long matched, long total, int width = 24)
    {
        if (matched < 0 || total <= 0 || matched > total || width < 1 || width > 100)
            throw new ArgumentOutOfRangeException(nameof(matched));
        int fill = (int)Math.Floor((double)matched / total * width);
        return new string('█', fill) + new string('░', width - fill);
    }

    public static string Percent(long matched, long total) =>
        total == 0 ? "N/A" : ((double)matched / total * 100)
            .ToString("F2", CultureInfo.InvariantCulture) + "%";

    private static bool ValidSha(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
}
