namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    /// <summary>Uses the turn's Normal Summon on a face-up Gemini monster so that it gains its effects.</summary>
    private DuelActionResult ExecuteGeminiSummon(int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer);
        if (player.MonsterZones[zone] is not { } monster) return Reject(DuelError.MonsterZoneEmpty);
        if (!rules[monster.Card.GameCardId].IsGemini) return Reject(DuelError.NotGeminiMonster);
        if (monster.Position == BattlePosition.FaceDownDefense) return Reject(DuelError.InvalidBattlePosition);
        if (monster.GeminiActive) return Reject(DuelError.GeminiAlreadyActive);
        if (!NormalSummonAvailable(State)) return Reject(DuelError.NormalSummonLimitReached);
        if (IsSummonLocked(State, player.Id)) return Reject(DuelError.SummonLocked);
        if (RuleLocked(State, LockedAction.NormalSummon, player.Id, monster.Card, monster)) return Reject(DuelError.LockedByEffect);
        var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id, monster.Card, zone, Other(player.Id),
            Position: monster.Position) { IsGeminiSummon = true };
        var attempted = SpendNormalSummon(State.With(player: player)).WithSummon(pending);
        if (OpensSummonWindow(attempted, monster.Card.GameCardId))
            return Accept(attempted, new SummonAttempted(pending) { TimingGroup = 1 }, new SummonResponseRequested(pending));
        List<DuelEvent> events = [];
        return Accept(CompleteGeminiSummon(attempted, pending, events).WithSummon(null), events.ToArray());
    }

    /// <summary>Gives the monster its effects, if it is still the one the attempt named.</summary>
    private static DuelState CompleteGeminiSummon(DuelState next, PendingSummon pending, List<DuelEvent> events)
    {
        var player = next.GetPlayer(pending.Player);
        if (player.MonsterZones[pending.MonsterZone] is not { } monster || monster.Card != pending.Card) return next;
        var changed = monster with { GeminiActive = true };
        var monsters = player.MonsterZones.ToArray();
        monsters[pending.MonsterZone] = changed;
        events.Add(new MonsterNormalSummoned(player.Id, changed, pending.MonsterZone, pending.AttemptId) { TimingGroup = 1 });
        return next.With(player: player.With(monsters: monsters));
    }
}
