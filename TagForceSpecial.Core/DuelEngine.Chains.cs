namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ExecuteChain(DuelCommand command)
    {
        var chain = State.PendingChain!;
        if (chain.SummonDecision is not null)
            return command is ChooseEffectSummon summon ? AnswerEffectSummon(chain, summon) : Reject(DuelError.DecisionPending);
        if (chain.DefinitionSummon is not null)
            return command is ChooseDefinitionSummon choice ? AnswerDefinitionSummon(chain, choice) : Reject(DuelError.DecisionPending);
        if (chain.DefinitionCardChoice is not null)
            return command is ChooseDefinitionCard card ? AnswerDefinitionCardChoice(chain, card) : Reject(DuelError.DecisionPending);
        if (command is ChooseEffectSummon or ChooseDefinitionSummon or ChooseDefinitionCard) return Reject(DuelError.NoPendingDecision);
        if (State.PendingDecision is not null)
            return command is DecisionCommand answer ? AnswerDecision(answer) : Reject(DuelError.DecisionPending);
        if (command is DecisionCommand) return Reject(DuelError.NoPendingDecision);
        var id = command switch
        {
            PassChainResponse c => c.DecisionId,
            ActivateChainResponse c => c.DecisionId,
            ActivateHandChainResponse c => c.DecisionId,
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
        if (command is ActivateChainResponse response)
        {
            if (response.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
            var source = State.GetPlayer(command.Player).SpellTrapFields[response.SpellTrapZone];
            if (source is not null && battleResponses.TryGetValue(source.Card.GameCardId, out var battleProfile))
                return BeginBattleResponse(command.Player, response.SpellTrapZone, response.SummonedCardInstanceId, response.MonsterZone, battleProfile);
            if (response.SummonedCardInstanceId is not null || response.MonsterZone is not null)
                return Reject(DuelError.InvalidDecisionChoice);
            if (source is not null && definitions.TryGetValue(source.Card.GameCardId, out var definition))
            {
                // Definitions answer open and battle chains (outside the Damage Step), not summon windows.
                if (State.PendingSummon is not null) return Reject(DuelError.UnsupportedSummonResponse);
                return BeginDefinitionResponse(chain, response.SpellTrapZone, source, definition);
            }
            if (source is not null && chainResponses.TryGetValue(source.Card.GameCardId, out var profile))
            {
                if (State.PendingSummon is not null && (rules[source.Card.GameCardId].Category != CardCategory.Trap ||
                    profile.Operation is not (ChainOperation.NegateActivation or ChainOperation.NegateEffect)))
                    return Reject(DuelError.UnsupportedSummonResponse);
                if (State.Battle is { } battle && !profile.BattleWindows.HasFlag(battle.Window.Timing))
                    return Reject(DuelError.UnsupportedBattleResponse);
                return BeginChainResponse(chain, response.SpellTrapZone, source, profile);
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
            events.Add(new ChainLinkSkipped(link.Number, link.ActivationNegated
                ? ChainSkipReason.ActivationNegated : ChainSkipReason.EffectNegated));
        else if (link.Trigger is { } sourceTrigger && IsSourceEffectNegated(next, sourceTrigger))
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceEffectsNegated));
        else switch (link.Operation)
        {
            case ChainOperation.Definition:
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
                            _ => throw new InvalidOperationException("Unknown definition choice.")
                        }]);
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
            case ChainOperation.SummonNegation:
            {
                next = ResolveSummonNegation(next, link, events);
                break;
            }
            case ChainOperation.BattleResponse:
            {
                next = ResolveBattleResponse(next, link, events);
                break;
            }
            case ChainOperation.TriggerEffect:
            {
                if (link.Trigger is not { } trigger) return Reject(DuelError.InvalidEffectState);
                next = ResolveTrigger(next, trigger, events);
                break;
            }
            case ChainOperation.NegateActivation:
            case ChainOperation.NegateEffect:
            {
                if (link.TargetLinkNumber is not int number || number < 1 || number >= link.Number)
                    return Reject(DuelError.InvalidEffectState);
                var negation = link.Operation == ChainOperation.NegateActivation || links[number - 1].ActivationNegated
                    ? ChainNegation.Activation : ChainNegation.Effect;
                next = NegateLink(next, links, link, negation, link.DestroyNegatedSource, events);
                break;
            }
            default: return Reject(DuelError.UnsupportedChainResolution);
        }
        events.Add(new ChainLinkResolved(link.Number, activation.EffectId));
        if (chain.NextLink > 1)
            return Accept(next.WithChain(new(checked(State.Revision + 1), links, chain.Responder,
                chain.ConsecutivePasses, chain.NextLink - 1)), events.ToArray());

        // Normal/Quick-Play sources remain on the field while links resolve.
        // A source already destroyed as a target must not be sent a second time.
        foreach (var entry in links.Reverse())
        {
            if (entry.Trigger is not null) continue; // monster/graveyard effect sources are not Spell activations
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
            var field = player.SpellTrapFields[receipt.SpellTrapZone];
            if (field is null || field.Card != receipt.Spell || field.PlacementId != entry.SourcePlacementId) continue;
            var slots = player.SpellTrapFields.ToArray();
            slots[receipt.SpellTrapZone] = null;
            next = next.With(player: player.With(spellTraps: slots, graveyard: player.Graveyard.Append(receipt.Spell)));
            events.Add(rules[receipt.Spell.GameCardId].Category == CardCategory.Trap
                ? new ChainTrapSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone)
                : new SpellSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone));
        }
        events.Add(new ChainCompleted());
        next = next.WithChain(null).WithDecision(null, State.DecisionResult);
        if (next.PendingSummon is not null) next = CompletePendingSummon(next, events);
        return Accept(next, events.ToArray());
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
        if (destroy && target.Trigger is null)
        {
            var receipt = target.Activation;
            var player = next.GetPlayer(receipt.Player);
            var field = target.IsPendulumScale ? player.PendulumZones[receipt.SpellTrapZone] : player.SpellTrapFields[receipt.SpellTrapZone];
            if (field is not null && field.Card == receipt.Spell && field.PlacementId == target.SourcePlacementId)
            {
                var binding = new SpellTrapTarget(player.Id, receipt.SpellTrapZone, receipt.Spell, field.PlacementId, target.IsPendulumScale);
                next = RemoveFieldSpell(next, binding, new SpellTrapDestroyed(player.Id, receipt.SpellTrapZone, receipt.Spell, target.IsPendulumScale),
                    events, activationNegated: target.IsPendulumScale && negation == ChainNegation.Activation);
            }
        }
        if (destroy && target.Trigger is { } triggered && triggered.Source.IsStillPresent(next))
        {
            var binding = triggered.Source;
            var player = next.GetPlayer(binding.Player);
            var monsters = player.MonsterZones.ToArray();
            monsters[binding.Zone] = null;
            next = next.With(player: player.With(monsters: monsters));
            next = SendFieldCardToGraveyard(next, binding.Card, new MonsterDestroyed(player.Id, binding.Zone, binding.Card), events);
        }
        return next;
    }
}
