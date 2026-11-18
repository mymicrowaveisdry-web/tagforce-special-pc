using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Resources;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Installs the private Card Album binder package (binder.json with its .sha256, geometry captured from the original's GPU dumps;
/// it cannot be derived from the disc) into the data root, so the game finds it without --album-reference. Sources, in order: the data
/// root's installed copy, then album or album-reference folders in the bounded local discovery roots. Never downloaded or bundled.</summary>
public static class AlbumReferenceStage
{
    public const string Id = "album-reference";
    public const string Folder = "album-reference", FileName = "binder.json";

    public static StageResult Run(PrivateStorage storage, string? source, string buildId, CancellationToken cancellation)
    {
        string target = storage.PathFor(Folder);
        string? found = Candidates(storage, source).FirstOrDefault(c => { cancellation.ThrowIfCancellationRequested(); return Valid(c, buildId); });
        if (found is null)
            return Result(false, "Card Album binder package not found; the album shows its PC notice until a captured package is placed in a local album-reference folder.");
        if (!string.Equals(Path.GetFullPath(found).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(target);
            foreach (string name in new[] { FileName, FileName + ".sha256" })
            {
                string temporary = Path.Combine(target, name + ".tmp");
                File.Copy(Path.Combine(found, name), temporary, true);
                File.Move(temporary, Path.Combine(target, name), true);
            }
            if (!Valid(target, buildId)) return Result(false, "The copied Card Album binder package failed its hash check.");
        }
        return Result(true, "Card Album binder package installed and hash-verified.");
    }

    private static IEnumerable<string> Candidates(PrivateStorage storage, string? source)
    {
        yield return storage.PathFor(Folder);
        foreach (string root in ResourceDiscovery.Roots(storage, source))
            foreach (string name in new[] { Folder, "album", Path.Combine("reference", "album") })
                yield return Path.Combine(root, name);
    }

    /// <summary>The package's SHA-256 equals its sidecar and it names the source build (the game's loader repeats both checks).</summary>
    private static bool Valid(string folder, string buildId)
    {
        try
        {
            string file = Path.Combine(folder, FileName), pin = file + ".sha256";
            if (!File.Exists(file) || !File.Exists(pin)) return false;
            PrivateStorage.Check(file);
            byte[] data = File.ReadAllBytes(file);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), File.ReadAllText(pin).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            using var json = JsonDocument.Parse(data);
            return json.RootElement.TryGetProperty("buildId", out var build) && build.GetString() == buildId;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e) || e is JsonException) { return false; }
    }

    private static StageResult Result(bool ready, string message) => new(ready ? StageStatus.Succeeded : StageStatus.Warning, message,
        Checks: [new("album-reference.package", CheckCategory.Asset, ready ? CheckSeverity.Info : CheckSeverity.Warning,
            ready ? CheckStatus.Passed : CheckStatus.Warning, Folder, "The private Card Album binder package matches its recorded SHA-256 and source build.",
            Remediation: ready ? null : "Place the captured binder.json and binder.json.sha256 in a local album-reference folder, then repeat import.")]);
}
