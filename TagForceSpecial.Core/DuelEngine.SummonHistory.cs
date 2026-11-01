namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelState PrepareSummonHistory(DuelState before, DuelState next, IReadOnlyList<DuelEvent> events)
    {
        var history = next.SummonHistory.ToList();
        long revision = checked(before.Revision + 1);
        string? Retained(CardInstance card, DecisionCardSource? source) =>
            source is DecisionCardSource.Hand or DecisionCardSource.Deck ? null
                : history.LastOrDefault(h => h.Card == card)?.ProperProcedureId;
        void Record(PlayerId player, CardInstance card, int zone, BattlePosition position, SummonMethod method,
            DecisionCardSource? source, SummonStatus status, long? placement, long? attempt = null,
            string? procedure = null, string? proper = null)
        {
            var entry = new SummonRecord(attempt ?? revision, player, card, zone, position, method, source, status,
                next.TurnNumber, status == SummonStatus.Pending ? null : revision, placement, procedure,
                status == SummonStatus.Completed ? proper ?? Retained(card, source) : Retained(card, source));
            history.RemoveAll(h => h.Card == card);
            history.Add(entry);
        }
        foreach (var emitted in events)
            switch (emitted)
            {
                case SummonAttempted e:
                    var a = e.Attempt;
                    Record(a.Player, a.Card, a.MonsterZone, a.Position,
                        a.IsSpecialSummon ? SummonMethod.SpecialProcedure : SummonMethod.Normal,
                        a.Source, SummonStatus.Pending, null, a.AttemptId, a.ProcedureId);
                    break;
                case SummonNegated e:
                    var n = e.Attempt;
                    Record(n.Player, n.Card, n.MonsterZone, n.Position,
                        n.IsSpecialSummon ? SummonMethod.SpecialProcedure : SummonMethod.Normal,
                        n.Source, SummonStatus.Negated, null, n.AttemptId, n.ProcedureId);
                    break;
                case MonsterSpecialSummoned e:
                    Record(e.Player, e.Monster.Card, e.Zone, e.Monster.Position,
                        e.ProcedureId is null ? SummonMethod.SpecialEffect : SummonMethod.SpecialProcedure,
                        e.Source, SummonStatus.Completed, e.Monster.PlacementId, e.AttemptId, e.ProcedureId, e.ProperProcedureId);
                    break;
                case MonsterNormalSummoned e:
                    Record(e.Player, e.Monster.Card, e.Zone, e.Monster.Position, SummonMethod.Normal,
                        DecisionCardSource.Hand, SummonStatus.Completed, e.Monster.PlacementId, e.AttemptId);
                    break;
                case MonsterSet e:
                    Record(e.Player, e.Monster.Card, e.Zone, e.Monster.Position, SummonMethod.Set,
                        DecisionCardSource.Hand, SummonStatus.Completed, e.Monster.PlacementId);
                    break;
                case MonsterFlipSummoned e:
                    Record(e.Player, e.Monster.Card, e.Zone, e.Monster.Position, SummonMethod.Flip,
                        null, SummonStatus.Completed, e.Monster.PlacementId);
                    break;
                case TokenSpecialSummoned e:
                    Record(e.Player, e.Monster.Card, e.Zone, e.Monster.Position, SummonMethod.Token,
                        null, SummonStatus.Completed, e.Monster.PlacementId);
                    break;
            }
        // A return to a private origin ends retained proper-summon eligibility, even before another summon.
        var privateCards = next.Players.SelectMany(p => p.Hand.Concat(p.Deck)).ToHashSet();
        for (int i = 0; i < history.Count; i++)
            if (history[i].ProperProcedureId is not null && privateCards.Contains(history[i].Card))
                history[i] = history[i] with { ProperProcedureId = null };
        return next.WithSummonHistory(history);
    }
}
