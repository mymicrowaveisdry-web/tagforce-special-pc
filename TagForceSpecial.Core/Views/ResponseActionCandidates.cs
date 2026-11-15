using TagForceSpecial.Cards;
using static TagForceSpecial.Core.Views.ActionCandidate;
using static TagForceSpecial.Core.Views.ActionCandidateEnumeration;

namespace TagForceSpecial.Core.Views;

/// <summary>Constructs open-window, summon, chain and battle response candidates in their original order.</summary>
internal static class ResponseActionCandidates
{
    internal static IEnumerable<ActionCandidate> Build(DuelState state, PlayerId player, CardCatalog catalog, ActionEffectCatalog effects)
    {
        var own = state.GetPlayer(player);
        if (state.OpenWindow is { } window)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < effects.EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateChainResponse(player, window.DecisionId, z, i), ActionKind.ChainResponse, $"Activate set zone {z + 1}", zone: z, target: Option(i));
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < effects.EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterChainResponse(player, window.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Activate monster [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            foreach (var (card, i) in effects.ZoneEffects(own))
                yield return B(new ActivateZoneEffectResponse(player, window.DecisionId, card.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[card.GameCardId].Name}", card.InstanceId, target: Option(i));
            foreach (var (z, card, i) in effects.FaceUpEffects(own))
                yield return B(new ActivateFieldEffectResponse(player, window.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[card.GameCardId].Name} effect", card.InstanceId, z, Option(i));
            yield return B(new PassChainResponse(player, window.DecisionId), ActionKind.ChainPass, "Pass");
            yield break;
        }
        if (state.PendingChain is null && state.PendingSummon is { } summonAttempt)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < effects.EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateSummonNegation(player, summonAttempt.DecisionId, z, i), ActionKind.ChainResponse, $"Negate with zone {z + 1}", zone: z, target: Option(i));
            yield return B(new PassSummonResponse(player, summonAttempt.DecisionId), ActionKind.ChainPass, "Pass");
            yield break;
        }
        if (state.PendingChain is { } chain)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < effects.EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateChainResponse(player, chain.DecisionId, z, i), ActionKind.ChainResponse, $"Respond zone {z + 1}", zone: z, target: Option(i));
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < effects.EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterChainResponse(player, chain.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Respond with monster [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            foreach (var card in own.Hand)
                for (int z = 0; z < 5; z++)
                    for (int i = 0; i < effects.EffectsOf(card); i++)
                        yield return B(new ActivateHandChainResponse(player, chain.DecisionId, card.InstanceId, z, i), ActionKind.ChainResponse,
                            $"Respond with {catalog[card.GameCardId].Name} [{z + 1}]", card.InstanceId, z, Option(i));
            foreach (var (zoneCard, i) in effects.ZoneEffects(own))
                yield return B(new ActivateZoneEffectResponse(player, chain.DecisionId, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
            foreach (var (z, faceUp, i) in effects.FaceUpEffects(own))
                yield return B(new ActivateFieldEffectResponse(player, chain.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
            yield return B(new PassChainResponse(player, chain.DecisionId), ActionKind.ChainPass, "Pass response");
            yield return B(new ResolveNextChainLink(player, chain.DecisionId), ActionKind.ResolveChain, "Resolve chain link");
            yield break;
        }
        if (state.Battle is { } battle)
        {
            for (int z = 0; z < 5; z++)
            {
                for (int i = 0; i < effects.EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateBattleResponse(player, battle.Window.DecisionId, z, i), ActionKind.BattleResponse, $"Battle response [{z + 1}]", zone: z, target: Option(i));
                for (int i = 0; i < effects.EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterBattleResponse(player, battle.Window.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Battle monster effect [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            }
            foreach (var card in own.Hand)
                for (int z = 0; z < 5; z++)
                    for (int i = 0; i < effects.EffectsOf(card); i++)
                        yield return B(new ActivateHandBattleResponse(player, battle.Window.DecisionId, card.InstanceId, z, i), ActionKind.BattleResponse,
                            $"Activate {catalog[card.GameCardId].Name} [{z + 1}]", card.InstanceId, z, Option(i));
            foreach (var (zoneCard, i) in effects.ZoneEffects(own))
                yield return B(new ActivateZoneEffectBattleResponse(player, battle.Window.DecisionId, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
            foreach (var (z, faceUp, i) in effects.FaceUpEffects(own))
                yield return B(new ActivateFieldEffectBattleResponse(player, battle.Window.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
            yield return B(new PassBattleResponse(player, battle.Window.DecisionId), ActionKind.BattlePass, "Pass battle response");
            yield return B(new AdvanceBattle(player, battle.Window.DecisionId), ActionKind.AdvanceBattle, "Advance battle step");
            yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId, Cancel: true), ActionKind.CancelReplay, "Cancel attack replay");
            yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId), ActionKind.ReplayAttack, "Replay direct attack");
            for (int z = 0; z < 5; z++)
                yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId, z), ActionKind.ReplayAttack, $"Replay target [{z + 1}]", target: z);
            foreach (var attack in Attacks(own, player)) yield return attack;
            yield return B(new ChangePhase(player, DuelPhase.Main2), ActionKind.Phase, "Phase: Main2", phase: DuelPhase.Main2);
            yield return B(new ChangePhase(player, DuelPhase.End), ActionKind.Phase, "Phase: End", phase: DuelPhase.End);
            yield break;
        }
    }
}
