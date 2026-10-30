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
        uint level = catalog[card.GameCardId].Statistics.Level;
        if (level < 5) return Reject(DuelError.UnsupportedMonsterLevel);
        if (State.NormalSummonUsed) return Reject(DuelError.NormalSummonLimitReached);
        int count = level <= 6 ? 1 : 2;
        var selected = command.Tributes;
        if (selected.Count != count || selected.Any(t => t is null || t.Player != command.Player ||
                !t.IsStillPresent(State)) || selected.Select(t => t.Zone).Distinct().Count() != count)
            return Reject(DuelError.InvalidTributeSelection);
        foreach (var tribute in selected)
            if (player.MonsterZones[tribute.Zone]!.BattleDestroyed ||
                !rules.TryGetValue(tribute.Card.GameCardId, out var donorRule) || !donorRule.AllowsTributeForSummon)
                return Reject(DuelError.TributeNotAllowed);
        if (player.MonsterZones[zone] is not null && !selected.Any(t => t.Zone == zone))
            return Reject(DuelError.MonsterZoneOccupied);

        var monsters = player.MonsterZones.ToArray();
        var ordered = selected.OrderBy(t => t.Zone).ToArray();
        foreach (var tribute in ordered) monsters[tribute.Zone] = null;
        var monster = new FieldMonster(card, command.FaceDown ? BattlePosition.FaceDownDefense : BattlePosition.FaceUpAttack,
            SummonedOnTurn: State.TurnNumber, PlacementId: checked(State.Revision + 1));
        monsters[zone] = monster;
        var next = State.With(player: player.With(monsters: monsters,
            hand: player.Hand.Where(c => c.InstanceId != card.InstanceId)), normalSummonUsed: true);
        foreach (var owner in ordered.Select(t => t.Card.Owner).Distinct())
        {
            var destination = next.GetPlayer(owner);
            next = next.With(player: destination.With(graveyard: destination.Graveyard.Concat(
                ordered.Where(t => t.Card.Owner == owner).Select(t => t.Card))));
        }
        List<DuelEvent> events = ordered.Select(t => (DuelEvent)new MonsterTributed(t) { TimingGroup = 0 }).ToList();
        events.Add(command.FaceDown
            ? new MonsterSet(player.Id, monster, zone) { TimingGroup = 1 }
            : new MonsterNormalSummoned(player.Id, monster, zone) { TimingGroup = 1 });
        return Accept(next, events.ToArray());
    }
}
