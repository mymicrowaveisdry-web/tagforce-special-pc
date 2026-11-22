namespace TagForceSpecial.PC.App.Visual;

/// <summary>Absolute presentation time only; no wall clock, timers or engine callbacks.</summary>
public sealed class AnimationScheduler
{
    private sealed record Track(string Owner, string Key, TimeSpan Start, TimeSpan Duration, float From, float To, Action<float> Apply);
    private readonly List<Track> tracks = [];
    public int Count => tracks.Count;
    public void Schedule(string owner, string key, TimeSpan start, TimeSpan duration, float from, float to, Action<float> apply)
    {
        if (start < TimeSpan.Zero || duration <= TimeSpan.Zero || !float.IsFinite(from) || !float.IsFinite(to))
            throw new ArgumentOutOfRangeException(nameof(duration));
        tracks.RemoveAll(t => t.Owner == owner && t.Key == key);
        tracks.Add(new(owner, key, start, duration, from, to, apply));
    }
    public void CancelOwner(string owner) => tracks.RemoveAll(t => t.Owner == owner);
    public void Update(TimeSpan presentationTime)
    {
        if (presentationTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(presentationTime));
        foreach (var track in tracks.ToArray())
        {
            if (!tracks.Contains(track)) continue;
            double t = Math.Clamp((presentationTime - track.Start).TotalSeconds / track.Duration.TotalSeconds, 0, 1);
            // Remove before invoking user code so replacement/cancellation is safe.
            if (t >= 1) tracks.Remove(track);
            track.Apply((float)(track.From + (track.To - track.From) * t));
        }
    }
}
