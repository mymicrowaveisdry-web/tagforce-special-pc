namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private bool IsXyzCard(CardInstance card) => materialProcedures.Values.Any(p =>
        p.Xyz is not null && p.TargetCardIds.Contains(card.GameCardId));

    private static DuelState SendXyzMaterials(DuelState next, CardInstance host,
        IEnumerable<CardInstance> materials, bool cost, List<DuelEvent> events, int? timingGroup = null)
    {
        foreach (var card in materials)
        {
            var owner = next.GetPlayer(card.Owner);
            next = next.With(player: owner.With(graveyard: owner.Graveyard.Append(card)));
            events.Add(new XyzMaterialSentToGraveyard(host, card, cost) { TimingGroup = timingGroup });
        }
        return next;
    }

    // Central departure handling also covers battle, Tributes, banishment and use as another material.
    // Attachment cards never inherit the host's destination or transfer to its new placement.
    private static DuelState PrepareXyzMaterials(DuelState before, DuelState next, List<DuelEvent> events)
    {
        foreach (var (binding, monster) in FieldMonsters(before))
            if (monster.XyzMaterials.Count != 0 && !binding.IsStillPresent(next))
            {
                // Cleanup belongs to the host's departure, before any later placement in this transition.
                int departure = events.FindIndex(e => e switch
                {
                    MonsterDestroyed d => d.Card == monster.Card,
                    MonsterTributed t => t.Monster == binding,
                    MonsterBanished b => b.Card == monster.Card,
                    SummonMaterialSentToGraveyard m => m.Material == binding,
                    SummonMaterialBanished m => m.Material == binding,
                    FusionMaterialSentToGraveyard m => m.Material.Field == binding,
                    RitualMaterialTributed m => m.Material.Field == binding,
                    _ => false
                });
                List<DuelEvent> released = [];
                next = SendXyzMaterials(next, monster.Card, monster.XyzMaterials, false, released,
                    departure < 0 ? null : events[departure].TimingGroup);
                events.InsertRange(departure < 0 ? events.Count : departure + 1, released);
            }
        return next;
    }
}
