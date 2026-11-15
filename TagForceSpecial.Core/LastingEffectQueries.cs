namespace TagForceSpecial.Core;

/// <summary>Evaluates active lasting sources, modifiers and linked-destruction candidates without applying transitions.</summary>
internal static class LastingEffectQueries
{
    internal static IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> ActiveCards(DuelState state) =>
        state.Players.SelectMany(p => Enumerable.Range(0, DuelPlayerState.FieldZoneIndex + 1).Select(zone => (Player: p.Id, Zone: zone, Field: p.SpellTrapAt(zone))))
            .Where(e => e.Field is { IsFaceUp: true, EffectActive: true }).Select(e => (e.Player, e.Zone, e.Field!));

    internal static bool IsSpellsNegated(DuelState state, IReadOnlyDictionary<uint, EffectDefinition> definitions, bool hasSpellNegators) => hasSpellNegators && ActiveCards(state)
        .Any(e => definitions.TryGetValue(e.Field.Card.GameCardId, out var d) && d.Negation == LastingNegation.Spells);

    internal static bool IsSpellCard(FieldSpellTrap field, IReadOnlyDictionary<uint, DuelCardRules> rules) => field.IsUnion || rules[field.Card.GameCardId].Category == CardCategory.Spell;

    internal static void AddLastingModifiers(DuelState state, (MonsterBinding Binding, FieldMonster Monster)[] monsters, IReadOnlyList<MonsterBinding> negated, List<StatModifier> result,
        IReadOnlyDictionary<uint, EffectDefinition> definitions, IReadOnlyDictionary<uint, DuelCardRules> rules,
        bool spellsNegated, Func<CardInstance, CardFilter, bool> matches)
    {
        foreach (var (player, zone, field) in ActiveCards(state))
        {
            if (spellsNegated && IsSpellCard(field, rules) || !definitions.TryGetValue(field.Card.GameCardId, out var definition)) continue;
            foreach (var lasting in definition.LastingModifiers)
            {
                int multiplier = CardCountQueries.Lasting(state, player, lasting.PerCount, lasting.CountFilter is null ? null : card => matches(card, lasting.CountFilter));
                foreach (var (target, monster) in monsters)
                    if (lasting.EquippedOnly ? target == field.EquippedTo
                        : (lasting.Side == TargetSide.Either || lasting.Side == TargetSide.Own == (target.Player == player)) &&
                          (lasting.Filter is null || matches(monster.Card, lasting.Filter)))
                        result.Add(new(definition.EffectId, null, target, checked(lasting.Attack * multiplier), checked(lasting.Defense * multiplier),
                            SpellTrapSource: new(player, zone, field.Card, field.PlacementId)));
            }
        }
        foreach (var (target, monster) in monsters)
            if (monster.Counters > 0 && definitions.TryGetValue(monster.Card.GameCardId, out var definition) && definition.AttackPerCounter != 0 && !negated.Contains(target))
                result.Add(new(definition.EffectId, target, target, checked(definition.AttackPerCounter * monster.Counters), 0));
    }

    internal static bool TargetPasses(DuelState state, FieldTarget spec, FieldCardTarget target, Func<CardInstance, CardFilter, bool> matches) =>
        (spec.CardFilter is null || matches(target.Card, spec.CardFilter)) &&
        (spec.MinXyzMaterials == 0 && spec.MaxXyzMaterials == int.MaxValue || target.Location == FieldLocation.MonsterZone &&
            state.GetPlayer(target.Player).MonsterZones[target.Zone] is { } holder &&
            holder.XyzMaterials.Count >= spec.MinXyzMaterials && holder.XyzMaterials.Count <= spec.MaxXyzMaterials) &&
        (!spec.NoUnionEquipped || target.Location != FieldLocation.MonsterZone || !HasUnionEquipped(state, new(target.Player, target.Zone, target.Card, target.PlacementId)));

    internal static bool HasUnionEquipped(DuelState state, MonsterBinding monster) =>
        state.GetPlayer(monster.Player).SpellTrapFields.Any(f => f is { IsUnion: true } && f.EquippedTo == monster);

    internal static IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> LinkedCards(DuelState state) =>
        state.Players.SelectMany(p => p.SpellTrapFields.Select((field, zone) => (Player: p.Id, Zone: zone, Field: field)))
            .Where(e => e.Field is not null && (e.Field.EquippedTo is not null || e.Field.LinkedMonster is not null))
            .Select(e => (e.Player, e.Zone, e.Field!));

    internal static FieldCardTarget? NextLinkedDestruction(IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> linked, DuelState next, List<DuelEvent> events,
        IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        static FieldCardTarget MonsterTarget(MonsterBinding b) => new(b.Player, FieldLocation.MonsterZone, b.Zone, b.Card, b.PlacementId);
        foreach (var (player, zone, field) in linked)
        {
            FieldCardTarget source = new(player, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId);
            if (source.IsStillPresent(next))
            {
                if (field.EquippedTo is { } equipped && (!equipped.IsStillPresent(next) ||
                        next.GetPlayer(equipped.Player).MonsterZones[equipped.Zone]!.Position == BattlePosition.FaceDownDefense))
                    return source;
                if (field.Revival == RevivalLink.Haunted && field.LinkedMonster is { } revived &&
                    events.OfType<MonsterDestroyed>().Any(e => e.Card == revived.Card && e.Zone == revived.Zone))
                    return source;
            }
            else
            {
                if (field.Revival == RevivalLink.Haunted && field.LinkedMonster is { } left && left.IsStillPresent(next)) return MonsterTarget(left);
                if (field.Revival == RevivalLink.Equip && field.EquippedTo is { } held && held.IsStillPresent(next) &&
                    events.OfType<SpellTrapDestroyed>().Any(e => e.Card == field.Card))
                    return MonsterTarget(held);
            }
        }
        if (events.OfType<SpellTrapDestroyed>().Any(e => rules.TryGetValue(e.Card.GameCardId, out var rule) && rule.IsToonWorld))
            foreach (var (binding, monster) in FieldCardQueries.Monsters(next))
                if (rules.TryGetValue(monster.Card.GameCardId, out var toonRule) && toonRule.IsToon) return MonsterTarget(binding);
        return null;
    }

    internal static bool UsedThisTurn(DuelState state, CardInstance card, EffectDefinition effect, long? placement) =>
        state.Bookkeeping.ActivatedUses.Any(u => u.InstanceId == card.InstanceId && u.TurnNumber == state.TurnNumber && u.Key == effect.OncePerTurnKey && u.PlacementId == placement);
}
