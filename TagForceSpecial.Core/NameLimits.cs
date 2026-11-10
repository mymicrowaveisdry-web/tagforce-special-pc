namespace TagForceSpecial.Core;

/// <summary>What a by-name limit counts: activations (a negated activation does not count) or uses (it does).</summary>
public enum LimitCounts { Activations, Uses }

/// <summary>
/// A limit that every copy a player controls shares: "activate 1 X per turn", "use this effect of X once per turn",
/// or the same once per Duel. Definitions with the same key share one count.
/// </summary>
public sealed record NameLimit
{
    public string Key { get; }
    public LimitCounts Counts { get; }
    public bool PerDuel { get; }

    public NameLimit(string key, LimitCounts counts = LimitCounts.Uses, bool perDuel = false)
    {
        if (string.IsNullOrWhiteSpace(key) || !Enum.IsDefined(counts)) throw new ArgumentException("A name limit needs a key and a known count.", nameof(key));
        Key = key; Counts = counts; PerDuel = perDuel;
    }
}

internal sealed record NamedUse(PlayerId Player, string Key, int TurnNumber, bool PerDuel);

public sealed partial class DuelEngine
{
    private static bool NameLimitReached(DuelState state, PlayerId player, NameLimit limit) =>
        state.Bookkeeping.NamedUses.Any(u => u.Player == player && u.Key == limit.Key && (u.PerDuel || u.TurnNumber == state.TurnNumber));

    private static DuelState RecordNamedUse(DuelState state, PlayerId player, NameLimit limit) => state.WithBookkeeping(state.Bookkeeping with
    {
        NamedUses = Array.AsReadOnly(state.Bookkeeping.NamedUses.Where(u => u.PerDuel || u.TurnNumber == state.TurnNumber)
            .Append(new(player, limit.Key, state.TurnNumber, limit.PerDuel)).ToArray())
    });

    /// <summary>Takes back the use a negated activation recorded, for limits that count activations.</summary>
    private static DuelState ForgetNamedUse(DuelState state, PlayerId player, NameLimit limit)
    {
        var uses = state.Bookkeeping.NamedUses.ToList();
        int last = uses.FindLastIndex(u => u.Player == player && u.Key == limit.Key);
        if (last < 0) return state;
        uses.RemoveAt(last);
        return state.WithBookkeeping(state.Bookkeeping with { NamedUses = uses.AsReadOnly() });
    }
}
