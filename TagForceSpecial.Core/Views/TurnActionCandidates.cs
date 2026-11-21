using TagForceSpecial.Cards;
using static TagForceSpecial.Core.Views.ActionCandidate;
using static TagForceSpecial.Core.Views.ActionCandidateEnumeration;

namespace TagForceSpecial.Core.Views;

/// <summary>Constructs ordinary-turn offers with read-only effect metadata and two synchronous summon queries.</summary>
internal static class TurnActionCandidates
{
    internal static IEnumerable<ActionCandidate> Build(DuelState state, PlayerId player, CardCatalog catalog, ActionEffectCatalog effects,
        Func<DuelState, PlayerId, CardInstance, IEnumerable<(string? ProcedureId, int Count, bool AllowsSet)>> tributeOptions,
        Func<DuelState, PlayerId, IEnumerable<(DuelCommand Command, int Card, int Zone, BattlePosition Position, int Tag)>> procedureOptions)
    {
        var own = state.GetPlayer(player);
        yield return B(new ResolveDrawStep(player), ActionKind.Draw, "Draw / begin turn");
        foreach (var card in own.Hand)
        {
            string name = catalog[card.GameCardId].Name;
            for (int z = 0; z < 5; z++)
            {
                yield return B(new NormalSummon(player, card.InstanceId, z), ActionKind.Summon, $"Summon {name} [{z + 1}]", card.InstanceId, z);
                for (int i = 0; i < effects.EffectsOf(card); i++)
                    yield return B(new ActivateSpell(player, card.InstanceId, z, i), ActionKind.ActivateSpell, $"Activate {name} [{z + 1}]", card.InstanceId, z, Option(i));
                yield return B(new SetMonster(player, card.InstanceId, z), ActionKind.SetMonster, $"Set {name} [{z + 1}]", card.InstanceId, z);
                yield return B(new SetSpellTrap(player, card.InstanceId, z), ActionKind.SetSpellTrap, $"Set {name} [{z + 1}]", card.InstanceId, z);
            }
            yield return B(new ActivateSpell(player, card.InstanceId, DuelPlayerState.FieldZoneIndex), ActionKind.ActivateSpell, $"Activate {name} [Field]", card.InstanceId, DuelPlayerState.FieldZoneIndex);
            yield return B(new SetSpellTrap(player, card.InstanceId, DuelPlayerState.FieldZoneIndex), ActionKind.SetSpellTrap, $"Set {name} [Field]", card.InstanceId, DuelPlayerState.FieldZoneIndex);
            yield return B(new DiscardForHandLimit(player, card.InstanceId), ActionKind.Discard, $"Discard {name}", card.InstanceId);
            // Tribute Summons and Sets, with every choice of Tributes the card needs; the engine checks each offer.
            int[] occupied = Enumerable.Range(0, 5).Where(z => own.MonsterZones[z] is not null).ToArray();
            foreach (var (procedure, count, allowsSet) in tributeOptions(state, player, card))
                foreach (var zones in ZoneCombinations(occupied, count, 0))
                {
                    var tributes = zones.Select(z => new MonsterBinding(player, z, own.MonsterZones[z]!.Card, own.MonsterZones[z]!.PlacementId)).ToArray();
                    int mask = zones.Aggregate(0, (m, z) => m | 1 << z);
                    string materials = string.Join(", ", zones.Select(z => z + 1));
                    yield return B(new TributeSummon(player, state.Revision, card.InstanceId, zones[0], tributes, procedureId: procedure), ActionKind.Summon,
                        $"Tribute Summon {name} - use zone {materials}", card.InstanceId, zones[0], 2000 + mask);
                    if (allowsSet)
                        yield return B(new TributeSummon(player, state.Revision, card.InstanceId, zones[0], tributes, faceDown: true, procedureId: procedure), ActionKind.SetMonster,
                            $"Tribute Set {name} - use zone {materials}", card.InstanceId, zones[0], 2000 + mask);
                }
            if (!effects.IsToonCard(card.GameCardId)) continue;
            foreach (var tributes in ZoneSubsets(own))
                for (int z = 0; z < 5; z++)
                    foreach (var position in new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense })
                        yield return B(new ToonSummon(player, card.InstanceId, z, position, tributes, state.Revision), ActionKind.Summon,
                            $"Toon Summon {name} [{z + 1}] {position}", card.InstanceId, z, position: position, target: tributes.Aggregate(0, (mask, t) => mask | 1 << t));
        }
        yield return B(new ActivateSetSpell(player, DuelPlayerState.FieldZoneIndex), ActionKind.ActivateSetSpell, "Activate set [Field]", own.FieldSpell?.Card.InstanceId, DuelPlayerState.FieldZoneIndex);
        // Pendulum scales, one-card Pendulum Summons and Pendulum Effects; the engine filters out what is not legal.
        foreach (var card in own.Hand)
            for (int z = 0; z < 2; z++)
                yield return B(new ActivatePendulumScale(player, card.InstanceId, z), ActionKind.ActivateSpell, $"Scale {catalog[card.GameCardId].Name} [{z + 1}]",
                    card.InstanceId, z, target: 100 + z);
        if (Enumerable.Range(0, 5).FirstOrDefault(z => own.MonsterZones[z] is null, -1) is int free and >= 0)
            foreach (var (card, from) in own.Hand.Select(c => (c, DecisionCardSource.Hand)).Concat(own.FaceUpExtraDeck.Select(c => (c, DecisionCardSource.FaceUpExtraDeck))))
                yield return B(new PendulumSummon(player, state.Revision, [new(card.InstanceId, from, free)]), ActionKind.Summon,
                    $"Pendulum Summon {catalog[card.GameCardId].Name}", card.InstanceId, free, target: 300);
        for (int z = 0; z < 2; z++)
            if (own.PendulumZones[z] is { } scale)
                foreach (int i in effects.PendulumEffectIndexes(scale.Card.GameCardId))
                    yield return B(new ActivatePendulumEffect(player, z, i), ActionKind.CardEffect, $"Use {catalog[scale.Card.GameCardId].Name} Pendulum Effect",
                        scale.Card.InstanceId, z, 200 + i);
        foreach (var (command, instance, zone, position, tag) in procedureOptions(state, player))
            yield return B(command, ActionKind.Summon, $"Special Summon {tag}", instance, zone, tag, position);
        for (int z = 0; z < 5; z++)
        {
            if (own.MonsterZones[z] is { } activated)
            {
                for (int i = 0; i < effects.EffectsOf(activated.Card); i++)
                {
                    int detachCount = effects.DetachCostFor(activated.Card.GameCardId, i);
                    if (detachCount == 0)
                        yield return B(new ActivateMonsterEffect(player, z, EffectIndex: i), ActionKind.MonsterEffect, $"Activate {catalog[activated.Card.GameCardId].Name} [{z + 1}]", activated.Card.InstanceId, z, Option(i));
                    else
                    {
                        int choice = i * 1000;
                        foreach (var cards in Combinations(activated.XyzMaterials, detachCount, 0).Take(MaximumTriggerOrders))
                            yield return B(new ActivateMonsterEffect(player, z, cards.Select(c => c.InstanceId).ToArray(), i), ActionKind.MonsterEffect,
                                $"Activate {catalog[activated.Card.GameCardId].Name} [{z + 1}] detaching {string.Join(", ", cards.Select(c => catalog[c.GameCardId].Name))}", activated.Card.InstanceId, z, choice++);
                    }
                }
            }
            if (own.SpellTrapFields[z] is { } equipped)
            {
                yield return B(new UnequipUnion(player, z), ActionKind.Unequip, $"Unequip {catalog[equipped.Card.GameCardId].Name}", equipped.Card.InstanceId, z);
                yield return B(new ResolveUpkeep(player, z, true), ActionKind.Upkeep, $"Pay upkeep for {catalog[equipped.Card.GameCardId].Name}", equipped.Card.InstanceId, z, target: 1);
                yield return B(new ResolveUpkeep(player, z, false), ActionKind.Upkeep, $"Destroy {catalog[equipped.Card.GameCardId].Name}", equipped.Card.InstanceId, z, target: 0);
            }
        }
        for (int z = 0; z < 5; z++)
            if (own.MonsterZones[z] is { } gemini)
                yield return B(new GeminiSummon(player, z), ActionKind.Summon, $"Gemini Summon {catalog[gemini.Card.GameCardId].Name} [{z + 1}]", gemini.Card.InstanceId, z);
        for (int z = 0; z < 5; z++)
        {
            for (int i = 0; i < effects.EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                yield return B(new ActivateSetSpell(player, z, i), ActionKind.ActivateSetSpell, $"Activate set [{z + 1}]", own.SpellTrapFields[z]?.Card.InstanceId, z, Option(i));
        }
        foreach (var (z, faceUp, i) in effects.FaceUpEffects(own))
            yield return B(new ActivateCardEffect(player, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
        foreach (var (zoneCard, i) in effects.ZoneEffects(own))
            yield return B(new ActivateZoneEffect(player, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
        foreach (var attack in Attacks(own, player)) yield return attack;
        foreach (var phase in Enum.GetValues<DuelPhase>())
            yield return B(new ChangePhase(player, phase), ActionKind.Phase, $"Phase: {phase}", phase: phase);
        yield return B(new EndTurn(player), ActionKind.EndTurn, "End turn");
        for (int z = 0; z < 5; z++)
            foreach (var position in Enum.GetValues<BattlePosition>())
                yield return B(new ChangeMonsterPosition(player, z, position), ActionKind.Position, $"Position {z + 1}: {position}", own.MonsterZones[z]?.Card.InstanceId, z, position: position);
    }
}
