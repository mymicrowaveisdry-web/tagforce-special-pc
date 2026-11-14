namespace TagForceSpecial.Core;

/// <summary>Enumerates immutable field and zone candidates in controller and slot order, without activation or effective-identity policy.</summary>
internal static class FieldCardQueries
{
    internal static IEnumerable<FieldCardTarget> OnField(DuelState state, PlayerId controller, FieldCardKind kind, TargetSide side,
        FieldCardFilter filter, int? excludeInstanceId, IReadOnlyDictionary<uint, DuelCardRules> rules)
    {
        PlayerId[] players = side switch
        {
            TargetSide.Own => [controller],
            TargetSide.Opponent => [controller == PlayerId.One ? PlayerId.Two : PlayerId.One],
            _ => [controller, controller == PlayerId.One ? PlayerId.Two : PlayerId.One]
        };
        foreach (var id in players)
        {
            var player = state.GetPlayer(id);
            if (kind is not (FieldCardKind.SpellTrap or FieldCardKind.PendulumScale or FieldCardKind.Spell))
                for (int zone = 0; zone < 5; zone++)
                    if (player.MonsterZones[zone] is { } monster && monster.Card.InstanceId != excludeInstanceId && filter switch
                        {
                            FieldCardFilter.Any => true,
                            FieldCardFilter.FaceUp => monster.Position != BattlePosition.FaceDownDefense,
                            FieldCardFilter.AttackPosition or FieldCardFilter.FaceUpAttack => monster.Position == BattlePosition.FaceUpAttack,
                            FieldCardFilter.DefensePosition => monster.Position != BattlePosition.FaceUpAttack,
                            FieldCardFilter.FaceDown => monster.Position == BattlePosition.FaceDownDefense,
                            _ => false
                        })
                        yield return new(id, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId);
            if (kind == FieldCardKind.Monster || filter is not (FieldCardFilter.Any or FieldCardFilter.FaceUp or FieldCardFilter.FaceDown)) continue;
            // Set Spells and Traps are face-down; Pendulum Scales are always face-up.
            bool Shown(bool faceUp) => filter == FieldCardFilter.Any || faceUp == (filter == FieldCardFilter.FaceUp);
            // A Pendulum Scale target is only a card in a Pendulum Zone.
            if (kind != FieldCardKind.PendulumScale)
                for (int zone = 0; zone < 5; zone++)
                if (player.SpellTrapFields[zone] is { } field && field.Card.InstanceId != excludeInstanceId && Shown(field.IsFaceUp) &&
                    (kind != FieldCardKind.Spell || field.EquippedTo is not null ||
                     rules.TryGetValue(field.Card.GameCardId, out var fieldRules) && fieldRules.Category is CardCategory.Spell or CardCategory.Monster))
                    yield return new(id, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId);
            for (int zone = 0; zone < 2; zone++)
                if (player.PendulumZones[zone] is { } scale && scale.Card.InstanceId != excludeInstanceId && filter != FieldCardFilter.FaceDown)
                    yield return new(id, FieldLocation.PendulumZone, zone, scale.Card, scale.PlacementId);
            if (kind != FieldCardKind.PendulumScale && player.FieldSpell is { } fieldSpell && fieldSpell.Card.InstanceId != excludeInstanceId && Shown(fieldSpell.IsFaceUp))
                yield return new(id, FieldLocation.SpellTrapZone, DuelPlayerState.FieldZoneIndex, fieldSpell.Card, fieldSpell.PlacementId);
        }
    }

    internal static IEnumerable<FieldCardTarget> InZone(DuelState state, PlayerId controller, CardZone zone, TargetSide side)
    {
        PlayerId[] players = side switch
        {
            TargetSide.Own => [controller],
            TargetSide.Opponent => [controller == PlayerId.One ? PlayerId.Two : PlayerId.One],
            _ => [controller, controller == PlayerId.One ? PlayerId.Two : PlayerId.One]
        };
        foreach (var id in players)
        {
            var player = state.GetPlayer(id);
            foreach (var card in zone == CardZone.Graveyard ? player.Graveyard : player.Banished)
                yield return new(id, zone == CardZone.Graveyard ? FieldLocation.Graveyard : FieldLocation.Banished, -1, card, 0);
        }
    }
}
