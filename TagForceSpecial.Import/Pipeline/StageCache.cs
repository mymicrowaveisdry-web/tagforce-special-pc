using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Pipeline;

/// <summary>Immutable stage outputs under <c>import/stages/&lt;stage&gt;/&lt;key&gt;</c>, built in a sibling staging folder and published by one move.</summary>
public sealed class StageCache(PrivateStorage storage)
{
    /// <summary>A key over every input that changes a stage's output: stage version, tool identity, source identity and options.</summary>
    public static string Key(params string?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts.Select(p => p ?? "<null>")))));

    public string RelativeDirectory(string stage, string key) => ImportLayout.StageOutput(stage, key);
    public string Directory(string stage, string key) => storage.PathFor(RelativeDirectory(stage, key));

    /// <summary>Returns the existing output only when <paramref name="validate"/> accepts it; a rejected output is left in place for inspection.</summary>
    public bool TryReuse(string stage, string key, Func<string, bool> validate)
    {
        string path = Directory(stage, key);
        return System.IO.Directory.Exists(path) && validate(path);
    }

    /// <summary>Builds into a new staging folder, then moves it to the keyed directory. An identical existing output is kept; a differing
    /// (for example corrupted) one is moved aside to <c>.rejected-*</c> for inspection, never deleted.</summary>
    public string Publish(string stage, string key, Action<string> build, CancellationToken cancellation)
    {
        if (!ImportLayout.ValidKey(key)) throw new ArgumentException("Invalid stage cache key.");
        string parent = storage.PathFor(ImportLayout.Stages(stage));
        System.IO.Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".building-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(staging);
        try
        {
            build(staging);
            cancellation.ThrowIfCancellationRequested();
            string target = Directory(stage, key);
            if (System.IO.Directory.Exists(target))
            {
                if (Inventory(target).SequenceEqual(Inventory(staging))) return target;
                System.IO.Directory.Move(target, Path.Combine(parent, ".rejected-" + Guid.NewGuid().ToString("N")));
            }
            System.IO.Directory.Move(staging, target);
            return target;
        }
        finally { RemoveStaging(parent, staging); }
    }

    /// <summary>SHA-256 and length of every file under a stage directory, '/'-separated and ordinally sorted.</summary>
    public static ArtifactRecord[] Inventory(string directory, int maximumFiles = 20_000)
    {
        var root = Path.GetFullPath(directory);
        var files = System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(maximumFiles + 1).ToArray();
        if (files.Length > maximumFiles) throw new InvalidDataException("Stage output has too many files.");
        return files.Select(f =>
        {
            PrivateStorage.Check(f);
            using var stream = File.OpenRead(f);
            return new ArtifactRecord(Path.GetRelativePath(root, f).Replace('\\', '/'), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }).OrderBy(a => a.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>True when the directory holds exactly the recorded files with the recorded hashes.</summary>
    public static bool Matches(string directory, IReadOnlyList<ArtifactRecord> expected)
    {
        try { return Inventory(directory).SequenceEqual(expected.OrderBy(a => a.Path, StringComparer.Ordinal)); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return false; }
    }

    private static void RemoveStaging(string parent, string staging)
    {
        if (!System.IO.Directory.Exists(staging)) return;
        // Only this call's own ".building-" folder directly under the stage parent is ever removed.
        string resolved = Path.GetFullPath(staging);
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith(".building-", StringComparison.Ordinal))
            throw new InvalidOperationException("Staging cleanup target leaves its stage folder.");
        PrivateStorage.Check(resolved);
        System.IO.Directory.Delete(resolved, recursive: true);
    }
}
