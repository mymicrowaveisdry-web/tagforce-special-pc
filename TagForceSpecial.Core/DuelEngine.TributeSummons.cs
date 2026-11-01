namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult SummonWithTributes(TributeSummon command)
    {
        if (command.ExpectedRevision != State.Revision) return Reject(DuelError.StaleSummonSelection);
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        int zone = command.MonsterZone;
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(command.Player);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!rules.TryGetValue(card.GameCardId, out var rule) || rule.Category == CardCategory.Unknown)
            return Reject(DuelError.UnknownCardRules);
        if (rule.Category != CardCategory.Monster) return Reject(DuelError.NotMonster);
        if (!rule.AllowsNormalSummon) return Reject(DuelError.NormalSummonNotAllowed);
        if (!rule.AllowsTributeSummon) return Reject(DuelError.TributeSummonNotSupported);
        TributeProcedureProfile? procedure = null;
        if (command.ProcedureId is not null &&
            (!tributeProcedures.TryGetValue(command.ProcedureId, out procedure) || procedure.CardId != card.GameCardId ||
             command.FaceDown && !procedure.AllowsSet) || command.ProcedureId is null && rule.RequiresRegisteredTributeProcedure)
            return Reject(DuelError.UnsupportedTributeProcedure);
        uint level = ComputeHandSummonLevel(State, player.Id, card);
        // Preserve the original high-level command boundary unless a profile explicitly changes it.
        if (level == 0 || procedure is null && level < 5 && catalog[card.GameCardId].Statistics.Level < 5)
            return Reject(DuelError.UnsupportedMonsterLevel);
        if (State.NormalSummonUsed) return Reject(DuelError.NormalSummonLimitReached);
        int count = procedure?.TributeCount ?? (level <= 4 ? 0 : level <= 6 ? 1 : 2);
        var selected = command.Tributes;
        if (selected.Count != count || selected.Any(t => t is null || t.Player != command.Player ||
                !t.IsStillPresent(State)) || selected.Select(t => t.Zone).Distinct().Count() != count)
            return Reject(DuelError.InvalidTributeSelection);
        foreach (var tribute in selected)
            if (player.MonsterZones[tribute.Zone]!.BattleDestroyed ||
                !rules.TryGetValue(tribute.Card.GameCardId, out var donorRule) || !donorRule.AllowsTributeForSummon ||
                procedure is not null && (procedure.AllowedTributeCardIds.Count != 0 && !procedure.AllowedTributeCardIds.Contains(tribute.Card.GameCardId) ||
                    procedure.RequireFaceUpTributes && player.MonsterZones[tribute.Zone]!.Position == BattlePosition.FaceDownDefense))
                return Reject(DuelError.TributeNotAllowed);
        if (player.MonsterZones[zone] is not null && !selected.Any(t => t.Zone == zone))
            return Reject(DuelError.MonsterZoneOccupied);

        var monsters = player.MonsterZones.ToArray();
        var ordered = selected.OrderBy(t => t.Zone).ToArray();
        foreach (var tribute in ordered) monsters[tribute.Zone] = null;
        var next = State.With(player: player.With(monsters: monsters,
            hand: player.Hand.Where(c => c.InstanceId != card.InstanceId)), normalSummonUsed: true);
        List<DuelEvent> events = [];
        foreach (var tribute in ordered)
            next = SendFieldCardToGraveyard(next, tribute.Card, new MonsterTributed(tribute) { TimingGroup = 0 }, events);
        return CommitSummon(next, player.Id, card, zone, command.FaceDown, events);
    }
}
