using static TagForceSpecial.Core.Views.ActionCandidate;

namespace TagForceSpecial.Core.Views;

/// <summary>Stable bounded candidate combinations, trigger orders and attack enumeration.</summary>
internal static class ActionCandidateEnumeration
{
    internal const int MaximumTriggerOrders = 256;

    internal static IEnumerable<CardInstance[]> Combinations(IReadOnlyList<CardInstance> cards, int count, int start)
    {
        if (count == 0) { yield return []; yield break; }
        for (int i = start; i <= cards.Count - count; i++)
            foreach (var rest in Combinations(cards, count - 1, i + 1)) yield return [cards[i], .. rest];
    }

    internal static IEnumerable<TriggerActivation[]> TriggerOrders(IReadOnlyList<TriggerActivation> options, bool mandatory, TriggerActivation[] prefix)
    {
        if (!mandatory || prefix.Length == options.Count) yield return prefix;
        foreach (var next in options.Except(prefix))
            foreach (var order in TriggerOrders(options, mandatory, [.. prefix, next])) yield return order;
    }

    internal static IEnumerable<int[]> ZoneCombinations(int[] zones, int count, int start)
    {
        if (count == 0) { yield break; }
        for (int i = start; i <= zones.Length - count; i++)
            if (count == 1) yield return [zones[i]];
            else foreach (var rest in ZoneCombinations(zones, count - 1, i + 1)) yield return [zones[i], .. rest];
    }

    /// <summary>Sets of 0 to 2 occupied Monster Zones, the Tribute choices a Toon Summon can use.</summary>
    internal static IEnumerable<int[]> ZoneSubsets(DuelPlayerState own)
    {
        int[] occupied = Enumerable.Range(0, 5).Where(z => own.MonsterZones[z] is not null).ToArray();
        yield return [];
        for (int i = 0; i < occupied.Length; i++)
        {
            yield return [occupied[i]];
            for (int j = i + 1; j < occupied.Length; j++) yield return [occupied[i], occupied[j]];
        }
    }

    internal static IEnumerable<ActionCandidate> Attacks(DuelPlayerState own, PlayerId player)
    {
        for (int z = 0; z < 5; z++)
        {
            for (int target = 0; target < 5; target++)
                yield return B(new DeclareAttack(player, z, target), ActionKind.Attack, $"Attack {z + 1} -> {target + 1}", own.MonsterZones[z]?.Card.InstanceId, z, target);
            yield return B(new DeclareAttack(player, z), ActionKind.Attack, $"Direct attack [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z);
        }
    }
}
