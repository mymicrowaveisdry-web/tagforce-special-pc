namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private static uint[] ValidateNegationDependencies(ContinuousNegationProfile[] profiles)
    {
        var sources = profiles.Select(p => p.CardId).Distinct().Order().ToArray();
        var edges = sources.ToDictionary(id => id, id => profiles.Where(p => p.CardId == id)
            .SelectMany(p => p.TargetCardIds).Where(sources.Contains).Distinct().ToArray());
        var indegree = sources.ToDictionary(id => id, id => edges.Values.Count(targets => targets.Contains(id)));
        List<uint> ordered = [];
        var ready = new SortedSet<uint>(sources.Where(id => indegree[id] == 0));
        while (ready.Count != 0)
        {
            uint id = ready.Min; ready.Remove(id); ordered.Add(id);
            foreach (uint target in edges[id]) if (--indegree[target] == 0) ready.Add(target);
        }
        if (ordered.Count != sources.Length)
            throw new ArgumentException("Cyclic/self-negating continuous dependencies require an unsupported timing/precedence contract.");
        return ordered.ToArray();
    }

    private MonsterBinding[] CalculateNegatedMonsters(DuelState state, IEnumerable<MonsterEffectNegation> timed)
    {
        var monsters = FieldMonsters(state).Where(e => e.Monster.Position != BattlePosition.FaceDownDefense).ToArray();
        var negated = timed.Where(n => n.ExpiresOnTurn == state.TurnNumber && state.Phase != DuelPhase.End &&
            n.Target.IsStillPresent(state)).Select(n => n.Target).ToHashSet();
        // Dependency order, not card/zone priority. Cyclic dependencies fail closed at registration.
        foreach (uint id in negationOrder)
            foreach (var (source, _) in monsters.Where(e => e.Monster.Card.GameCardId == id && !e.Monster.BattleDestroyed))
                if (!negated.Contains(source))
                    foreach (var profile in continuousNegations.Where(p => p.CardId == id).OrderBy(p => p.EffectId, StringComparer.Ordinal))
                        foreach (var (target, _) in monsters)
                            if (profile.TargetCardIds.Contains(target.Card.GameCardId) && Relates(profile.Affects, source, target))
                                negated.Add(target);
        return negated.OrderBy(n => n.Player).ThenBy(n => n.Zone).ToArray();
    }

    private static bool IsSourceEffectNegated(DuelState state, TriggerActivation trigger) =>
        trigger.Profile.SourceLocation == TriggerSourceLocation.FaceUpMonster && trigger.Source.IsStillPresent(state) &&
        state.EventEffects.NegatedMonsters.Contains(trigger.Source);
}
