namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ExecuteChain(DuelCommand command)
    {
        var chain = State.PendingChain!;
        if (chain.SummonDecision is not null)
            return command is ChooseEffectSummon summon ? AnswerEffectSummon(chain, summon) : Reject(DuelError.DecisionPending);
        if (chain.DefinitionSummon is not null)
            return command switch
            {
                ChooseDefinitionSummon choice => AnswerDefinitionSummon(chain, choice),
                FinishDefinitionSummon finish => FinishDefinitionSummons(chain, finish),
                _ => Reject(DuelError.DecisionPending)
            };
        if (chain.DefinitionCardChoice is not null)
            return command is ChooseDefinitionCard card ? AnswerDefinitionCardChoice(chain, card) : Reject(DuelError.DecisionPending);
        if (chain.DefinitionChoice is PendingDeclaration declaration)
            return command is DeclareValue declared ? AnswerDeclaration(chain, declaration, declared) : Reject(DuelError.DecisionPending);
        if (chain.DefinitionChoice is PendingTypeCall typeCall)
            return command is CallCardType call ? AnswerTypeCall(chain, typeCall, call) : Reject(DuelError.DecisionPending);
        if (chain.ReplacementChoice is { } replacement)
            return command is ChooseDestructionReplacement answer ? AnswerChainReplacement(chain, replacement, answer) : Reject(DuelError.DecisionPending);
        if (command is ChooseEffectSummon or ChooseDefinitionSummon or FinishDefinitionSummon or ChooseDefinitionCard or ChooseDestructionReplacement or CallCardType or DeclareValue) return Reject(DuelError.NoPendingDecision);
        if (State.PendingDecision is not null)
            return command is DecisionCommand answer ? AnswerDecision(answer) : Reject(DuelError.DecisionPending);
        if (command is DecisionCommand) return Reject(DuelError.NoPendingDecision);
        var id = command switch
        {
            PassChainResponse c => c.DecisionId,
            ActivateChainResponse c => c.DecisionId,
            ActivateHandChainResponse c => c.DecisionId,
            ActivateMonsterChainResponse c => c.DecisionId,
            ActivateZoneEffectResponse c => c.DecisionId,
            ActivateFieldEffectResponse c => c.DecisionId,
            ResolveNextChainLink c => c.DecisionId,
            _ => -1
        };
        if (id == -1) return Reject(DuelError.ChainPending);
        if (id != chain.DecisionId) return Reject(DuelError.StaleChainDecision);
        if (chain.NextLink != 0)
        {
            if (command is not ResolveNextChainLink) return Reject(DuelError.ChainResolving);
            if (command.Player != State.ActivePlayer) return Reject(DuelError.NotActivePlayer);
            return ResolveChainLink(chain);
        }
        if (command is ResolveNextChainLink) return Reject(DuelError.ChainPending);
        if (command.Player != chain.Responder) return Reject(DuelError.NotChainResponder);
        if (command is ActivateHandChainResponse hand) return BeginHandDefinitionResponse(chain, hand);
        if (command is ActivateMonsterChainResponse monsterResponse) return BeginMonsterEffect(monsterResponse.Player, monsterResponse.MonsterZone, chain, detach: monsterResponse.DetachInstanceIds, effectIndex: monsterResponse.EffectIndex);
        if (command is ActivateZoneEffectResponse zoneResponse) return BeginZoneEffect(zoneResponse.Player, zoneResponse.CardInstanceId, zoneResponse.EffectIndex, chain);
        if (command is ActivateFieldEffectResponse fieldResponse) return BeginFieldEffect(fieldResponse.Player, fieldResponse.SpellTrapZone, fieldResponse.EffectIndex, chain);
        if (command is ActivateChainResponse response)
        {
            if (response.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
            var source = State.GetPlayer(command.Player).SpellTrapFields[response.SpellTrapZone];
            if (source is not null && EffectAt(source.Card.GameCardId, response.EffectIndex, out var definition))
            {
                // In a summon chain only Spell Speed 3 can follow the first Counter Trap.
                return BeginDefinitionResponse(chain, response.SpellTrapZone, source, definition);
            }
            if (State.Battle is not null) return Reject(DuelError.UnsupportedBattleResponse);
            if (State.PendingSummon is not null) return Reject(DuelError.UnsupportedSummonResponse);
            return Reject(DuelError.UnsupportedChainResponse);
        }
        var passes = chain.ConsecutivePasses + 1;
        var next = new PendingChain(checked(State.Revision + 1), chain.Links, Other(chain.Responder),
            passes, passes == 2 ? chain.Links.Count : 0);
        return passes == 2
            ? Accept(State.WithChain(next), new ChainResponsePassed(command.Player), new ChainResolutionStarted(chain.Links.Count))
            : Accept(State.WithChain(next), new ChainResponsePassed(command.Player),
                new ChainResponseRequested(next.Responder, next.DecisionId));
    }

    private DuelActionResult ResolveChainLink(PendingChain chain, ChooseEffectSummon? summonChoice = null,
        DuelCommand? definitionChoice = null)
    {
        var link = chain.Links[chain.NextLink - 1];
        var activation = link.Activation;
        var next = State;
        var links = chain.Links.ToArray();
        List<DuelEvent> events = [];
        if (link.EffectNegated)
        {
            events.Add(new ChainLinkSkipped(link.Number, link.ActivationNegated
                ? ChainSkipReason.ActivationNegated : ChainSkipReason.EffectNegated));
            if (!link.ActivationNegated) next = BindNegatedEquip(next, link);
        }
        else if (link.Trigger is { } sourceTrigger && IsSourceEffectNegated(next, sourceTrigger))
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceEffectsNegated));
        else if (link.Operation == ChainOperation.MonsterEffect && next.EventEffects.NegatedMonsters.Contains(
                new MonsterBinding(activation.Player, activation.SpellTrapZone, activation.Spell, link.SourcePlacementId)))
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceEffectsNegated));
        else if (IsLastingLink(link) && !LastingSourceInPlace(next, link))
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceLeftField));
        else if (link.Operation is ChainOperation.Definition or ChainOperation.EffectSummon or ChainOperation.FieldEffect && rules[activation.Spell.GameCardId].Category == CardCategory.Spell && IsSpellsNegated(next))
        {
            // A negated lasting Spell still stays; it starts applying if the negation ends.
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceEffectsNegated));
            next = BindNegatedEquip(next, link);
            if (IsLastingLink(link)) next = ActivateLastingEffect(next, link);
        }
        else switch (link.Operation)
        {
            case ChainOperation.TriggerEffect when link.Trigger is { Profile.Definition: null } trigger:
            {
                next = ResolveTrigger(next, trigger, events);
                break;
            }
            case ChainOperation.UnionUnequip:
                next = ResolveUnequip(next, link, events);
                break;
            case ChainOperation.Definition:
            case ChainOperation.MonsterEffect:
            case ChainOperation.FieldEffect:
            case ChainOperation.ZoneEffect:
            case ChainOperation.TriggerEffect when link.Trigger is not null:
            {
                (next, var pause) = ResolveDefinitionSteps(next, links, link, chain.DefinitionChoice?.StepIndex ?? 0,
                    definitionChoice, events);
                // Defeat is terminal immediately, as in damage calculation. No later link or cleanup executes.
                if (next.Outcome is not null) return Accept(next, events.ToArray());
                if (pause is not null)
                    return Accept(next.WithChain(new(pause.DecisionId, links, chain.Responder, chain.ConsecutivePasses,
                        chain.NextLink, definitionChoice: pause)), [.. events, pause switch
                        {
                            PendingDefinitionSummon summon => new DefinitionSummonRequested(summon),
                            PendingDefinitionCardChoice card => new DefinitionCardChoiceRequested(card),
                            PendingTypeCall call => new TypeCallRequested(call),
                            PendingReplacementChoice replacement => new DestructionReplacementRequested(replacement.Replacement),
                            _ => throw new InvalidOperationException("Unknown definition choice.")
                        }]);
                if (IsLastingLink(link) && LastingSourceInPlace(next, link)) next = ActivateLastingEffect(next, link);
                break;
            }
            case ChainOperation.ActivatePendulumScale: break; // Source already occupies its dedicated zone; never recreate it.
            case ChainOperation.EffectSummon:
            {
                var profile = summonEffects[activation.Spell.GameCardId];
                if (summonChoice is null)
                {
                    var targets = LegalEffectSummonTargets(next, activation.Player, profile).ToArray();
                    if (targets.Length == 0)
                    {
                        events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
                        break;
                    }
                    var options = targets.SelectMany(t => EffectSummonMaterials(next, activation.Player, profile, t))
                        .DistinctBy(m => m.Card.InstanceId);
                    var decision = new PendingEffectSummon(checked(State.Revision + 1), activation.Player, profile, targets, options);
                    return Accept(next.WithChain(new(decision.DecisionId, chain.Links, chain.Responder,
                        chain.ConsecutivePasses, chain.NextLink, decision)), new EffectSummonRequested(decision));
                }
                var target = SpecialSourceCards(next.GetPlayer(activation.Player), EffectSummonSource(profile))
                    .Single(c => c.InstanceId == summonChoice.TargetInstanceId);
                var materials = chain.SummonDecision!.Materials.Where(m => summonChoice.MaterialInstanceIds.Contains(m.Card.InstanceId)).ToArray();
                next = MoveEffectSummonMaterials(next, activation.Player, materials, profile, events);
                next = PlaceSpecialSummon(next, activation.Player, target, summonChoice.MonsterZone, summonChoice.Position,
                    EffectSummonSource(profile), events, properProcedureId: profile.ProperProcedureId, timingGroup: 1);
                break;
            }
            case ChainOperation.TriggerEffect: return Reject(DuelError.InvalidEffectState);
            default: return Reject(DuelError.UnsupportedChainResolution);
        }
        if (link.Operation is ChainOperation.Definition or ChainOperation.EffectSummon && !link.ActivationNegated && rules[activation.Spell.GameCardId].Category == CardCategory.Spell)
            next = AddSpellCounters(next, activation.Spell, events);
        events.Add(new ChainLinkResolved(link.Number, activation.EffectId));
        if (chain.NextLink > 1)
            return Accept(next.WithChain(new(checked(State.Revision + 1), links, chain.Responder,
                chain.ConsecutivePasses, chain.NextLink - 1)), events.ToArray());

        // Normal/Quick-Play sources remain on the field while links resolve.
        // A source already destroyed as a target must not be sent a second time.
        foreach (var entry in links.Reverse())
        {
            // Monster effects, including a Union's unequip, are not Spell activations.
            if (entry.Trigger is not null || entry.Operation is ChainOperation.MonsterEffect or ChainOperation.UnionUnequip or ChainOperation.FieldEffect or ChainOperation.ZoneEffect) continue;
            var receipt = entry.Activation;
            var player = next.GetPlayer(receipt.Player);
            if (entry.IsPendulumScale)
            {
                var scale = player.PendulumZones[receipt.SpellTrapZone];
                if (entry.ActivationNegated && scale is not null && scale.Card == receipt.Spell && scale.PlacementId == entry.SourcePlacementId)
                    next = RemoveFieldSpell(next, new(player.Id, receipt.SpellTrapZone, receipt.Spell, scale.PlacementId, true),
                        new SpellSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone, true), events, activationNegated: true);
                continue;
            }
            var field = player.SpellTrapAt(receipt.SpellTrapZone);
            if (field is null || field.Card != receipt.Spell || field.PlacementId != entry.SourcePlacementId) continue;
            // An Equip Spell that never got attached has nothing to stay on.
            if (IsLastingLink(entry) && !entry.ActivationNegated &&
                !(LinkDefinition(entry).Kind == EffectKind.EquipSpell && field.EquippedTo is null)) continue;
            next = next.With(player: player.WithSpellTrapAt(receipt.SpellTrapZone, null).With(graveyard: player.Graveyard.Append(receipt.Spell)));
            events.Add(rules[receipt.Spell.GameCardId].Category == CardCategory.Trap
                ? new ChainTrapSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone) { ActivationNegated = entry.ActivationNegated }
                : new SpellSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone) { ActivationNegated = entry.ActivationNegated });
        }
        events.Add(new ChainCompleted());
        next = next.WithChain(null).WithDecision(null, State.DecisionResult);
        if (next.PendingSummon is not null) next = CompletePendingSummon(next, events);
        return Accept(next, events.ToArray());
    }

    private bool IsLastingLink(ChainLink link) => link.Trigger is null && link.Operation == ChainOperation.Definition &&
        LinkDefinition(link).IsLasting;

    private static bool LastingSourceInPlace(DuelState state, ChainLink link) =>
        state.GetPlayer(link.Activation.Player).SpellTrapAt(link.Activation.SpellTrapZone) is { IsFaceUp: true } field &&
        field.Card == link.Activation.Spell && field.PlacementId == link.SourcePlacementId;

    /// <summary>Starts a resolved lasting card's continuing effects.</summary>
    private static DuelState ActivateLastingEffect(DuelState state, ChainLink link)
    {
        var player = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        return state.With(player: player.WithSpellTrapAt(zone, player.SpellTrapAt(zone)! with { EffectActive = true }));
    }

    /// <summary>
    /// Negates the link this one answers. Destroying the negated source is explicit per-effect policy; negation
    /// alone never destroys a source or refunds an already committed cost.
    /// </summary>
    private DuelState NegateLink(DuelState next, ChainLink[] links, ChainLink link, ChainNegation negation, bool destroy,
        List<DuelEvent> events)
    {
        int number = link.TargetLinkNumber ?? throw new InvalidOperationException("Negation needs a target link.");
        var target = links[number - 1];
        links[number - 1] = target with { Negation = negation };
        events.Add(new ChainLinkNegated(number, link.Number, negation));
        // A negated activation never happened, so a limit that counts activations is not spent.
        if (negation == ChainNegation.Activation && target.Trigger is null && effectsById.TryGetValue(target.Activation.EffectId, out var negated) &&
            negated.NameLimit is { Counts: LimitCounts.Activations } limit)
            next = ForgetNamedUse(next, target.Activation.Player, limit);
        // A card used from the hand or Graveyard is not on the field to be destroyed.
        if (target.Operation == ChainOperation.ZoneEffect) return next;
        if (destroy && target.Operation == ChainOperation.MonsterEffect)
        {
            var receipt = target.Activation;
            FieldCardTarget monster = new(receipt.Player, FieldLocation.MonsterZone, receipt.SpellTrapZone, receipt.Spell, target.SourcePlacementId);
            return monster.IsStillPresent(next) ? DestroyFieldCard(next, monster, events) : next;
        }
        if (destroy && (target.Trigger is null || target.Trigger.Profile.SourceLocation == TriggerSourceLocation.FaceUpSpellTrap))
        {
            var receipt = target.Activation;
            var player = next.GetPlayer(receipt.Player);
            var field = target.IsPendulumScale ? player.PendulumZones[receipt.SpellTrapZone] : player.SpellTrapAt(receipt.SpellTrapZone);
            if (field is not null && field.Card == receipt.Spell && field.PlacementId == target.SourcePlacementId)
            {
                var binding = new SpellTrapTarget(player.Id, receipt.SpellTrapZone, receipt.Spell, field.PlacementId, target.IsPendulumScale);
                next = RemoveFieldSpell(next, binding, new SpellTrapDestroyed(player.Id, receipt.SpellTrapZone, receipt.Spell, target.IsPendulumScale) { ActivationNegated = negation == ChainNegation.Activation },
                    events, activationNegated: target.IsPendulumScale && negation == ChainNegation.Activation);
            }
        }
        if (destroy && target.Trigger is { Profile.SourceLocation: not TriggerSourceLocation.FaceUpSpellTrap } triggered && triggered.Source.IsStillPresent(next))
        {
            var binding = triggered.Source;
            next = DestroyFieldCard(next, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, binding.Card, binding.PlacementId), events);
        }
        return next;
    }
}
