using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards.CatalogStorage;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Stages;

namespace TagForceSpecial.Import.Installed;

/// <summary>A validated import the game can use: the card catalog (with the reviewed overlay), optional presentation
/// resources and the optional recipe library directory.</summary>
public sealed class InstalledGame
{
    public string BuildId { get; }
    public string SourceSha256 { get; }
    public LoadedCardCatalog Catalog { get; }
    public SemanticAssetRegistry? Presentation { get; }
    /// <summary>Directory holding <c>recipes/viewer.json</c>, or null when no recipes were imported.</summary>
    public string? RecipeRoot { get; }
    public ImportManifest? Manifest { get; }

    internal InstalledGame(GameBuildProfile profile, LoadedCardCatalog catalog, SemanticAssetRegistry? presentation, string? recipeRoot, ImportManifest? manifest)
        => (BuildId, SourceSha256, Catalog, Presentation, RecipeRoot, Manifest) = (profile.BuildId, profile.ImageSha256, catalog, presentation, recipeRoot, manifest);

    /// <summary>Opens a directory that holds a pinned <c>catalog/</c> package (a catalog stage output or an earlier cache), without presentation or recipes.</summary>
    public static InstalledGame FromCatalogDirectory(string directory, GameBuildProfile? profile = null)
    {
        profile ??= GameBuildProfile.English21;
        return new(profile, CatalogStage.Open(Path.GetFullPath(directory), profile), null, null, null);
    }
}

public sealed record ImportIssue(string Code, string Message)
{
    public static ImportIssue For(string code) => new(code, code switch
    {
        "ImportMissing" => "Import your original ISO to prepare local game data.",
        "ActiveImportInvalid" => "The active import record is unreadable or was changed. Import your ISO again; saved decks are kept.",
        "ImportUnsupported" => "The active import comes from an unsupported build. Import the complete English v2.1 ISO.",
        "CatalogInvalid" => "The imported card catalog failed verification. Import your ISO again; saved decks are kept.",
        "PresentationInvalid" => "Original title artwork failed verification. Import your ISO again to rebuild it.",
        "PresentationMissing" => "Original title artwork was not imported. Import your ISO with presentation enabled.",
        "RecipesInvalid" => "The imported recipe library failed verification. Import it again from the importer.",
        _ => "Local game data could not be verified. Import your ISO again; saved decks are kept."
    });
}

public sealed record InstalledGameResult(InstalledGame? Game, IReadOnlyList<ImportIssue> Issues)
{
    public bool Usable => Game is not null;
}
