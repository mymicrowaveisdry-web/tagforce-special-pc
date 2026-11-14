using System.Collections.Frozen;

namespace TagForceSpecial.Core;

/// <summary>Compiles activation decision sequences and definition metadata without changing duel state.</summary>
internal static class EffectDefinitionRegistration
{
    internal static string SequenceId(EffectDefinition definition) => $"activation:{definition.EffectId}";

    internal static bool RequiresSeed(EffectDefinition definition) => definition.Steps.Any(s =>
        s is ShuffleDeckStep or AddFromDeckToHandStep or SendFromDeckToGraveyardStep or ShuffleTargetsIntoDeckStep ||
        s is SpecialSummonStep summon && summon.Sources.HasFlag(SpecialSummonSources.Deck));

    internal static FrozenDictionary<string, DecisionSequence> Decisions(EffectDefinition[] effectDefinitions,
        TriggerEffectProfile[] triggerEffects, Func<EffectStep, IEnumerable<CardFilter>> stepFilters)
    {
        var definitionList = effectDefinitions;
        var result = definitionList.Where(d => d.Cost is DiscardCost or TributeCost or BanishFromGraveyardCost || d.Target is not null || d.FieldTargets.Any(t => !EffectDefinition.IsAutomatic(t)))
            .ToFrozenDictionary(d => d.EffectId, d => new DecisionSequence(SequenceId(d),
                [.. d.Cost is DiscardCost discard ? Enumerable.Repeat(discard.Filter switch
                     {
                         CardIdFilter only => DecisionStep.Card(DecisionCardSource.Hand, only.CardIds),
                         { } other => DecisionStep.FilteredCard(DecisionCardSource.Hand, other),
                         null => DecisionStep.AnyCard(DecisionCardSource.Hand)
                     }, discard.Count) : Array.Empty<DecisionStep>(),
                 .. d.Cost is TributeCost tribute ? Enumerable.Repeat(DecisionStep.FieldTargetChoice(new FieldTarget(FieldCardKind.Monster, TargetSide.Own, tribute.Position, cardFilter: tribute.Filter) { ForTribute = true }, optional: false), tribute.Count) : Array.Empty<DecisionStep>(),
                 .. d.Cost is BanishFromGraveyardCost banish ? Enumerable.Repeat(DecisionStep.FieldTargetChoice(new ZoneTarget(CardZone.Graveyard, TargetSide.Own, banish.Filter)), banish.Count) : Array.Empty<DecisionStep>(),
                 .. d.Target is SpellTrapOnFieldTarget ? [DecisionStep.SpellTrapTarget()] : Array.Empty<DecisionStep>(),
                 .. d.FieldTargets.Where(t => !EffectDefinition.IsAutomatic(t)).SelectMany(spec => Enumerable.Range(0, EffectDefinition.MaxCount(spec))
                     .Select(i => DecisionStep.FieldTargetChoice(spec, optional: i >= EffectDefinition.MinCount(spec))))],
                stopAfterCompletion: false));
        static bool UsesDeclared(CardFilter filter) => filter switch
        {
            DeclaredFilter => true, AnyOfFilter any => any.Filters.Any(UsesDeclared), AllOfFilter all => all.Filters.Any(UsesDeclared),
            NotFilter not => UsesDeclared(not.Filter), _ => false
        };
        if (definitionList.Concat(triggerEffects.Select(p => p.Definition).OfType<EffectDefinition>())
                .Any(d => d.Declares is null && d.Steps.SelectMany(stepFilters).Any(UsesDeclared)))
            throw new ArgumentException("A declared filter belongs to an effect that declares.", nameof(effectDefinitions));
        return result;
    }
    internal static void Validate(EffectDefinition[] effectDefinitions, IReadOnlySet<uint> knownIds,
        IReadOnlyDictionary<uint, DuelCardRules> rules, IReadOnlySet<string> usedEffectIds,
        IReadOnlyDictionary<uint, SummonEffectProfile> summonEffects, IReadOnlyDictionary<string, DecisionSequence> decisionSequences,
        TriggerEffectProfile[] triggerEffects, ulong? shuffleSeed)
    {
        var definitionList = effectDefinitions;
        bool IsTokenId(uint id) => rules.TryGetValue(id, out var rule) && rule.IsToken;
        // Definitions need matching settable card rules, Token rules for Token steps and a seed for shuffles.
        if (definitionList.Any(d => d is null || d.Cost is DiscardCost { Filter: CardIdFilter discardFilter } && discardFilter.CardIds.Any(id => !knownIds.Contains(id)) ||
                d.Steps.OfType<SummonTokensStep>().Any(s => s.TokenCardIds.Any(id => !IsTokenId(id))) ||
                d.Kind == EffectKind.MonsterTrigger || shuffleSeed is null && RequiresSeed(d) ||
                // A Pendulum Effect belongs to a monster; without a registered scale it never applies.
                !rules.TryGetValue(d.CardId, out var rule) || (d.Origin == EffectOrigin.PendulumZone ? rule.Category != CardCategory.Monster :
                d.IsMonsterEffect ? rule.Category != CardCategory.Monster : d.IsFaceUpEffect ? rule.Category is not (CardCategory.Spell or CardCategory.Trap) : !rule.AllowsSpellTrapSet ||
                rule.Category != (d.Kind is EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell ? CardCategory.Spell : CardCategory.Trap)) ||

                summonEffects.ContainsKey(d.CardId) || usedEffectIds.Contains(d.EffectId) ||
                decisionSequences.ContainsKey(SequenceId(d))) ||
            !EffectGroupsValid(definitionList) ||
            definitionList.Select(d => d.EffectId).Distinct(StringComparer.Ordinal).Count() != definitionList.Length)
            throw new ArgumentException("Effect definitions require distinct, unreserved IDs, matching settable card rules, Token rules for Token steps and a seed for shuffle steps.", nameof(effectDefinitions));
        if (definitionList.Concat(triggerEffects.Where(p => p.Definition is not null).Select(p => p.Definition!))
            .Any(d => d.Steps.OfType<ChangeTargetsNameStep>().Any(s => !knownIds.Contains(s.NameCardId))))
            throw new ArgumentException("Name changes must reference a catalog card.");
        if (shuffleSeed is null && triggerEffects.Any(p => p.Definition is { } d && RequiresSeed(d)))
            throw new ArgumentException("Triggers that search or Summon from the Deck need a seed.", nameof(shuffleSeed));
    }

    private static bool EffectGroupsValid(EffectDefinition[] effects) => effects.GroupBy(d => d.CardId).All(group =>
    {
        var kinds = group.Where(d => d.Origin == EffectOrigin.Field).Select(d => d.Kind).ToArray();
        if (kinds.Length == 0) return group.All(d => d.IsMonsterEffect || d.Origin == EffectOrigin.PendulumZone);
        if (kinds.Any(k => k is EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick))
            return kinds[0] is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell && kinds.Skip(1).All(k => k is EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick);
        return kinds.Length == 1 || kinds.All(k => k is EffectKind.MonsterIgnition or EffectKind.MonsterQuick) ||
            kinds.All(k => k == kinds[0] && k is EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap);
    });

}
