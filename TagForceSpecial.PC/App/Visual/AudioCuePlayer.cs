namespace TagForceSpecial.PC.App.Visual;

public readonly record struct AudioCue(string Owner, string CueId);

/// <summary>Bounded presentation events. A host sink may play only approved audio; absent audio stays silent.</summary>
public sealed class AudioCuePlayer
{
    private readonly Queue<AudioCue> pending = new();
    private readonly Action<AudioCue>? play;
    public AudioCuePlayer(Action<AudioCue>? play = null) => this.play = play;
    public int PendingCount => pending.Count;
    public void Enqueue(AudioCue cue)
    {
        if (string.IsNullOrWhiteSpace(cue.Owner) || string.IsNullOrWhiteSpace(cue.CueId)) throw new ArgumentException("Cue identity is required.");
        if (pending.Count >= 64) throw new InvalidOperationException("Audio cue budget exhausted.");
        pending.Enqueue(cue);
    }
    public void CancelOwner(string owner)
    {
        var keep = pending.Where(c => c.Owner != owner).ToArray(); pending.Clear();
        foreach (var cue in keep) pending.Enqueue(cue);
    }
    public void Update()
    {
        int count = pending.Count;
        for (int i = 0; i < count; i++) { var cue = pending.Dequeue(); play?.Invoke(cue); }
    }
}
