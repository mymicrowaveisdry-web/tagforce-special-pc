namespace TagForceSpecial.Core;

/// <summary>Applies counter, equip/revival binding and usage bookkeeping changes to immutable state.</summary>
internal static class LastingEffectTransitions
{
    internal static DuelState UpdateSource(DuelState state, ChainLink link, Func<FieldSpellTrap, FieldSpellTrap> change)
    {
        var player = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        return state.With(player: player.WithSpellTrapAt(zone, change(player.SpellTrapAt(zone)!)));
    }

    internal static DuelState AttachEquip(DuelState state, ChainLink link, FieldCardTarget target) =>
        UpdateSource(state, link, f => f with { EquippedTo = new(target.Player, target.Zone, target.Card, target.PlacementId) });

    /// <summary>Ties a monster just revived in that zone to its resolving source.</summary>
    internal static DuelState AttachRevival(DuelState state, ChainLink link, int monsterZone, RevivalLink kind)
    {
        var monster = state.GetPlayer(link.Activation.Player).MonsterZones[monsterZone]!;
        MonsterBinding binding = new(link.Activation.Player, monsterZone, monster.Card, monster.PlacementId);
        return UpdateSource(state, link, f => kind == RevivalLink.Equip
            ? f with { EquippedTo = binding, Revival = kind } : f with { LinkedMonster = binding, Revival = kind });
    }

    internal static DuelState PlaceCounters(DuelState state, FieldCardTarget target, PlaceCounterStep step, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (target.Location == FieldLocation.SpellTrapZone)
        {
            if (player.SpellTrapAt(target.Zone) is not { IsFaceUp: true } field) return state;
            int held = step.Protective ? field.ProtectionCounters : field.Counters;
            if (held >= step.Max) return state;
            int sum = Math.Min(step.Max, held + step.Count);
            events.Add(new SpellTrapCountersPlaced(player.Id, target.Zone, field.Card, sum - held, sum));
            return state.With(player: player.WithSpellTrapAt(target.Zone, step.Protective ? field with { ProtectionCounters = sum } : field with { Counters = sum }));
        }
        if (player.MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense } monster) return state;
        int current = step.Protective ? monster.ProtectionCounters : monster.Counters;
        if (current >= step.Max) return state;
        int total = Math.Min(step.Max, current + step.Count);
        var monsters = player.MonsterZones.ToArray();
        monsters[target.Zone] = step.Protective ? monster with { ProtectionCounters = total } : monster with { Counters = total };
        events.Add(new CountersPlaced(new(target.Player, target.Zone, target.Card, target.PlacementId), total - current, total));
        return state.With(player: player.With(monsters: monsters));
    }

    internal static DuelState AddSpellCounters(DuelState state, CardInstance resolved, List<DuelEvent> events,
        IReadOnlyDictionary<uint, EffectDefinition> definitions, IReadOnlyDictionary<uint, DuelCardRules> rules, bool spellsNegated)
    {
        foreach (var (player, zone, field) in LastingEffectQueries.ActiveCards(state).ToArray())
            if (field.Card != resolved && !(spellsNegated && LastingEffectQueries.IsSpellCard(field, rules)) && definitions.TryGetValue(field.Card.GameCardId, out var definition) &&
                definition.SpellCounterMax > field.Counters)
                state = PlaceCounters(state, new(player, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId), new(1, definition.SpellCounterMax), events);
        foreach (var (binding, monster) in FieldCardQueries.Monsters(state).ToArray())
            if (monster.Position != BattlePosition.FaceDownDefense && !monster.BattleDestroyed && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                definitions.TryGetValue(monster.Card.GameCardId, out var definition) && definition.SpellCounterMax > monster.Counters)
                state = PlaceCounters(state, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, binding.Card, binding.PlacementId), new(1, definition.SpellCounterMax), events);
        return state;
    }

    internal static DuelState RecordUse(DuelState state, CardInstance card, EffectDefinition effect, int turnNumber, long? placement) => state.WithBookkeeping(state.Bookkeeping with
    {
        ActivatedUses = Array.AsReadOnly(state.Bookkeeping.ActivatedUses.Where(u => u.TurnNumber == turnNumber).Append(new(card.InstanceId, turnNumber, effect.OncePerTurnKey, placement)).ToArray())
    });
}
