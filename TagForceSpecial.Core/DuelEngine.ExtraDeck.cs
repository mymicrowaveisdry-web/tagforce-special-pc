namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ExecuteMaterialProcedure(SpecialSummonWithMaterials command)
    {
        if (command.ExpectedRevision != State.Revision) return Reject(DuelError.StaleSummonSelection);
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (command.ProcedureId is null || !materialProcedures.TryGetValue(command.ProcedureId, out var profile))
            return Reject(DuelError.UnsupportedMaterialProcedure);
        if (command.MonsterZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        if (command.Position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense))
            return Reject(DuelError.InvalidBattlePosition);
        var player = State.GetPlayer(command.Player);
        var target = player.ExtraDeck.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (target is null) return Reject(DuelError.SpecialSummonSourceUnavailable);
        if (!profile.TargetCardIds.Contains(target.GameCardId)) return Reject(DuelError.UnsupportedMaterialProcedure);
        var selected = command.Materials;
        if (selected.Count != profile.MaterialCount ||
            selected.Any(m => m is null || m.Player != player.Id || !m.IsStillPresent(State)) ||
            selected.Select(m => m.Zone).Distinct().Count() != selected.Count)
            return Reject(DuelError.InvalidSummonMaterials);
        foreach (var material in selected)
        {
            var monster = player.MonsterZones[material.Zone]!;
            if (monster.BattleDestroyed || profile.RequireFaceUp && monster.Position == BattlePosition.FaceDownDefense ||
                !profile.MaterialCardIds.Contains(material.Card.GameCardId) ||
                !rules.TryGetValue(material.Card.GameCardId, out var rule) || !rule.AllowsSummonMaterial ||
                State.SummonHistory.Any(h => h.Card == material.Card && h.Method == SummonMethod.Token))
                return Reject(DuelError.SummonMaterialNotAllowed);
        }
        if (player.MonsterZones[command.MonsterZone] is not null && !selected.Any(m => m.Zone == command.MonsterZone))
            return Reject(DuelError.MonsterZoneOccupied);
        var ordered = selected.OrderBy(m => m.Zone).ToArray();
        List<DuelEvent> events = [];
        var next = MoveSummonMaterials(State, player.Id, ordered, profile.Destination, profile.ProcedureId, events);
        var error = CheckSpecialSummon(next, player.Id, target, command.MonsterZone, command.Position,
            DecisionCardSource.ExtraDeck, profile.ProcedureId);
        if (error != DuelError.None) return Reject(error);
        if (summonWindows.Contains(target.GameCardId))
        {
            var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id,
                target, command.MonsterZone, Other(player.Id), IsSpecialSummon: true, Source: DecisionCardSource.ExtraDeck,
                Position: command.Position, ProcedureId: profile.ProcedureId, ProperProcedureId: profile.ProcedureId);
            next = next.With(player: RemoveSpecialSource(next.GetPlayer(player.Id), target, DecisionCardSource.ExtraDeck)).WithSummon(pending);
            events.Add(new SummonAttempted(pending) { TimingGroup = 1 });
            events.Add(new SummonResponseRequested(pending));
            return Accept(next, events.ToArray());
        }
        next = PlaceSpecialSummon(next, player.Id, target, command.MonsterZone, command.Position, DecisionCardSource.ExtraDeck,
            events, profile.ProcedureId, properProcedureId: profile.ProcedureId, timingGroup: 1);
        return Accept(next, events.ToArray());
    }

    // Only call after validating every binding and destination; the returned state is not published here.
    private static DuelState MoveSummonMaterials(DuelState state, PlayerId controller, IReadOnlyList<MonsterBinding> ordered,
        MaterialDestination destination, string procedureId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(controller);
        var zones = player.MonsterZones.ToArray();
        foreach (var material in ordered) zones[material.Zone] = null;
        var next = state.With(player: player.With(monsters: zones));
        foreach (var ownerId in ordered.Select(m => m.Card.Owner).Distinct())
        {
            var owner = next.GetPlayer(ownerId);
            var cards = ordered.Where(m => m.Card.Owner == ownerId).Select(m => m.Card);
            next = next.With(player: destination == MaterialDestination.Graveyard
                ? owner.With(graveyard: owner.Graveyard.Concat(cards)) : owner.With(banished: owner.Banished.Concat(cards)));
        }
        foreach (var material in ordered)
            events.Add(destination == MaterialDestination.Graveyard
                ? new SummonMaterialSentToGraveyard(material, procedureId) { TimingGroup = 0 }
                : new SummonMaterialBanished(material, procedureId) { TimingGroup = 0 });
        return next;
    }
}
