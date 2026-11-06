namespace TagForceSpecial.PC.Rendering;

/// <summary>Bind on the MonoGame thread; worker tasks may pass CPU data only.</summary>
public sealed class GameThreadGuard
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    public void Check()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("GPU resource access must remain on the MonoGame game thread.");
    }
}
