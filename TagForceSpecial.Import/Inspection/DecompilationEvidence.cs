using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Inspection;

public sealed record FunctionEvidence(string Module, string Address, int BodyBytes, string Status, string? Name,
    string? SourceSha256, string Registry, string? Evidence, string EvidenceIntegrity, string Details);
public sealed record EvidenceInventory(DateTimeOffset MeasuredUtc, FunctionEvidence[] Functions, string[] Metrics, string[] Diagnostics);

/// <summary>Displays persisted function candidates and research records without promoting C# implementation to native matching.</summary>
public static class DecompilationEvidence
{
    public static EvidenceInventory Load(string repository)
    {
        repository = Path.GetFullPath(repository);
        var functions = new List<FunctionEvidence>(); var diagnostics = new List<string>();
        var queue = ReadQueue(repository);
        string root = Path.Combine(repository, "config/npjh00142");
        if (Directory.Exists(root))
            foreach (string registry in Directory.EnumerateFiles(root, "*functions.csv", SearchOption.AllDirectories))
            {
                PrivateStorage.Check(registry);
                string module = Path.GetFileName(registry) == "functions.csv" ? "EBOOT.BIN" : Path.GetFileNameWithoutExtension(registry).Replace("_functions", "");
                foreach (var f in FunctionInventory.Read(File.ReadAllText(registry)))
                {
                    string address = $"0x{f.Address:X8}";
                    var evidence = queue.FirstOrDefault(q => Get(q, "module") == module && string.Equals(Get(q, "address"), address, StringComparison.OrdinalIgnoreCase));
                    string? persisted = Get(evidence, "status");
                    string integrity = evidence.ValueKind == JsonValueKind.Undefined ? "No research record" : CheckEvidence(repository, evidence);
                    string status = persisted switch { null => "Discovered", "classified" => "Classified", "named" => "Named", _ => "Under Investigation" };
                    functions.Add(new(module, address, f.BodyBytes, status,
                        Get(evidence, "id"), Get(evidence, "sourceSha256"), Path.GetRelativePath(repository, registry),
                        persisted, integrity, evidence.ValueKind == JsonValueKind.Undefined ? "Registry candidate only; source binary identity not recorded here." : evidence.GetRawText()));
                }
            }
        else diagnostics.Add("Function registry directory not found.");
        foreach (var entry in queue)
            if (!functions.Any(f => f.Module == Get(entry, "module") && string.Equals(f.Address, Get(entry, "address"), StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add($"Research record {Get(entry, "id")} has no matching function registry row.");
        string native = File.Exists(Path.Combine(repository, "config/native-matches.json"))
            ? "Native byte matching: Not measured here; use the existing native evidence validator before accepting a match." : "Native byte matching: Not measured (no native match manifest).";
        return new(DateTimeOffset.UtcNow, functions.ToArray(),
        [
            $"Discovered candidates: {functions.Count:N0}; candidate body bytes: {functions.Sum(f => (long)f.BodyBytes):N0} (not a unique executable-byte coverage measurement).",
            "Asset extraction coverage: Not measured by function inventories; see import stage results.",
            "Asset validation coverage: Not measured by function inventories; see validation checks.",
            "Binary analysis coverage: Not measured (executable denominator and classified ranges not validated here).",
            "Function reconstruction coverage: Not measured (no accepted reconstruction evidence adapter).",
            native,
            "C# behavioral parity: Not measured (no scoped original-versus-C# result set loaded)."
        ], diagnostics.ToArray());
    }
    private static JsonElement[] ReadQueue(string repository)
    {
        string path = Path.Combine(repository, "config/research-queue.json");
        if (!File.Exists(path)) return [];
        PrivateStorage.Check(path);
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Research queue exceeds its limit.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("functions", out var functions) || functions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Research queue must contain a functions array.");
        return functions.EnumerateArray().Select(e => e.Clone()).ToArray();
    }
    private static string CheckEvidence(string repository, JsonElement entry)
    {
        if (!entry.TryGetProperty("provenance", out var refs)) return "Missing provenance";
        int matched = 0, missing = 0, changed = 0;
        foreach (var item in refs.EnumerateArray())
        {
            string? relative = Get(item, "path"), expected = Get(item, "sha256");
            if (relative is null || expected is null) { missing++; continue; }
            string path = Path.GetFullPath(Path.Combine(repository, relative));
            if (!path.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { changed++; continue; }
            if (!File.Exists(path)) { missing++; continue; }
            PrivateStorage.Check(path);
            using var stream = File.OpenRead(path);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) == expected.ToLowerInvariant()) matched++; else changed++;
        }
        return $"{matched} hashes match / {missing} missing / {changed} changed; persisted status is not an independent verification result";
    }
    private static string? Get(JsonElement entry, string name) => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
