using System.Collections.Frozen;

namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private FrozenDictionary<uint, SummonEffectProfile> ValidateSummonEffects(IEnumerable<SummonEffectProfile> profiles,
        IEnumerable<string> reservedEffectIds)
    {
        var all = profiles.ToArray(); var reserved = reservedEffectIds.ToHashSet(StringComparer.Ordinal);
        bool ValidTarget(uint id, SummonEffectProfile p) => rules.TryGetValue(id, out var r) && r.Category == CardCategory.Monster &&
            (p.Kind != EffectSummonKind.Fusion || r.AllowsExtraDeck) && specialSummonCards.TryGetValue(id, out var policy) &&
            (policy.RequiredProperProcedureId is null || policy.RequiredProperProcedureId == p.ProperProcedureId) && !IsXyzId(id);
        bool IsXyzId(uint id) => materialProcedures.Values.Any(p => p.Xyz is not null && p.TargetCardIds.Contains(id));
        bool ValidMaterial(uint id, bool ritual) => rules.TryGetValue(id, out var r) && r.Category == CardCategory.Monster &&
            (ritual ? r.AllowsTributeForSummon && catalog[id].Statistics.Level > 0 && !IsXyzId(id) : r.AllowsSummonMaterial);
        if (all.Any(p => p is null || !rules.TryGetValue(p.SpellCardId, out var source) || source.Category != CardCategory.Spell ||
                chainResponses.ContainsKey(p.SpellCardId) ||
                battleResponses.ContainsKey(p.SpellCardId) || reserved.Contains(p.EffectId) ||
                p.FusionRecipes.Any(r => !ValidTarget(r.TargetCardId, p) || r.MaterialCardIds.Any(id => !ValidMaterial(id, false))) ||
                p.RitualRecipes.Any(r => !ValidTarget(r.TargetCardId, p) || catalog[r.TargetCardId].Statistics.Level != r.RequiredLevel ||
                    r.TributeCardIds.Any(id => !ValidMaterial(id, true)))) ||
            all.Select(p => p.SpellCardId).Distinct().Count() != all.Length ||
            all.Select(p => p.EffectId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("Summon effects require distinct Spell sources/effect IDs and explicitly eligible targets/materials.");
        return all.ToFrozenDictionary(p => p.SpellCardId);
    }

    private static DecisionCardSource EffectSummonSource(SummonEffectProfile profile) =>
        profile.Kind == EffectSummonKind.Fusion ? DecisionCardSource.ExtraDeck : DecisionCardSource.Hand;

    private IEnumerable<EffectSummonMaterial> EffectSummonMaterials(DuelState state, PlayerId playerId,
        SummonEffectProfile profile, CardInstance target)
    {
        var player = state.GetPlayer(playerId);
        var allowed = profile.Kind == EffectSummonKind.Fusion
            ? profile.FusionRecipes.Single(r => r.TargetCardId == target.GameCardId).MaterialCardIds
            : profile.RitualRecipes.Single(r => r.TargetCardId == target.GameCardId).TributeCardIds;
        bool Eligible(CardInstance card) => card != target && allowed.Contains(card.GameCardId) &&
            !state.SummonHistory.Any(h => h.Card == card && h.Method == SummonMethod.Token) &&
            (profile.Kind != EffectSummonKind.Ritual || !IsXyzCard(card));
        // Field first also gives the Fusion existence check a freed slot when the field is full.
        for (int z = 0; z < 5; z++)
            if (player.MonsterZones[z] is { BattleDestroyed: false } monster &&
                (profile.AllowFaceDownFieldMaterials || monster.Position != BattlePosition.FaceDownDefense) && Eligible(monster.Card))
                yield return new(monster.Card, new(playerId, z, monster.Card, monster.PlacementId));
        foreach (var card in player.Hand.Where(Eligible)) yield return new(card, null);
    }

    private EffectSummonMaterial[]? FindEffectSummonMaterials(DuelState state, PlayerId playerId,
        SummonEffectProfile profile, CardInstance target)
    {
        var pool = EffectSummonMaterials(state, playerId, profile, target).ToArray();
        bool needsField = state.GetPlayer(playerId).MonsterZones.All(m => m is not null);
        if (profile.Kind == EffectSummonKind.Fusion)
        {
            var recipe = profile.FusionRecipes.Single(r => r.TargetCardId == target.GameCardId);
            var chosen = new List<EffectSummonMaterial>();
            foreach (var group in recipe.MaterialCardIds.GroupBy(id => id))
            {
                var matches = pool.Where(m => m.Card.GameCardId == group.Key).Take(group.Count()).ToArray();
                if (matches.Length != group.Count()) return null;
                chosen.AddRange(matches);
            }
            return needsField && chosen.All(m => m.Field is null) ? null : chosen.ToArray();
        }
        var level = profile.RitualRecipes.Single(r => r.TargetCardId == target.GameCardId).RequiredLevel;
        var failed = new HashSet<(int Index, int Count, long Total, bool Field)>();
        var selection = new List<EffectSummonMaterial>();
        bool Search(int index, long total, bool hasField)
        {
            if (total == level) return selection.Count != 0 && (!needsField || hasField);
            if (index == pool.Length || selection.Count == 5 || total > level) return false;
            var key = (index, selection.Count, total, hasField);
            if (failed.Contains(key)) return false;
            var material = pool[index];
            selection.Add(material);
            if (Search(index + 1, total + catalog[material.Card.GameCardId].Statistics.Level, hasField || material.Field is not null)) return true;
            selection.RemoveAt(selection.Count - 1);
            if (Search(index + 1, total, hasField)) return true;
            failed.Add(key); return false;
        }
        return Search(0, 0, false) ? selection.ToArray() : null;
    }

    private IEnumerable<CardInstance> LegalEffectSummonTargets(DuelState state, PlayerId player,
        SummonEffectProfile profile)
    {
        var ids = profile.FusionRecipes.Select(r => r.TargetCardId).Concat(profile.RitualRecipes.Select(r => r.TargetCardId));
        foreach (var target in SpecialSourceCards(state.GetPlayer(player), EffectSummonSource(profile)).Where(c => ids.Contains(c.GameCardId)))
        {
            if (FindEffectSummonMaterials(state, player, profile, target) is not { } materials) continue;
            var next = MoveEffectSummonMaterials(state, player, materials, profile, []);
            if (Enumerable.Range(0, 5).Any(z => CheckSpecialSummon(next, player, target, z, BattlePosition.FaceUpAttack,
                EffectSummonSource(profile), profile.ProperProcedureId) == DuelError.None)) yield return target;
        }
    }

    private DuelActionResult ActivateSummonEffect(CardInstance spell, int zone, SummonEffectProfile profile)
    {
        var player = State.GetPlayer(State.ActivePlayer);
        if (player.SpellTrapFields[zone] is not null) return Reject(DuelError.SpellTrapZoneOccupied);
        if (!LegalEffectSummonTargets(State, player.Id, profile).Any()) return Reject(DuelError.DecisionUnavailable);
        var slots = player.SpellTrapFields.ToArray();
        long placement = checked(State.Revision + 1);
        slots[zone] = new(spell, true, placement);
        var receipt = new CommittedActivation(player.Id, profile.EffectId, spell, zone, null, null);
        var link = new ChainLink(1, receipt, placement, ChainOperation.EffectSummon);
        var chain = new PendingChain(placement, [link], Other(player.Id));
        var next = State.With(player: player.With(hand: player.Hand.Where(c => c != spell), spellTraps: slots))
            .WithCommittedActivation(receipt).WithChain(chain);
        return Accept(next, new SpellActivated(player.Id, profile.EffectId, spell, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(chain.Responder, chain.DecisionId));
    }

    private DuelActionResult AnswerEffectSummon(PendingChain chain, ChooseEffectSummon command)
    {
        var pending = chain.SummonDecision!;
        if (command.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (command.Player != pending.Player) return Reject(DuelError.NotActivePlayer);
        if (command.MonsterZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        if (command.Position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense)) return Reject(DuelError.InvalidBattlePosition);
        var target = pending.Targets.SingleOrDefault(c => c.InstanceId == command.TargetInstanceId);
        var ids = command.MaterialInstanceIds;
        if (target is null || ids.Count is < 1 or > 5 || ids.Distinct().Count() != ids.Count ||
            ids.Any(id => !pending.Materials.Any(m => m.Card.InstanceId == id))) return Reject(DuelError.InvalidSummonMaterials);
        var profile = summonEffects[chain.Links[chain.NextLink - 1].Activation.Spell.GameCardId];
        var available = EffectSummonMaterials(State, command.Player, profile, target).ToArray();
        var materials = pending.Materials.Where(m => ids.Contains(m.Card.InstanceId)).ToArray();
        if (materials.Any(m => !available.Contains(m) || m.Field is { } field && !field.IsStillPresent(State)))
            return Reject(DuelError.InvalidSummonMaterials);
        if (profile.Kind == EffectSummonKind.Fusion)
        {
            var recipe = profile.FusionRecipes.Single(r => r.TargetCardId == target.GameCardId);
            if (!materials.Select(m => m.Card.GameCardId).Order().SequenceEqual(recipe.MaterialCardIds.Order()))
                return Reject(DuelError.InvalidSummonMaterials);
        }
        else if (materials.Sum(m => (long)catalog[m.Card.GameCardId].Statistics.Level) !=
            profile.RitualRecipes.Single(r => r.TargetCardId == target.GameCardId).RequiredLevel)
            return Reject(DuelError.InvalidSummonMaterials);
        var next = MoveEffectSummonMaterials(State, command.Player, materials, profile, []);
        var error = CheckSpecialSummon(next, command.Player, target, command.MonsterZone, command.Position,
            EffectSummonSource(profile), profile.ProperProcedureId);
        if (error != DuelError.None) return Reject(error);
        return ResolveChainLink(chain, command);
    }

    // Shared movement operation: preflight every selection before calling; owner destinations differ from controllers.
    private DuelState MoveEffectSummonMaterials(DuelState state, PlayerId playerId,
        IReadOnlyList<EffectSummonMaterial> materials, SummonEffectProfile profile, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId); var zones = player.MonsterZones.ToArray();
        foreach (var material in materials)
            if (material.Field is { } field) zones[field.Zone] = null;
        var next = state.With(player: player.With(monsters: zones, hand: player.Hand.Where(c => !materials.Any(m => m.Card == c))));
        foreach (var material in materials)
        {
            DuelEvent cause = profile.Kind == EffectSummonKind.Fusion
                ? new FusionMaterialSentToGraveyard(material, profile.EffectId) { TimingGroup = 0 }
                : new RitualMaterialTributed(material, profile.EffectId) { TimingGroup = 0 };
            if (material.Field is not null) next = SendFieldCardToGraveyard(next, material.Card, cause, events);
            else
            {
                var owner = next.GetPlayer(material.Card.Owner);
                next = next.With(player: owner.With(graveyard: owner.Graveyard.Append(material.Card)));
                events.Add(cause);
            }
        }
        return next;
    }
}
