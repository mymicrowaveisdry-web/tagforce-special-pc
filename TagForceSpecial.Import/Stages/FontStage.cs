using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Resources;
using TagForceSpecial.Import.Storage;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

public static class FontStage
{
    public const string Id = "fonts";
    public static StageResult Run(PrivateStorage storage, string source, string? fontDirectory, CancellationToken cancellation)
    {
        var missing = GameFonts.Ensure(storage, source, fontDirectory, cancellation);
        bool ready = missing.Count == 0;
        return new(ready ? StageStatus.Succeeded : StageStatus.Warning,
            ready ? "Menu and card-text PSP fonts installed and hash-verified." : "PSP fonts not found: " + string.Join(", ", missing) + ". Select a local PSP font folder in the importer.",
            Checks: [new("fonts.pinned", CheckCategory.Asset, ready ? CheckSeverity.Info : CheckSeverity.Warning,
                ready ? CheckStatus.Passed : CheckStatus.Warning, "fonts", "Both local PSP fonts match the renderer's pinned SHA-256 identities.",
                Remediation: ready ? null : "Select the local folder containing ltn4.pgf and ltn0.pgf, then repeat import. Fonts are firmware resources and are not downloaded.")]);
    }
}
