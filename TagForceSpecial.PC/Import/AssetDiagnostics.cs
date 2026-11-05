using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.PC.Import;

public sealed record AssetRequirement(string Screen, string AssetKey);
public sealed record AssetDiagnostic(string Screen, string AssetKey, string Status, string Source, string Detail);

public static class AssetDiagnostics
{
    public static IReadOnlyList<AssetRequirement> DefaultRequirements { get; } = Array.AsReadOnly(new[]
    {
        new AssetRequirement("title", "title.logo"), new("title", "audio.bgm.title"),
        new("main-menu", "menu.main.background"), new("main-menu", "menu.main.focus-bar"),
        new("deck-editor", "ui.font.main"), new("duel", "duel.card.back"),
        new("story", "story.map.0")
    }.Concat(PresentationReferenceCases.Cases.Select(c => new AssetRequirement(c.Screen, c.AssetKey))).ToArray());

    public static IReadOnlyList<AssetDiagnostic> Inspect(SemanticAssetRegistry? registry,
        IEnumerable<AssetRequirement> requirements, bool invalidPackage = false)
    {
        var rows = new List<AssetDiagnostic>();
        foreach (var requirement in requirements)
        {
            if (invalidPackage)
            { rows.Add(new(requirement.Screen, requirement.AssetKey, "invalid", "unavailable", "Cache validation failed; rebuild the presentation generation.")); continue; }
            if (registry is null || !registry.Entries.TryGetValue(requirement.AssetKey, out var entry))
            { rows.Add(new(requirement.Screen, requirement.AssetKey, "missing", "unavailable", "No validated semantic resource is registered.")); continue; }
            string attribution = entry.Source.Archive + (entry.Source.Member is null ? "" : " / " + entry.Source.Member);
            try
            {
                _ = registry.Resolve(requirement.AssetKey);
                string? referenceScope = registry.VerifiedReferenceScope(requirement.AssetKey);
                rows.Add(new(requirement.Screen, requirement.AssetKey,
                    referenceScope is not null ? "verified" : entry.Output.PixelSha256 is not null ? "decoded-unverified" : "unresolved", attribution,
                    (referenceScope is not null ? "TEXTURE PIXELS VERIFIED; LAYOUT UNVERIFIED. " : "") +
                    "SHA256 " + entry.Source.SourceSha256 + " OFFSET " + entry.Source.Offset));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { rows.Add(new(requirement.Screen, requirement.AssetKey, "invalid", attribution, "Resource integrity check failed.")); }
        }
        return rows.AsReadOnly();
    }

    public static IReadOnlyList<string> Statuses { get; } = Array.AsReadOnly(new[] { "verified", "decoded-unverified", "unresolved", "missing", "invalid" });
}
