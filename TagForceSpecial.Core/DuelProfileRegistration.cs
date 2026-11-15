using System.Collections.Frozen;

namespace TagForceSpecial.Core;

/// <summary>Validates immutable profile registrations and builds their ordered lookup tables at the caller's validation stage.</summary>
internal static class DuelProfileRegistration
{
    internal static (FrozenDictionary<uint, SpecialSummonCardProfile> Cards, FrozenDictionary<string, SpecialSummonProcedureProfile> Procedures)
        SpecialSummons(IEnumerable<SpecialSummonCardProfile>? experimentalSpecialSummonCards,
            IEnumerable<SpecialSummonProcedureProfile>? experimentalSpecialSummonProcedures, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        bool IsMonster(uint id) => rules.TryGetValue(id, out var rule) && rule.Category == CardCategory.Monster;
        var specialCards = (experimentalSpecialSummonCards ?? []).ToArray();
        var specialProcedures = (experimentalSpecialSummonProcedures ?? []).ToArray();
        if (specialCards.Any(p => p is null || !IsMonster(p.CardId)) ||
            specialCards.Select(p => p.CardId).Distinct().Count() != specialCards.Length ||
            specialProcedures.Any(p => p is null || p.CardIds.Any(id => !specialCards.Any(c => c.CardId == id))) ||
            specialProcedures.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != specialProcedures.Length)
            throw new ArgumentException("Special Summon procedures require distinct IDs and explicitly profiled catalog monsters.");
        var cards = specialCards.ToFrozenDictionary(p => p.CardId);
        var registeredProcedures = specialProcedures.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
        return (cards, registeredProcedures);
    }

    internal static FrozenDictionary<string, ExtraDeckMaterialProcedureProfile> Materials(
        IEnumerable<ExtraDeckMaterialProcedureProfile>? experimentalMaterialProcedures,
        IReadOnlyDictionary<uint, SpecialSummonCardProfile> specialSummonCards,
        IReadOnlyDictionary<string, SpecialSummonProcedureProfile> specialSummonProcedures, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        bool IsTokenId(uint id) => rules.TryGetValue(id, out var rule) && rule.IsToken;
        var materialProfiles = (experimentalMaterialProcedures ?? []).ToArray();
        if (materialProfiles.Any(p => p is null || p.TargetCardIds.Any(id => !specialSummonCards.ContainsKey(id) ||
                !rules.TryGetValue(id, out var rule) || !rule.AllowsExtraDeck) ||
                p.MaterialCardIds.Any(id => !rules.TryGetValue(id, out var rule) || !rule.AllowsSummonMaterial)) ||
            materialProfiles.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != materialProfiles.Length ||
            materialProfiles.Any(p => specialSummonProcedures.ContainsKey(p.ProcedureId)) ||
            materialProfiles.Any(p => p.Xyz is not null && p.MaterialCardIds.Any(IsTokenId)))
            throw new ArgumentException("Material procedures require distinct IDs and explicit Extra Deck, target and material eligibility.");
        return materialProfiles.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
    }

    internal static (FrozenDictionary<string, TributeProcedureProfile> Tributes, FrozenSet<uint> Unnegatable,
        FrozenSet<uint> Unanswerable, FrozenSet<uint> Windows, string[] EffectIds) OrdinarySummons(
        SummonLevelModifierProfile[] summonLevelModifiers, TributeProcedureProfile[] procedures, SummonWindowProfile[] windows,
        IReadOnlyDictionary<uint, DuelCardRules> rules, IReadOnlyDictionary<uint, SpecialSummonCardProfile> specialSummonCards, string[] eventIds)
    {
        bool IsMonster(uint id) => rules.TryGetValue(id, out var rule) && rule.Category == CardCategory.Monster;
        if (summonLevelModifiers.Any(p => p is null || !IsMonster(p.SourceCardId) || p.TargetCardIds.Any(id => !IsMonster(id))) ||
            procedures.Any(p => p is null || !rules.TryGetValue(p.CardId, out var rule) || !rule.AllowsTributeSummon ||
                // A listed card without rules can never be on the field; one with rules must be able to be Tributed.
                p.AllowedTributeCardIds.Any(id => rules.TryGetValue(id, out var donor) && !donor.AllowsTributeForSummon)) ||
            windows.Any(p => p is null || !rules.TryGetValue(p.CardId, out var rule) ||
                !rule.AllowsNormalSummon && !specialSummonCards.ContainsKey(p.CardId)) ||
            procedures.Select(p => p.ProcedureId).Distinct(StringComparer.Ordinal).Count() != procedures.Length ||
            windows.Select(p => p.CardId).Distinct().Count() != windows.Length ||
            rules.Any(entry => entry.Value.RequiresRegisteredTributeProcedure && !procedures.Any(p => p.CardId == entry.Key)))
            throw new ArgumentException("Summon profiles require explicit source/target eligibility, distinct registrations and procedures for restricted targets.");
        var summonIds = summonLevelModifiers.Select(p => p.EffectId).ToArray();
        if (summonIds.Distinct(StringComparer.Ordinal).Count() != summonIds.Length ||
            summonIds.Any(eventIds.Contains))
            throw new ArgumentException("Summon effect IDs must be distinct and unreserved.");
        var tributes = procedures.ToFrozenDictionary(p => p.ProcedureId, StringComparer.Ordinal);
        var unnegatable = procedures.Where(p => p.CannotBeNegated).Select(p => p.CardId).ToFrozenSet();
        var unanswerable = procedures.Where(p => p.NoResponses).Select(p => p.CardId).ToFrozenSet();
        var registeredWindows = windows.Select(p => p.CardId).ToFrozenSet();
        return (tributes, unnegatable, unanswerable, registeredWindows, summonIds);
    }

    internal static FrozenDictionary<uint, RuleLockProfile[]> Locks(IEnumerable<RuleLockProfile>? ruleLocks, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        var lockList = (ruleLocks ?? []).ToArray();
        if (lockList.Any(l => l is null || l.Lock.Actions == LockedAction.None || !rules.TryGetValue(l.CardId, out var rule) ||
                l.Lock.Scope == LockScope.Equipped && rule.Category != CardCategory.Spell || l.Lock.Scope == LockScope.Self && rule.Category != CardCategory.Monster ||
                l.Lock.UsesPerTurn < 0 || l.Lock.UsesPerTurn != 0 && (l.Lock.Actions & ~(LockedAction.BeDestroyedByBattle | LockedAction.BeDestroyedByEffect)) != 0) ||
            lockList.Any(l => l.PendulumZone && l.Lock.Scope != LockScope.Side) ||
            lockList.Select(l => l.EffectId).Distinct(StringComparer.Ordinal).Count() != lockList.Length)
            throw new ArgumentException("Rule locks need distinct IDs, known source cards and a scope that suits the source.", nameof(ruleLocks));
        return lockList.GroupBy(l => l.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
    }

    internal static FrozenDictionary<uint, BattleAbilityProfile[]> Battle(IEnumerable<BattleAbilityProfile>? battleAbilities, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        var battleProfiles = (battleAbilities ?? []).ToArray();
        if (battleProfiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.EffectId) || p.Abilities is null ||
                p.Abilities.AttacksPerBattlePhase < 1 || !Enum.IsDefined(p.Scope) || !Enum.IsDefined(p.Side) ||
                !rules.TryGetValue(p.CardId, out var rule) || p.Scope == LockScope.Self && rule.Category != CardCategory.Monster ||
                p.Scope == LockScope.Equipped && rule.Category != CardCategory.Spell) ||
            battleProfiles.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != battleProfiles.Length)
            throw new ArgumentException("Battle abilities require distinct IDs, known sources and valid scopes and attack counts.", nameof(battleAbilities));
        return battleProfiles.GroupBy(p => p.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
    }

    internal static FrozenDictionary<uint, DestructionReplacementProfile[]> Replacements(IEnumerable<DestructionReplacementProfile>? destructionReplacements, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        var replacementList = (destructionReplacements ?? []).ToArray();
        if (replacementList.Any(p => p is null || string.IsNullOrWhiteSpace(p.EffectId) || p.Cost is null || !Enum.IsDefined(p.Subject) ||
                !Enum.IsDefined(p.Cost.Action) || p.Cause is not (DestructionCause.Battle or DestructionCause.Effect or DestructionCause.Any) ||
                p.Cost.LifePoints < 0 || !Enum.IsDefined(p.Side) || !rules.TryGetValue(p.CardId, out var replacementRule) ||
                p.Subject == ReplacementSubject.Equipped && replacementRule.Category != CardCategory.Spell ||
                p.Cost.Action == ReplacementAction.DetachMaterial && replacementRule.Category != CardCategory.Monster ||
                p.Cost.Action == ReplacementAction.SendSourceToGraveyard && replacementRule.Category == CardCategory.Monster) ||
            replacementList.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != replacementList.Length)
            throw new ArgumentException("Destruction replacements need distinct IDs, known sources and costs that suit the source.", nameof(destructionReplacements));
        return replacementList.GroupBy(p => p.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
    }

    internal static FrozenDictionary<uint, MonsterStatProfile> Statistics(IEnumerable<MonsterStatProfile>? monsterStats, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        var statList = (monsterStats ?? []).ToArray();
        if (statList.Any(p => p is null || string.IsNullOrWhiteSpace(p.EffectId) || !rules.TryGetValue(p.CardId, out var statRule) || statRule.Category != CardCategory.Monster ||
                p.PerHandCard < 0 || p.ReducedLevel is 0 or > 12 || p.ReducedAttack < 0) || statList.Select(p => p.CardId).Distinct().Count() != statList.Length)
            throw new ArgumentException("Monster stat rules need one known monster each and valid values.", nameof(monsterStats));
        return statList.ToFrozenDictionary(p => p.CardId);
    }

    internal static (FrozenDictionary<uint, GraveyardRedirectProfile[]> Redirects, FrozenDictionary<uint, DamageRuleProfile[]> Damage)
        RedirectsAndDamage(IEnumerable<GraveyardRedirectProfile>? graveyardRedirects, IEnumerable<DamageRuleProfile>? damageRules,
            IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        var redirectList = (graveyardRedirects ?? []).ToArray();
        var damageList = (damageRules ?? []).ToArray();
        if (redirectList.Any(p => p is null || string.IsNullOrWhiteSpace(p.EffectId) || p.Rule is null || !Enum.IsDefined(p.Rule.To) || !Enum.IsDefined(p.Rule.Owner) ||
                !rules.ContainsKey(p.CardId)) ||
            damageList.Any(p => p is null || string.IsNullOrWhiteSpace(p.EffectId) || p.Rule is null || !Enum.IsDefined(p.Rule.Kind) ||
                (p.Rule.Applies & DamageKinds.Any) == 0 || !Enum.IsDefined(p.Side) || !rules.ContainsKey(p.CardId)) ||
            redirectList.Select(p => p.EffectId).Concat(damageList.Select(p => p.EffectId)).Distinct(StringComparer.Ordinal).Count() != redirectList.Length + damageList.Length)
            throw new ArgumentException("Graveyard redirects and damage rules need distinct IDs, known source cards and defined values.", nameof(graveyardRedirects));
        var redirects = redirectList.GroupBy(p => p.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        var damage = damageList.GroupBy(p => p.CardId).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        return (redirects, damage);
    }
    internal static string[] EventIds(TriggerEffectProfile[] triggerEffects, ContinuousEffectProfile[] continuousEffects,
        ContinuousNegationProfile[] continuousNegations)
    {
        var eventIds = triggerEffects.Select(p => p.EffectId).Concat(continuousEffects.Select(p => p.EffectId))
            .Concat(continuousNegations.Select(p => p.EffectId)).ToArray();
        if (eventIds.Distinct(StringComparer.Ordinal).Count() != eventIds.Length)
            throw new ArgumentException("Event effect IDs must be distinct and unreserved.");
        return eventIds;
    }

}
