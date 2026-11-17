using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Sources;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Stages;

/// <summary>Recognizes the selected ISO or extracted directory and checks it against the trusted build profiles. Recognition,
/// structural validity and support are recorded as three separate findings.</summary>
public static class SourceStage
{
    public const string Id = "source";

    public sealed record Outcome(StageResult Result, SourceIdentity Identity, ValidatedGameSource? Source, IReadOnlyList<ScannedFile>? Scan);

    public static Outcome Run(string path, IReadOnlyList<GameBuildProfile> profiles, IProgress<(long Done, long Total, string Message)>? progress,
        CancellationToken cancellation)
    {
        string full = Path.GetFullPath(Path.TrimEndingDirectorySeparator(path));
        var probe = SourceProbe.Probe(full);
        long length = probe.Kind == "iso" && File.Exists(full) ? new FileInfo(full).Length : 0;
        var identity = new SourceIdentity(probe.Kind, full, Path.GetFileName(full), length, null, probe.GameId, probe.Title, probe.DiscVersion,
            probe.Recognized, false, false, null, null, probe.Notes.ToArray());
        var checks = new List<ValidationCheck>
        {
            Check("source.container", CheckCategory.Source, probe.Container, "The ISO 9660 structure or directory could be opened and listed read-only."),
            Check("source.recognized", CheckCategory.Source, probe.Recognized,
                "PSP layout is present and PARAM.SFO names a known Tag Force Special disc ID.", $"{probe.GameId ?? "no disc ID"} {probe.Title}".Trim())
        };
        if (!probe.Container) return Fail("The source could not be opened.");
        IReadOnlyList<ScannedFile>? scan = null;
        GameSourceValidation? validation = null;
        GameBuildProfile? matched = null;
        if (probe.Kind == "directory")
        {
            using (var files = GameFileSystem.OpenDirectory(full))
                scan = SourceScan.Run(files, new InlineProgress<(long, long, string)>(p => progress?.Report((p.Item1, p.Item2, "Hashing " + p.Item3))), cancellation);
            string tree = SourceScan.TreeDigest(scan);
            identity = identity with { Length = scan.Sum(f => f.Entry.Length), Sha256 = tree };
            foreach (var profile in profiles)
            {
                validation = GameSourceValidator.OpenDirectory(full, profile, cancellation);
                if (validation.Source is null) continue;
                identity = identity with { StructurallyValid = true };
                if (DirectoryPins.TreeSha256.TryGetValue(profile.Fingerprint, out var pin) && pin == tree) { matched = profile; break; }
                validation.Source.Dispose(); validation = null;
                identity = identity with { Notes = [.. identity.Notes, pinNote(profile)] };
            }
        }
        else
        {
            foreach (var profile in profiles)
            {
                validation = GameSourceValidator.Open(full, profile, cancellation,
                    new InlineProgress<long>(n => progress?.Report((n, profile.ImageLength, "Hashing the original image"))));
                if (validation.Source is not null) { matched = profile; break; }
            }
            identity = identity with { StructurallyValid = probe.PspLayout, Sha256 = matched?.ImageSha256 ?? HashImage(full, progress, cancellation) };
        }
        if (matched is null || validation?.Source is null)
        {
            var codes = validation?.Diagnostics.Select(d => d.Code).Distinct().ToArray() ?? [];
            checks.Add(Check("source.supported-build", CheckCategory.Source, false,
                "Every file matches a trusted build profile (whole-image SHA-256 for an ISO; the measured PSP_GAME tree for a directory).",
                codes.Length > 0 ? string.Join(", ", codes) : identity.Notes.LastOrDefault(),
                "Select the complete original English v2.1 ISO or an unmodified extraction of it; sources are never modified."));
            checks.Add(Check("source.structure", CheckCategory.Source, identity.StructurallyValid, "PSP layout and required files are readable within bounds."));
            return new(new(StageStatus.Failed, Describe(identity, codes), Checks: checks), identity with { Notes = [.. identity.Notes, .. codes] }, null, scan);
        }
        identity = identity with { StructurallyValid = true, Supported = true, BuildId = matched.BuildId, ProfileFingerprint = matched.Fingerprint };
        checks.Add(Check("source.structure", CheckCategory.Source, true, "PSP layout and required files are readable within bounds."));
        checks.Add(Check("source.supported-build", CheckCategory.Source, true,
            "Every file matches a trusted build profile (whole-image SHA-256 for an ISO; the measured PSP_GAME tree for a directory).", matched.BuildId));
        return new(new(StageStatus.Succeeded, $"Supported build {matched.BuildId} ({probe.Kind}).", Checks: checks), identity, validation.Source, scan);

        Outcome Fail(string message) => new(new(StageStatus.Failed, message, Checks: checks), identity, null, null);
        static string pinNote(GameBuildProfile p) => DirectoryPins.TreeSha256.ContainsKey(p.Fingerprint)
            ? $"Directory files differ from build {p.BuildId}; pinned files match but other files were changed, added or removed."
            : $"No directory tree is pinned for build {p.BuildId}; import the ISO instead.";
    }

    private static string Describe(SourceIdentity identity, string[] codes) => !identity.Recognized
        ? "The source is not recognized as Tag Force Special." + (identity.Notes.Length > 0 ? " " + identity.Notes[0] : "")
        : "The source is Tag Force Special but not a supported build" + (codes.Length > 0 ? $" ({string.Join(", ", codes)})." : ".") +
          (identity.Notes.LastOrDefault() is { } note ? " " + note : "");

    private static string? HashImage(string path, IProgress<(long Done, long Total, string Message)>? progress, CancellationToken cancellation)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20]; long done = 0;
            for (int read; (read = stream.Read(buffer)) > 0;)
            {
                cancellation.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read); done += read;
                progress?.Report((done, stream.Length, "Hashing the unsupported image for the report"));
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static ValidationCheck Check(string id, string category, bool passed, string explanation, string? details = null, string? remediation = null) =>
        new(id, category, passed ? CheckSeverity.Info : CheckSeverity.Error, passed ? CheckStatus.Passed : CheckStatus.Failed, "source",
            explanation, details, passed ? null : remediation);
}
