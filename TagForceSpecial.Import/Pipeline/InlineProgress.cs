namespace TagForceSpecial.Import.Pipeline;

/// <summary>Reports synchronously on the calling thread, keeping progress ordered (unlike <see cref="Progress{T}"/>).</summary>
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
