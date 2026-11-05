using System.Security.Cryptography;
using System.Text.Json;
using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Import;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.PC.Import;

namespace TagForceSpecial.PC.Setup;

public sealed class SetupStore
{
    private sealed record Journal(int SchemaVersion, string SourcePath, string Profile);
    public PrivateStorage Storage { get; }
    public Preferences Preferences { get; private set; } = new();
    public ValidatedGameCache? Cache { get; private set; }
    public SetupIssue? Issue { get; private set; }
    public ResourceIndex? Resources { get; private set; }
    public string? PresentationNotice { get; private set; }
    private bool preferencesAccepted = true;
    public SetupStore(string root)
    {
        Storage = new(root);
        if (!File.Exists(Storage.PathFor("preferences.json"))) return;
        try { var preferences = Storage.Read<Preferences>("preferences.json", 65536); preferences.Validate(); Preferences = preferences; }
        catch (Exception e) when (Recoverable(e)) { Issue = Error("PreferencesInvalid"); preferencesAccepted = false; }
    }
    public void SavePreferences(Preferences value)
    {
        value.Validate();
        // Backup only the last accepted in-memory document, never a corrupt file.
        if (preferencesAccepted) Storage.Write("preferences.backup.json", Preferences);
        Storage.Write("preferences.json", value); Preferences = value; preferencesAccepted = true;
    }
    public SetupResult RestorePreferences()
    {
        try
        {
            var backup = Storage.Read<Preferences>("preferences.backup.json", 65536); backup.Validate();
            Storage.Write("preferences.json", backup); Preferences = backup; preferencesAccepted = true;
            return ValidateActive();
        }
        catch (Exception e) when (Recoverable(e)) { return Fail("PreferencesInvalid"); }
    }
    public SetupResult ValidateActive()
    {
        Cache = null; Resources = null;
        if (Preferences.ActiveGeneration is not { } id) return Fail("CacheMissing");
        try
        {
            Preferences.Validate();
            string cachePath = Storage.PathFor($"caches/{id}/cache");
            var validated = GameCache.Validate(cachePath);
            if (validated.Cache is null) return Fail(validated.Diagnostics.FirstOrDefault()?.Code ?? "CacheUnreadable");
            var expected = ResourceIndex.Create(cachePath);
            var actual = Storage.Read<ResourceIndex>($"caches/{id}/resources.json", 4 * 1024 * 1024);
            if (actual.Fingerprint() != expected.Fingerprint()) return Fail("ResourceIndexMismatch");
            Cache = validated.Cache; Resources = expected; Issue = null;
            return new(true);
        }
        catch (Exception e) when (Recoverable(e)) { return Fail("CacheUnreadable"); }
    }
    public string? ResumeSource()
    {
        try
        {
            var journal = Storage.Read<Journal>("import-journal.json", 65536);
            return journal.SchemaVersion == 1 && journal.Profile == GameBuildProfile.English21.Fingerprint && Path.IsPathFullyQualified(journal.SourcePath)
                ? journal.SourcePath : null;
        }
        catch (Exception e) when (Recoverable(e)) { return Preferences.SourcePath; }
    }
    public void RestartWork()
    {
        string work = Storage.PathFor("work");
        if (!Directory.Exists(work)) return;
        string retained = Storage.PathFor("retained/work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
        // Both absolute paths are resolved/checked within this private root; no recursive deletion.
        Directory.Move(work, retained);
    }

    public SetupResult Import(string sourcePath, IProgress<ImportProgress>? progress = null, CancellationToken cancellation = default)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            // A second process cannot reuse or replace this work area during an import.
            using var lease = new FileStream(Storage.PathFor("import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var profile = GameBuildProfile.English21;
            progress?.Report(new(0, "Checking the selected original image"));
            var validation = GameSourceValidator.Open(sourcePath, cancellationToken: cancellation,
                progress: new InlineProgress<long>(n => progress?.Report(new((int)(n * 30 / profile.ImageLength), "Checking the selected original image"))));
            using var source = validation.Source;
            if (source is null) return Fail(validation.Diagnostics.FirstOrDefault()?.Code ?? "SourceUnavailable");
            Storage.Write("import-journal.json", new Journal(1, Path.GetFullPath(sourcePath), profile.Fingerprint));
            foreach (var mapping in profile.SourceFiles.Where(f => f.CachePath is not null))
            {
                cancellation.ThrowIfCancellationRequested();
                string destination = Storage.PathFor("work/" + mapping.CachePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination) && new FileInfo(destination).Length == mapping.Length && Hash(destination) == mapping.Sha256) continue;
                if (File.Exists(destination)) File.Delete(destination);
                progress?.Report(new(35, "Importing local game resources"));
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyMappedFile(mapping.Path, output, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            string package = Storage.PathFor("work/catalog");
            bool packageValid = false;
            try
            {
                packageValid = profile.CacheFiles.Where(f => f.Path.StartsWith("catalog/", StringComparison.Ordinal))
                    .All(f => File.Exists(Storage.PathFor("work/" + f.Path)) && Hash(Storage.PathFor("work/" + f.Path)) == f.Sha256);
            }
            catch (Exception e) when (Recoverable(e)) { packageValid = false; }
            if (!packageValid)
            {
                progress?.Report(new(45, "Converting card names, descriptions and statistics"));
                // Remove only the known interrupted converter outputs, inside the checked work area.
                foreach (string name in new[] { "cards.json", "manifest.json" })
                { string f = Storage.PathFor("work/catalog/" + name); if (File.Exists(f)) File.Delete(f); }
                if (Directory.Exists(package)) Directory.Delete(package, recursive: false);
                using var images = File.OpenRead(Storage.PathFor("work/assets/cardh_j.cip"));
                var imageIndex = new CardImageIndexReader().ReadIndex(images);
                using var cards = File.OpenRead(Storage.PathFor("work/source/cardinfo.ehp"));
                var catalog = PspCardCatalogImporter.Read(cards, imageIndex.MinimumCardId, imageIndex.MaximumCardId);
                CardCatalogPackage.Write(package, catalog, new("tfsp-original", "1", "en", $"cardinfo-sha256:{catalog.Source.ContainerSha256}"));
            }
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(new(65, "Validating and preparing the new cache"));
            string generation = Guid.NewGuid().ToString("N");
            string cache = Storage.PathFor($"caches/{generation}/cache");
            var result = GameCache.Create(source, package, cache, cancellation);
            if (result.Cache is null) return Fail(result.Diagnostics.FirstOrDefault()?.Code ?? "ImportFailed");
            progress?.Report(new(90, "Indexing card resources"));
            var resources = ResourceIndex.Create(cache);
            Storage.Write($"caches/{generation}/resources.json", resources);
            cancellation.ThrowIfCancellationRequested();
            // The old generation is retained. Only a completely validated generation becomes active.
            SavePreferences(Preferences with { ActiveGeneration = generation, SourcePath = Path.GetFullPath(sourcePath) });
            Cache = result.Cache; Resources = resources; Issue = null;
            progress?.Report(new(95, "Preparing original title artwork"));
            PresentationNotice = null;
            try { TitlePresentationImport.Ensure(source, Storage); }
            catch (Exception e) when (Recoverable(e))
            { PresentationNotice = "Game data is ready; original title artwork could not be prepared. Retry setup to rebuild it."; }
            progress?.Report(new(100, "Import complete. You can prepare a supported deck."));
            return new(true);
        }
        catch (OperationCanceledException) { return Fail("ImportCancelled"); }
        catch (Exception e) when (Recoverable(e)) { return Fail(e is UnauthorizedAccessException ? "StorageUnavailable" : "ImportFailed"); }
    }
    public static bool Recoverable(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException;
    public static SetupIssue Error(string code) => new(code, code switch
    {
        "SourceLengthMismatch" or "SourceHashMismatch" => "This image is not supported. Select the complete English v2.1 ISO.",
        "MissingSourceFile" or "SourceFileLengthMismatch" or "SourceFileHashMismatch" or "InvalidSourceStructure" => "Required game resources are missing or damaged. Select the original complete ISO.",
        "SourceUnavailable" => "The selected image could not be read. Reconnect its drive or select the ISO again.",
        "ImportCancelled" => "Import stopped safely. Resume to reuse verified work.",
        "PreferencesInvalid" => "Preferences are unreadable or from another version. Restore the backup or reset preferences.",
        "CacheMissing" => "Select your original ISO to prepare local game data.",
        "StorageUnavailable" => "The private data folder is not writable. Check its permissions or free space.",
        "SourceChanged" => "The source changed during import. Select the original ISO and retry.",
        "ImportFailed" => "Import could not finish. Check available storage and retry; the previous cache is retained.",
        _ => "The local cache could not be verified. Rebuild from your original ISO; saved decks are retained."
    });
    private SetupResult Fail(string code) { Issue = Error(code); return new(false, Issue); }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
