namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private bool IsXyzCard(CardInstance card) => catalog[card.GameCardId].Codes?.GetterKind is 22 or 23 || materialProcedures.Values.Any(p =>
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

    private static FieldCardTarget? BoundTarget(ChainLink link, int index) =>
        link.Activation.FieldTargets is { } bound && bound.Count > index ? bound[index] : null;

    /// <summary>The face-up monster whose effect this link is, if it is still in place.</summary>
    private static FieldCardTarget? ActivatingMonster(DuelState state, ChainLink link) =>
        state.GetPlayer(link.Activation.Player).MonsterZones[link.Activation.SpellTrapZone] is { Position: not BattlePosition.FaceDownDefense } monster &&
        monster.Card == link.Activation.Spell && monster.PlacementId == link.SourcePlacementId
            ? new(link.Activation.Player, FieldLocation.MonsterZone, link.Activation.SpellTrapZone, monster.Card, monster.PlacementId) : null;

    private static DuelState DetachAll(DuelState state, FieldCardTarget bound, List<DuelEvent> events)
    {
        var player = state.GetPlayer(bound.Player);
        var zones = player.MonsterZones.ToArray();
        var monster = zones[bound.Zone]!;
        zones[bound.Zone] = monster with { XyzMaterials = Array.AsReadOnly(Array.Empty<CardInstance>()) };
        return SendXyzMaterials(state.With(player: player.With(monsters: zones)), monster.Card, monster.XyzMaterials, false, events);
    }

    /// <summary>The Proper Summon procedure an Xyz Monster records when Summoned by an effect treated as an Xyz Summon.</summary>
    private string ProperXyzProcedure(uint cardId) =>
        specialSummonCards.TryGetValue(cardId, out var policy) && policy.RequiredProperProcedureId is { } required ? required
        : materialProcedures.Values.First(p => p.Xyz is not null && p.OverlayFrom is null && p.TargetCardIds.Contains(cardId)).ProcedureId;

    /// <summary>Extra Deck Xyz Monsters a Rank-Up could Summon from the bound monster, checked against the zone it frees.</summary>
    private IEnumerable<CardInstance> RankUpOptions(DuelState state, FieldCardTarget bound, RankUpStep step)
    {
        if (bound.Location != FieldLocation.MonsterZone || state.GetPlayer(bound.Player).MonsterZones[bound.Zone] is not { Position: not BattlePosition.FaceDownDefense } host ||
            host.Card != bound.Card || !IsXyzCard(host.Card)) return [];
        var hostData = catalog[host.Card.GameCardId];
        long rank = hostData.Statistics.Level + step.RankChange;
        var freed = RemovePlacement(state, bound);
        return state.GetPlayer(bound.Player).ExtraDeck.Where(c => IsXyzCard(c) && MatchesFilter(c, step.Into) && catalog[c.GameCardId].Statistics.Level == rank &&
            (!step.SameType || catalog[c.GameCardId].Codes?.Type == hostData.Codes?.Type) &&
            CheckSpecialSummon(freed, bound.Player, c, bound.Zone, BattlePosition.FaceUpAttack, DecisionCardSource.ExtraDeck, ProperXyzProcedure(c.GameCardId)) == DuelError.None).ToArray();
    }

    /// <summary>Summons the Rank-Up monster into its material's zone, carrying that monster and its materials over.</summary>
    private DuelState RankUpSummon(DuelState state, FieldCardTarget bound, CardInstance card, BattlePosition position, string effectId, List<DuelEvent> events)
    {
        var host = state.GetPlayer(bound.Player).MonsterZones[bound.Zone]!;
        CardInstance[] materials = [host.Card, .. host.XyzMaterials];
        events.Add(new CardAttachedAsMaterial(bound.Player, card, host.Card, effectId));
        string proper = ProperXyzProcedure(card.GameCardId);
        return PlaceSpecialSummon(RemovePlacement(state, bound), bound.Player, card, bound.Zone, position, DecisionCardSource.ExtraDeck, events,
            proper, properProcedureId: proper, xyzMaterials: materials);
    }

    private static DuelState MoveXyzMaterial(DuelState state, PlayerId from, CardInstance card, FieldCardTarget host, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(from);
        var zones = player.MonsterZones.ToArray();
        int zone = Array.FindIndex(zones, m => m is not null && m.XyzMaterials.Contains(card));
        if (zone < 0) return state;
        zones[zone] = zones[zone]! with { XyzMaterials = Array.AsReadOnly(zones[zone]!.XyzMaterials.Where(c => c != card).ToArray()) };
        return AddXyzMaterial(state.With(player: player.With(monsters: zones)), host, card, effectId, events);
    }

    private IEnumerable<CardInstance> AttachCandidates(DuelState state, PlayerId playerId, AttachFromStep step, CardInstance exclude)
    {
        var player = state.GetPlayer(playerId);
        return (step.Hand ? player.Hand.Where(c => MatchesFilter(c, step.Filter)) : [])
            .Concat(step.Graveyard ? player.Graveyard.Where(c => MatchesFilter(c, step.Filter, inGraveyard: true)) : [])
            .Where(c => c != exclude).ToArray();
    }

    private static DuelState AttachFromZone(DuelState state, PlayerId playerId, CardInstance card, FieldCardTarget host, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        player = player.Hand.Contains(card) ? player.With(hand: player.Hand.Where(c => c != card)) : player.With(graveyard: player.Graveyard.Where(c => c != card));
        return AddXyzMaterial(state.With(player: player), host, card, effectId, events);
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
        // Materials carried over to another monster, or held by a pending Summon, stay attached.
        var carried = FieldMonsters(next).SelectMany(e => e.Monster.XyzMaterials).Concat(next.PendingSummon?.XyzMaterials ?? []).ToHashSet();
        foreach (var (binding, monster) in FieldMonsters(before))
            if (monster.XyzMaterials.Any(c => !carried.Contains(c)) && !binding.IsStillPresent(next))
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
                next = SendXyzMaterials(next, monster.Card, monster.XyzMaterials.Where(c => !carried.Contains(c)), false, released,
                    departure < 0 ? null : events[departure].TimingGroup);
                events.InsertRange(departure < 0 ? events.Count : departure + 1, released);
            }
        return next;
    }
}
