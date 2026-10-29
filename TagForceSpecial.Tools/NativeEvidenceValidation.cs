namespace TagForceSpecial.Tools;

public sealed record NativeEvidenceReview(int SchemaVersion, string Module, string Address, int Length,
    string SourceSha256, string OriginalSha256, string RebuiltSha256, string ReconstructedBy, string ValidatedBy,
    string Decision, string ValidationCommand, ResearchEvidence Source, ResearchEvidence Comparison);
public sealed record NativeComparisonSummary(int SchemaVersion, string Status, string Module, string Address,
    string SourceSha256, string ReconstructedSourceSha256, string OriginalSha256, string RebuiltSha256, int OriginalLength, int RebuiltLength,
    int DifferenceCount, string CompilerVersion, string[] CompileFlags, string PrivateComparisonReportSha256);

/// <summary>Public metadata gate only. Authentic byte validation must also run independently with private inputs.</summary>
public static class NativeEvidenceValidation
{
    public static void ValidateReview(string root, NativeFunctionMatch match, ResearchQueue queue)
    {
        var review = ResearchValidation.Read<NativeEvidenceReview>(File.ReadAllText(Path.Combine(root, match.EvidencePath)));
        if (review.SchemaVersion != 1 || review.Module != match.Module || review.Address != match.Address || review.Length != match.Length ||
            !Equal(review.SourceSha256, match.SourceSha256) || !Equal(review.OriginalSha256, match.OriginalSha256) ||
            !Equal(review.RebuiltSha256, match.RebuiltSha256) || review.Decision != "accepted" ||
            string.IsNullOrWhiteSpace(review.ValidationCommand) || review.ReconstructedBy == review.ValidatedBy)
            throw new InvalidDataException("Native evidence needs a complete independently accepted review.");
        var reconstruction = queue.WorkUnits.SingleOrDefault(w => w.Id == review.ReconstructedBy);
        var validation = queue.WorkUnits.SingleOrDefault(w => w.Id == review.ValidatedBy);
        var function = queue.Functions.SingleOrDefault(f => f.Module == match.Module && f.Address == match.Address);
        if (function is null || function.Length != match.Length || function.Classification != "game" || function.BoundaryConfidence != "corroborated" ||
            reconstruction?.Role != "native" || validation?.Role != "analysis" ||
            reconstruction.FunctionId != function.Id || validation.FunctionId != function.Id ||
            !reconstruction.Outputs.Contains(review.Source.Path, StringComparer.Ordinal))
            throw new InvalidDataException("Native review must link distinct native and analysis work units to a classified function.");
        ResearchValidation.ValidateEvidence(review.Source);
        ResearchValidation.ValidateEvidence(review.Comparison);
        if (review.Source.Availability != "committed" || review.Source.Kind != "source" ||
            review.Comparison.Availability != "committed" || review.Comparison.Kind != "comparison")
            throw new InvalidDataException("Native review requires committed reconstructed source and sanitized comparison metadata.");
        ResearchValidation.ValidateEvidenceFile(root, review.Source, false);
        ResearchValidation.ValidateEvidenceFile(root, review.Comparison, false);
        var summary = ResearchValidation.Read<NativeComparisonSummary>(File.ReadAllText(Path.Combine(root, review.Comparison.Path)));
        if (summary.SchemaVersion != 1 || summary.Status != "compared" || summary.Module != match.Module || summary.Address != match.Address ||
            !Equal(summary.SourceSha256, match.SourceSha256) || !Equal(summary.ReconstructedSourceSha256, review.Source.Sha256) ||
            !Equal(summary.OriginalSha256, match.OriginalSha256) ||
            !Equal(summary.RebuiltSha256, match.RebuiltSha256) || summary.OriginalLength != match.Length ||
            summary.RebuiltLength != match.Length || summary.DifferenceCount != 0 ||
            string.IsNullOrWhiteSpace(summary.CompilerVersion) || summary.CompileFlags is not { Length: > 0 } ||
            !ResearchValidation.Sha(summary.PrivateComparisonReportSha256))
            throw new InvalidDataException("Native comparison summary must record a real compiler trial and a complete zero-difference result.");
    }

    private static bool Equal(string? a, string? b) => ResearchValidation.Sha(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
