namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private bool IsXyzCard(CardInstance card) => materialProcedures.Values.Any(p =>
        p.Xyz is not null && p.TargetCardIds.Contains(card.GameCardId));

    /// <summary>Attaches the resolving Spell or Trap to a monster; nothing happens if the card already left its zone.</summary>
    private static DuelState AttachSourceAsMaterial(DuelState state, ChainLink link, FieldCardTarget host, List<DuelEvent> events)
    {
        var owner = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        if (owner.SpellTrapAt(zone) is not { } source || source.Card != link.Activation.Spell || source.PlacementId != link.SourcePlacementId ||
            host.Location != FieldLocation.MonsterZone || !host.IsStillPresent(state)) return state;
        return AddXyzMaterial(state.With(player: owner.WithSpellTrapAt(zone, null)), host, link.Activation.Spell, link.Activation.EffectId, events);
    }

    /// <summary>Moves a field monster under the activating monster; its own Xyz Materials go to the Graveyard as it leaves.</summary>
    private DuelState AttachMonsterAsMaterial(DuelState state, ChainLink link, FieldCardTarget target, string effectId, List<DuelEvent> events)
    {
        var receipt = link.Activation;
        if (state.GetPlayer(receipt.Player).MonsterZones[receipt.SpellTrapZone] is not { Position: not BattlePosition.FaceDownDefense } host ||
            host.Card != receipt.Spell || host.PlacementId != link.SourcePlacementId || target.Location != FieldLocation.MonsterZone ||
            target.Card == host.Card || IsTokenId(target.Card.GameCardId)) return state;
        return AddXyzMaterial(RemovePlacement(state, target), new(receipt.Player, FieldLocation.MonsterZone, receipt.SpellTrapZone, host.Card, host.PlacementId),
            target.Card, effectId, events);
    }

    private static DuelState AddXyzMaterial(DuelState state, FieldCardTarget host, CardInstance card, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(host.Player);
        var zones = player.MonsterZones.ToArray();
        var monster = zones[host.Zone]!;
        zones[host.Zone] = monster with { XyzMaterials = Array.AsReadOnly(monster.XyzMaterials.Append(card).ToArray()) };
        events.Add(new CardAttachedAsMaterial(host.Player, monster.Card, card, effectId));
        return state.With(player: player.With(monsters: zones));
    }

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
                    SummonMaterialSentToFaceUpExtraDeck m => m.Material == binding,
                    SummonMaterialBanished m => m.Material == binding,
                    FusionMaterialSentToGraveyard m => m.Material.Field == binding,
                    FusionMaterialSentToFaceUpExtraDeck m => m.Material.Field == binding,
                    RitualMaterialTributed m => m.Material.Field == binding,
                    CardAttachedAsMaterial a => a.Card == monster.Card,
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
