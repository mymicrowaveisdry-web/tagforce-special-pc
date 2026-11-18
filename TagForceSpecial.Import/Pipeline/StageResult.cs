using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Validation;

namespace TagForceSpecial.Import.Pipeline;

/// <summary>What one stage reports back to the pipeline runner.</summary>
public sealed record StageResult(StageStatus Status, string Message, StageOutput? Output = null,
    IReadOnlyList<ValidationCheck>? Checks = null, long? Processed = null, long? Total = null)
{
    public IReadOnlyList<ValidationCheck> AllChecks => Checks ?? [];
}

/// <summary>Live progress. <see cref="Fraction"/> is null when the amount of work is unknown, so callers show indeterminate progress.</summary>
public sealed record ImportProgress(string StageId, string StageName, string Message, long? Processed = null, long? Total = null,
    string? CurrentItem = null)
{
    public double? Fraction => Total is > 0 && Processed is { } done ? Math.Clamp(done / (double)Total.Value, 0, 1) : null;
}
