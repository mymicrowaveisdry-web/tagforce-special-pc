namespace TagForceSpecial.Core;

/// <summary>
/// Engine bookkeeping carried by every state snapshot: instance allocation, shuffle RNG, open window and
/// turn-scoped summon locks.
/// </summary>
internal sealed record DuelBookkeeping(int NextInstanceId = 1, DuelRandomState? Random = null, PendingOpenWindow? OpenWindow = null,
    IReadOnlyList<SummonLock>? SummonLocks = null)
{
    public IReadOnlyList<SummonLock> SummonLocks { get; init; } = SummonLocks ?? Array.AsReadOnly(Array.Empty<SummonLock>());
    /// <summary>The latest Summon, kept until play returns to an open state so summon conditions can answer it.</summary>
    public IReadOnlyList<SummonedMonster>? RecentSummon { get; init; }
    /// <summary>Once-per-turn activated effects used so far this turn, by card instance.</summary>
    public IReadOnlyList<ActivatedEffectUse> ActivatedUses { get; init; } = Array.AsReadOnly(Array.Empty<ActivatedEffectUse>());
}

internal sealed record ActivatedEffectUse(int InstanceId, int TurnNumber);

/// <summary>
/// A turn player's phase-change request, or a just-completed Summon, paused so the other player can activate a set
/// Quick-Play Spell or Normal Trap, or pass. Opened only when such an activation is legal.
/// </summary>
public sealed record PendingOpenWindow(long DecisionId, PlayerId Responder, DuelPhase RequestedPhase)
{
    /// <summary>Set for a summon-response window; passing it changes no phase.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SummonedMonster>? Summoned { get; init; }
}
public sealed record OpenWindowRequested(PlayerId Responder, long DecisionId, DuelPhase RequestedPhase) : DuelEvent;
public sealed record OpenWindowPassed(PlayerId Responder) : DuelEvent;

/// <summary>xoshiro128** generator state, seeded through SplitMix64 from the duel seed.</summary>
public readonly record struct DuelRandomState(uint S0, uint S1, uint S2, uint S3)
{
    public static DuelRandomState FromSeed(ulong seed)
    {
        ulong x = seed;
        ulong Next()
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        ulong a = Next(), b = Next();
        var state = new DuelRandomState((uint)a, (uint)(a >> 32), (uint)b, (uint)(b >> 32));
        // The all-zero state is the generator's only invalid state.
        return state is { S0: 0, S1: 0, S2: 0, S3: 0 } ? new(1, 0, 0, 0) : state;
    }

    /// <summary>One xoshiro128** output and the advanced state (pure; the receiver is unchanged).</summary>
    public (uint Value, DuelRandomState Next) NextUInt32()
    {
        uint result = uint.RotateLeft(S1 * 5, 7) * 9;
        uint t = S1 << 9;
        uint s2 = S2 ^ S0, s3 = S3 ^ S1;
        uint s1 = S1 ^ s2, s0 = S0 ^ s3;
        s2 ^= t;
        s3 = uint.RotateLeft(s3, 11);
        return (result, new(s0, s1, s2, s3));
    }

    /// <summary>Unbiased value in [0, bound) by Lemire's multiply-and-reject method.</summary>
    internal (int Value, DuelRandomState Next) NextBelow(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        var state = this;
        uint limit = (uint)bound;
        uint threshold = (0u - limit) % limit;
        while (true)
        {
            (uint x, state) = state.NextUInt32();
            ulong m = (ulong)x * limit;
            if ((uint)m >= threshold) return ((int)(m >> 32), state);
        }
    }

    /// <summary>Fisher–Yates shuffle; returns the permuted copy and the advanced state.</summary>
    internal (T[] Items, DuelRandomState Next) Shuffle<T>(IReadOnlyList<T> items)
    {
        var copy = items.ToArray();
        var state = this;
        for (int i = copy.Length - 1; i > 0; i--)
        {
            (int j, state) = state.NextBelow(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return (copy, state);
    }
}
