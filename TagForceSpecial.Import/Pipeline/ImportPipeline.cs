using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Import.Diagnostics;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Pipeline;

/// <param name="Profiles">Trusted build profiles; defaults to the supported English v2.1 profile.</param>
public sealed record ImportRequest(string SourcePath, string DataRoot, ImportOptions Options, IReadOnlyList<GameBuildProfile>? Profiles = null);

/// <summary>Runs the import stages in order, records each result and publishes the session. Stages own their work; this owns order only.</summary>
public static class ImportPipeline
{
    public static readonly IReadOnlyList<(string Id, string Name)> Stages =
    [
        (SourceStage.Id, "Recognize source"), (InventoryStage.Id, "Scan files"), (CatalogStage.Id, "Convert card catalog"), (PresentationStage.Id, "Decode presentation"),
        (RecipeStage.Id, "Import recipes"), (ValidationStage.Id, "Validate"), (PublishStage.Id, "Publish")
    ];

    public static ImportManifest Run(ImportRequest request, IProgress<ImportProgress>? progress = null,
        CancellationToken cancellation = default, Action<LogEntry>? onLog = null)
    {
        var storage = new PrivateStorage(request.DataRoot);
        Directory.CreateDirectory(storage.PathFor(ImportLayout.Root));
        // One import per data root: a second process cannot interleave stage or pointer writes.
        using var lease = new FileStream(storage.PathFor(ImportLayout.Lock), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var session = new ImportSession(storage, request.Options, progress, onLog);
        using var log = session.Log;
        var cache = new StageCache(storage);
        var profiles = request.Profiles ?? [GameBuildProfile.English21];
        log.Info("pipeline", $"Import {session.Id} started; importer {ToolIdentity.ImporterVersion}.");
        ValidatedGameSource? source = null;
        LoadedCardCatalog? catalog = null;
        IReadOnlyList<Sources.ScannedFile>? scan = null;
        try
        {
            session.Run(SourceStage.Id, report =>
            {
                var outcome = SourceStage.Run(request.SourcePath, profiles,
                    new InlineProgress<(long Done, long Total, string Message)>(p => report(p.Done, p.Total, p.Message)), cancellation);
                session.Source = outcome.Identity; source = outcome.Source; scan = outcome.Scan;
                return outcome.Result;
            });
            session.Run(InventoryStage.Id, report =>
            {
                // Unsupported sources are still listed, so their files can be inspected.
                if (!request.Options.Inventory) return new(StageStatus.Skipped, "Not selected.");
                if (!session.Checks.Any(c => c.Id == "source.container" && c.Status == Validation.CheckStatus.Passed))
                    return new(StageStatus.Skipped, "Requires a readable source.");
                var result = InventoryStage.Run(cache, session.Source, scan, request.Options.ReuseCache,
                    new InlineProgress<(long Done, long Total, string Item)>(p => report(p.Done, p.Total, "Hashing " + p.Item)), cancellation);
                session.Outputs = session.Outputs with { Inventory = result.Output };
                return result;
            });
            session.Run(CatalogStage.Id, _ =>
            {
                if (!request.Options.Catalog) return new(StageStatus.Skipped, "Not selected.");
                if (source is null) return new(StageStatus.Skipped, "Requires a supported source.");
                var result = CatalogStage.Run(cache, source, ToolIdentity.DecoderSha256, request.Options.ReuseCache, cancellation);
                if (result.Output is { } output) { session.Outputs = session.Outputs with { Catalog = output };
                    catalog = CatalogStage.Open(storage.PathFor(output.Directory), source.Profile); }
                return result;
            });
            session.Run(PresentationStage.Id, _ =>
            {
                if (!request.Options.Presentation) return new(StageStatus.Skipped, "Not selected.");
                if (source is null) return new(StageStatus.Skipped, "Requires a supported source.");
                var (result, binding) = PresentationStage.Run(cache, source, ToolIdentity.DecoderSha256, request.Options.ReuseCache, cancellation);
                session.Outputs = session.Outputs with { Presentation = result.Output, PresentationBinding = binding };
                return result;
            });
            session.Run(RecipeStage.Id, report =>
            {
                if (!request.Options.Recipes) return new(StageStatus.Skipped, "Not selected.");
                if (source is null || catalog is null) return new(StageStatus.Skipped, "Requires a supported source and an imported catalog.");
                string? library = request.Options.RecipeLibrary ?? CurrentRecipes(storage) ?? RecipeStage.FindLegacy(storage);
                if (library is not null) log.Info(RecipeStage.Id, "Recipe library selected.", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(library))));
                var result = RecipeStage.Run(cache, library, source.Profile, catalog.Catalog, request.Options.ReuseCache,
                    new InlineProgress<(long Done, long Total)>(p => report(p.Done, p.Total, "Copying recipe textures")), cancellation);
                session.Outputs = session.Outputs with { Recipes = result.Output };
                return result;
            });
            session.Run(ValidationStage.Id, _ => ValidationStage.Run(session));
            session.Run(PublishStage.Id, _ => PublishStage.Run(session, source is not null && source.IsUnchanged(cancellation)));
        }
        finally { source?.Dispose(); }
        return session.Manifest ?? session.BuildManifest();
    }

    /// <summary>The recipe library of the currently active import, so re-imports keep the Recipe Viewer populated.</summary>
    private static string? CurrentRecipes(PrivateStorage storage)
    {
        try
        {
            if (InstalledGameReader.ReadActiveManifest(storage) is not { Outputs.Recipes: { } recipes }) return null;
            string candidate = Path.Combine(storage.PathFor(recipes.Directory), "recipes", "viewer.json");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return null; }
    }
}
