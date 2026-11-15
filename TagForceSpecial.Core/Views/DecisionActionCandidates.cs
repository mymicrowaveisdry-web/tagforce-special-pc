using TagForceSpecial.Cards;
using static TagForceSpecial.Core.Views.ActionCandidate;
using static TagForceSpecial.Core.Views.ActionCandidateEnumeration;

namespace TagForceSpecial.Core.Views;

/// <summary>Constructs pending decision and trigger candidates without authorizing commands or exposing an observation.</summary>
internal static class DecisionActionCandidates
{
    internal static IEnumerable<ActionCandidate> Build(DuelState state, PlayerId player, CardCatalog catalog,
        Func<DeclarationKind, int, bool> declarationValid)
    {
        var own = state.GetPlayer(player);
        if (state.EventEffects.PendingCommitment is { } commitment)
        {
            foreach (var bound in CommitmentActions(state, commitment, player, catalog, declarationValid)) yield return bound;
            yield break;
        }
        if (state.EventEffects.PendingTriggers is { } batch)
        {
            int ordinal = 0;
            foreach (var order in TriggerOrders(batch.Options, batch.Mandatory, []).Take(MaximumTriggerOrders))
                yield return B(new ChooseTriggerOrder(player, batch.DecisionId, order.Select(t => t.Id).ToArray()),
                    order.Length == 0 ? ActionKind.DeclineTriggers : ActionKind.OrderTriggers,
                    order.Length == 0 ? "Decline" : string.Join(" then ", order.Select(t => catalog[t.Source.Card.GameCardId].Name)), target: ordinal++);
            yield break;
        }
        if (state.PendingDecision is { } decision)
        {
            foreach (var card in decision.CardOptions.OrderBy(c => c.GameCardId).ThenBy(c => c.InstanceId))
                yield return B(new ChooseDecisionCard(player, decision.DecisionId, card.InstanceId), ActionKind.ChooseCard,
                    $"Choose {catalog[card.GameCardId].Name}", card.InstanceId);
            foreach (var position in decision.PositionOptions)
                yield return B(new ChooseDecisionPosition(player, decision.DecisionId, position), ActionKind.ChoosePosition, $"{position}", position: position);
            foreach (int zone in decision.MonsterZoneOptions)
                yield return B(new ChooseDecisionZone(player, decision.DecisionId, zone), ActionKind.ChooseZone, $"Monster zone {zone + 1}", zone: zone);
            foreach (var target in decision.TargetOptions)
                yield return B(new ChooseDecisionTarget(player, decision.DecisionId, target.Player, target.Zone,
                    target.Card.InstanceId, target.IsPendulumZone), ActionKind.ChooseTarget,
                    $"Target P{(int)target.Player + 1} {(target.IsPendulumZone ? "scale" : "zone")} {target.Zone + 1}",
                    zone: target.IsPendulumZone ? 1 : 0, target: target.Zone, targetPlayer: target.Player);
            foreach (var field in decision.FieldTargetOptions ?? [])
            {
                // Graveyard and banished cards are public; field targets never reveal a face-down card.
                bool zone = field.Location is FieldLocation.Graveyard or FieldLocation.Banished;
                yield return B(new ChooseDecisionFieldTarget(player, decision.DecisionId, field.Player, field.Location, field.Zone,
                    field.Card.InstanceId), ActionKind.ChooseTarget, zone
                        ? $"Target P{(int)field.Player + 1} {field.Location} {catalog[field.Card.GameCardId].Name}"
                        : $"Target P{(int)field.Player + 1} {field.Location} {field.Zone + 1}",
                    zone ? field.Card.InstanceId : null, (int)field.Location, field.Zone, targetPlayer: field.Player);
            }
            if (decision.CanFinish) yield return B(new FinishDecision(player, decision.DecisionId), ActionKind.FinishChoice, "Done");
            yield break;
        }
        if (state.PendingChain?.SummonDecision is { } effectSummon)
        {
            int ordinal = 0;
            foreach (var target in effectSummon.Targets)
                foreach (var materials in DuelEngine.Subsets(effectSummon.Materials, 5).Take(1024))
                {
                    int? empty = Enumerable.Range(0, 5).Where(z => own.MonsterZones[z] is null).Select(z => (int?)z).FirstOrDefault();
                    if ((empty ?? materials.Select(m => m.Field?.Zone).FirstOrDefault(z => z is not null)) is not int zone) continue;
                    var ids = materials.Select(m => m.Card.InstanceId).ToArray();
                    foreach (var position in new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense })
                        yield return B(new ChooseEffectSummon(player, effectSummon.DecisionId, target.InstanceId, ids, zone, position), ActionKind.ChooseCard,
                            $"Summon {catalog[target.GameCardId].Name} using {string.Join(", ", materials.Select(m => catalog[m.Card.GameCardId].Name))}", target.InstanceId, zone, ordinal++, position);
                }
            yield break;
        }
        if ((state.PendingChain?.ReplacementChoice?.Replacement ?? state.Battle?.Replacement) is { } replacement)
        {
            var offer = replacement.Current;
            if (offer.Player != player) yield break;
            string source = catalog[offer.Source.GameCardId].Name;
            yield return B(new ChooseDestructionReplacement(player, replacement.DecisionId, false), ActionKind.FinishChoice, $"Do not use {source}");
            if (offer.CardOptions.Count == 0)
                yield return B(new ChooseDestructionReplacement(player, replacement.DecisionId, true), ActionKind.ChooseCard, $"Use {source}", offer.Source.InstanceId);
            foreach (var card in offer.CardOptions)
                yield return B(new ChooseDestructionReplacement(player, replacement.DecisionId, true, card.InstanceId), ActionKind.ChooseCard,
                    $"Use {source}: {catalog[card.GameCardId].Name}", card.InstanceId);
            yield break;
        }
        if (state.PendingChain?.DefinitionSummon is { } summon)
        {
            foreach (var option in summon.Options)
                foreach (int zone in summon.MonsterZoneOptions)
                    foreach (var position in summon.PositionOptions)
                        yield return B(new ChooseDefinitionSummon(player, summon.DecisionId, option.Card.InstanceId, zone, position),
                            ActionKind.ChooseCard, $"Summon {catalog[option.Card.GameCardId].Name} ({option.Source}) [{zone + 1}] {position}",
                            option.Card.InstanceId, zone, position: position);
            if (summon.CanFinish) yield return B(new FinishDefinitionSummon(player, summon.DecisionId), ActionKind.FinishChoice, "Done");
            yield break;
        }
        if (state.PendingChain?.Declaration is { } declaration)
        {
            foreach (var bound in DeclarationActions(state, player, declaration.DecisionId, declaration.Kind, declaration.Options, catalog, declarationValid)) yield return bound;
            yield break;
        }
        if (state.PendingChain?.DefinitionChoice is PendingTypeCall call)
        {
            foreach (var category in call.Options)
                yield return B(new CallCardType(player, call.DecisionId, category), ActionKind.ChooseCard, $"Call {category}", target: (int)category);
            yield break;
        }
        if (state.PendingChain?.DefinitionCardChoice is { } pick)
        {
            if (pick.CanFinish) yield return B(new FinishDefinitionCardChoice(player, pick.DecisionId), ActionKind.ChooseCard, "Finish choosing cards");
            foreach (var card in pick.Options)
                yield return B(new ChooseDefinitionCard(player, pick.DecisionId, card.InstanceId), ActionKind.ChooseCard,
                    $"Choose {catalog[card.GameCardId].Name}", card.InstanceId);
            yield break;
        }
    }

    private static IEnumerable<ActionCandidate> CommitmentActions(DuelState state, PendingTriggerCommitment commitment, PlayerId player, CardCatalog catalog, Func<DeclarationKind, int, bool> declarationValid)
    {
        if (commitment.Kind == TriggerCommitmentKind.DiscardCost)
            foreach (var card in commitment.CardOptions)
                yield return B(new ChooseTriggerCostCard(player, commitment.DecisionId, card.InstanceId), ActionKind.ChooseCard,
                    $"Discard {catalog[card.GameCardId].Name}", card.InstanceId);
        if (commitment is { Kind: TriggerCommitmentKind.Declaration, Declaration: { } kind })
            foreach (var bound in DeclarationActions(state, player, commitment.DecisionId, kind, commitment.DeclarationOptions, catalog, declarationValid)) yield return bound;
        foreach (var option in commitment.ZoneOptions)
            yield return option.Location is FieldLocation.Graveyard or FieldLocation.Banished
                ? B(new ChooseTriggerZoneTarget(player, commitment.DecisionId, option.Player, option.Card.InstanceId), ActionKind.ChooseCard,
                    $"Target {catalog[option.Card.GameCardId].Name}", option.Card.InstanceId)
                // Field targets are bound by zone so a face-down card is not revealed.
                : B(new ChooseTriggerZoneTarget(player, commitment.DecisionId, option.Player, option.Card.InstanceId), ActionKind.ChooseTarget,
                    $"Target P{(int)option.Player + 1} {option.Location} {option.Zone + 1}", null, (int)option.Location, option.Zone, targetPlayer: option.Player);
        // Bound by zone only: a target can be face-down.
        foreach (var target in commitment.TargetOptions)
            yield return B(new ChooseTriggerMonsterTarget(player, commitment.DecisionId, target.Player, target.Zone, target.Card.InstanceId), ActionKind.ChooseTarget,
                $"Target P{(int)target.Player + 1} zone {target.Zone + 1}", zone: 0, target: target.Zone, targetPlayer: target.Player);
        if (commitment.Kind != TriggerCommitmentKind.DetachCost) yield break;
        int ordinal = 0;
        foreach (var cards in Combinations(commitment.CardOptions, commitment.Trigger.Profile.DetachMaterialCount, 0).Take(MaximumTriggerOrders))
            yield return B(new ChooseTriggerXyzMaterials(player, commitment.DecisionId, cards.Select(c => c.InstanceId)), ActionKind.ChooseCard,
                $"Detach {string.Join(", ", cards.Select(c => catalog[c.GameCardId].Name))}", target: ordinal++);
    }

    /// <summary>
    /// Declarable values. Any card name is legal, so names are offered from the cards this player knows of: all their own and
    /// the opponent's public cards.
    /// </summary>
    private static IEnumerable<ActionCandidate> DeclarationActions(DuelState state, PlayerId player, long decisionId, DeclarationKind kind, IReadOnlyList<int> options, CardCatalog catalog, Func<DeclarationKind, int, bool> declarationValid)
    {
        if (kind is not (DeclarationKind.CardName or DeclarationKind.MonsterCardName))
        {
            foreach (int value in options)
                yield return B(new DeclareValue(player, decisionId, value), ActionKind.ChooseCard,
                    kind == DeclarationKind.CardType ? $"Declare {(CardCategory)value}" : $"Declare {kind} {value}", target: value);
            yield break;
        }
        var own = state.GetPlayer(player);
        var opponent = state.GetPlayer(Other(player));
        var known = own.Deck.Concat(own.Hand).Concat(own.Graveyard).Concat(own.Banished).Concat(own.ExtraDeck).Concat(own.FaceUpExtraDeck)
            .Concat(own.MonsterZones.OfType<FieldMonster>().Select(m => m.Card)).Concat(own.SpellTrapFields.OfType<FieldSpellTrap>().Select(f => f.Card))
            .Concat(opponent.Graveyard).Concat(opponent.Banished).Concat(opponent.FaceUpExtraDeck)
            .Concat(opponent.MonsterZones.OfType<FieldMonster>().Where(m => m.Position != BattlePosition.FaceDownDefense).Select(m => m.Card))
            .Concat(opponent.SpellTrapFields.OfType<FieldSpellTrap>().Where(f => f.IsFaceUp).Select(f => f.Card));
        foreach (var id in known.Select(c => c.GameCardId).Distinct().Where(id => declarationValid(kind, (int)id))
                     .GroupBy(id => catalog[id].Name).Select(g => g.Min()).Order())
            yield return B(new DeclareValue(player, decisionId, (int)id), ActionKind.ChooseCard, $"Declare {catalog[id].Name}", target: (int)id);
    }

}
