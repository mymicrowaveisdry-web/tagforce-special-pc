using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Sources;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <param name="Offset">Byte offset inside the ISO image; null for directory sources.</param>
/// <param name="Members">Entry count of an archive whose index was read, else null.</param>
/// <param name="Parse">"parsed", "failed" or null when no index reader applies.</param>
public sealed record InventoryRecord(string Path, long Length, long? Offset, string Sha256, string Format, string Category, string? Decoder,
    string Reason, string Header, int? Members, string? Parse, string? ParseDetail);

public sealed record InventoryDocument(int SchemaVersion, string SourceKind, string? SourceSha256, string TreeSha256, long Bytes, InventoryRecord[] Files);

/// <summary>Lists every original file with its hash, signature class and, for archives with an existing reader, its index status.
/// Unknown files stay listed as reverse-engineering evidence.</summary>
public static class InventoryStage
{
    public const string Id = "inventory";
    public const string Version = "inventory-v1";
    public const string File = "inventory.json";

    public static StageResult Run(StageCache cache, SourceIdentity source, IReadOnlyList<ScannedFile>? scan, bool reuse,
        IProgress<(long Done, long Total, string Item)>? progress, CancellationToken cancellation)
    {
        // A supported ISO's whole-image hash identifies every byte, so its inventory can be reused before rescanning.
        string? earlyKey = source.Kind == "iso" && source.Supported && source.Sha256 is { } sha ? StageCache.Key(Id, Version, Classes(), sha) : null;
        if (reuse && earlyKey is not null && cache.TryReuse(Id, earlyKey, dir => TryOpen(dir) is not null))
            return Result(StageStatus.Reused, "Inventory reused for this exact image.", cache, earlyKey, Open(cache.Directory(Id, earlyKey)));
        using var files = GameFileSystem.Open(source.Path);
        scan ??= SourceScan.Run(files, progress, cancellation);
        string tree = SourceScan.TreeDigest(scan);
        string key = earlyKey ?? StageCache.Key(Id, Version, Classes(), source.Kind, tree);
        if (reuse && cache.TryReuse(Id, key, dir => TryOpen(dir) is not null))
            return Result(StageStatus.Reused, "Inventory reused for identical files.", cache, key, Open(cache.Directory(Id, key)));
        var records = scan.Select(f => Record(files, f, source.Kind)).ToArray();
        var document = new InventoryDocument(1, source.Kind, source.Sha256, tree, scan.Sum(f => f.Entry.Length), records);
        cache.Publish(Id, key, staging => System.IO.File.WriteAllBytes(Path.Combine(staging, File), ManifestJson.Serialize(document)), cancellation);
        return Result(StageStatus.Succeeded, $"Inventory: {records.Length} files, {records.Count(r => r.Format == "unknown")} without a known signature.",
            cache, key, document);
    }

    public static InventoryDocument Open(string directory)
    {
        string path = Path.Combine(directory, File);
        PrivateStorage.Check(path);
        if (new FileInfo(path).Length > 256 * 1024 * 1024) throw new InvalidDataException("Inventory exceeds its limit.");
        return ManifestJson.Parse<InventoryDocument>(System.IO.File.ReadAllBytes(path));
    }

    public static InventoryDocument? TryOpen(string directory)
    {
        try { return Open(directory); }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return null; }
    }

    private static InventoryRecord Record(GameFileSystem files, ScannedFile file, string kind)
    {
        var cls = FileClassifier.Classify(file.Entry.Path, file.Header, file.Entry.Length);
        int? members = null; string? parse = null, detail = null;
        try
        {
            if (cls.Format == "ehp")
            {
                var index = files.WithStream(file.Entry, s => new EhpReader().ReadIndex(s, EhpReadMode.InspectStoredRanges));
                members = index.Entries.Count; parse = "parsed";
                detail = index.Diagnostics.Count == 0 ? null : string.Join("; ", index.Diagnostics.Take(3));
            }
            else if (cls.Format is "cip" or "cpj" or "cpm")
            {
                var index = files.WithStream(file.Entry, s => new CardImageIndexReader().ReadIndex(s));
                members = index.Entries.Count; parse = "parsed";
            }
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or EndOfStreamException or IOException)
        { parse = "failed"; detail = e.Message; }
        return new(file.Entry.Path, file.Entry.Length, kind == "iso" ? file.Entry.Offset : null, file.Sha256, cls.Format, cls.Category, cls.Decoder,
            cls.Reason, Convert.ToHexString(file.Header), members, parse, detail);
    }

    private static StageResult Result(StageStatus status, string message, StageCache cache, string key, InventoryDocument document)
    {
        var archives = document.Files.Where(f => f.Parse is not null).ToArray();
        int failed = archives.Count(f => f.Parse == "failed"), unknown = document.Files.Count(f => f.Format == "unknown");
        var output = new StageOutput(Id, cache.RelativeDirectory(Id, key), key, Version, StageCache.Inventory(cache.Directory(Id, key)));
        return new(status, message, output,
        [
            new("archive.index-readable", CheckCategory.Archive, failed > 0 ? CheckSeverity.Warning : CheckSeverity.Info,
                archives.Length == 0 ? CheckStatus.Skipped : failed > 0 ? CheckStatus.Warning : CheckStatus.Passed, "inventory",
                "Every EHP and card image archive index was read within its file bounds by the existing readers.",
                $"{archives.Length - failed} of {archives.Length} archive indexes read" + (failed > 0 ? $"; {failed} failed and are listed in the inventory." : ".")),
            new("inventory.classified", CheckCategory.Archive, CheckSeverity.Info, unknown > 0 ? CheckStatus.Warning : CheckStatus.Passed, "inventory",
                "Every file matched a known signature.",
                $"{unknown} of {document.Files.Length} files have no known signature; they stay listed with header bytes as reverse-engineering evidence.")
        ], document.Files.Length, document.Files.Length);
    }

    /// <summary>Changes when classification or index reading changes, so stale inventories are not reused.</summary>
    private static string Classes() => ToolIdentity.DecoderSha256 + ";" + typeof(FileClassifier).Assembly.ManifestModule.ModuleVersionId;
}
