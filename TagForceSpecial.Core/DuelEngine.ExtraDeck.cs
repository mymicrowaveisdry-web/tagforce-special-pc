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
        if ((profile.Synchro is null ? selected.Count != profile.MaterialCount :
                selected.Count < profile.Synchro.MinimumNonTuners + 1 || selected.Count > profile.Synchro.MaximumNonTuners + 1) ||
            selected.Any(m => m is null || m.Player != player.Id || !m.IsStillPresent(State)) ||
            selected.Select(m => m.Zone).Distinct().Count() != selected.Count)
            return Reject(DuelError.InvalidSummonMaterials);
        foreach (var material in selected)
        {
            var monster = player.MonsterZones[material.Zone]!;
            if (monster.BattleDestroyed || profile.RequireFaceUp && monster.Position == BattlePosition.FaceDownDefense ||
                !profile.AnyMaterial && !profile.MaterialCardIds.Contains(material.Card.GameCardId) ||
                !rules.TryGetValue(material.Card.GameCardId, out var rule) || !rule.AllowsSummonMaterial ||
                State.SummonHistory.Any(h => h.Card == material.Card && h.Method == SummonMethod.Token))
                return Reject(DuelError.SummonMaterialNotAllowed);
        }
        if (profile.Synchro is { } synchro)
        {
            var targetLevel = catalog[target.GameCardId].Statistics.Level;
            bool IsTuner(uint id) => synchro.ByTunerRule ? rules[id].IsTuner : synchro.TunerCardIds.Contains(id);
            if (IsXyzCard(target) || selected.Any(m => IsXyzCard(m.Card)) || targetLevel == 0 || selected.Count(m => IsTuner(m.Card.GameCardId)) != 1 ||
                selected.Any(m => catalog[m.Card.GameCardId].Statistics.Level == 0) ||
                selected.Sum(m => (long)catalog[m.Card.GameCardId].Statistics.Level) != targetLevel)
                return Reject(DuelError.InvalidSummonMaterials);
        }
        if (profile.Xyz is { } xyz && selected.Any(m => IsXyzCard(m.Card) ||
            catalog[m.Card.GameCardId].Statistics.Level != xyz.Rank))
            return Reject(DuelError.InvalidSummonMaterials);
        if (player.MonsterZones[command.MonsterZone] is not null && !selected.Any(m => m.Zone == command.MonsterZone))
            return Reject(DuelError.MonsterZoneOccupied);
        var ordered = selected.OrderBy(m => m.Zone).ToArray();
        if (profile.Destination == MaterialDestination.Banished &&
            ordered.Any(m => RuleLocked(State, LockedAction.BeBanished, m.Player, m.Card, State.GetPlayer(m.Player).MonsterZones[m.Zone])))
            return Reject(DuelError.LockedByEffect);
        List<DuelEvent> events = [];
        var next = MoveSummonMaterials(State, player.Id, ordered, profile.Destination, profile.ProcedureId, events);
        var attachments = Array.AsReadOnly(profile.Xyz is null ? Array.Empty<CardInstance>() : ordered.Select(m => m.Card).ToArray());
        if (profile.Xyz is not null)
            foreach (var material in ordered) events.Add(new XyzMaterialAttached(target, material, profile.ProcedureId) { TimingGroup = 0 });
        var error = CheckSpecialSummon(next, player.Id, target, command.MonsterZone, command.Position,
            DecisionCardSource.ExtraDeck, profile.ProcedureId);
        if (error != DuelError.None) return Reject(error);
        var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id,
            target, command.MonsterZone, Other(player.Id), IsSpecialSummon: true, Source: DecisionCardSource.ExtraDeck,
            Position: command.Position, ProcedureId: profile.ProcedureId, ProperProcedureId: profile.ProcedureId)
            { XyzMaterials = attachments };
        var attempted = next.With(player: RemoveSpecialSource(next.GetPlayer(player.Id), target, DecisionCardSource.ExtraDeck)).WithSummon(pending);
        if (OpensSummonWindow(attempted, target.GameCardId))
        {
            next = attempted;
            events.Add(new SummonAttempted(pending) { TimingGroup = 1 });
            events.Add(new SummonResponseRequested(pending));
            return Accept(next, events.ToArray());
        }
        next = PlaceSpecialSummon(next, player.Id, target, command.MonsterZone, command.Position, DecisionCardSource.ExtraDeck,
            events, profile.ProcedureId, properProcedureId: profile.ProcedureId, timingGroup: 1, xyzMaterials: attachments);
        return Accept(next, events.ToArray());
    }

    // Only call after validating every binding and destination; the returned state is not published here.
    private DuelState MoveSummonMaterials(DuelState state, PlayerId controller, IReadOnlyList<MonsterBinding> ordered,
        MaterialDestination destination, string procedureId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(controller);
        var zones = player.MonsterZones.ToArray();
        foreach (var material in ordered) zones[material.Zone] = null;
        var next = state.With(player: player.With(monsters: zones));
        if (destination == MaterialDestination.Attached) return next;
        if (destination == MaterialDestination.Graveyard)
        {
            foreach (var material in ordered)
                next = SendFieldCardToGraveyard(next, material.Card,
                    new SummonMaterialSentToGraveyard(material, procedureId) { TimingGroup = 0 }, events);
            return next;
        }
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
