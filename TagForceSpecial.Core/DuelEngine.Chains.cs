namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private DuelActionResult ExecuteChain(DuelCommand command)
    {
        var chain = State.PendingChain!;
        var id = command switch
        {
            PassChainResponse c => c.DecisionId,
            ActivateChainResponse c => c.DecisionId,
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
        if (command is ActivateChainResponse response) return AddTokenResponse(chain, response.SpellTrapZone);
        var passes = chain.ConsecutivePasses + 1;
        var next = new PendingChain(checked(State.Revision + 1), chain.Links, Other(chain.Responder),
            passes, passes == 2 ? chain.Links.Count : 0);
        return passes == 2
            ? Accept(State.WithChain(next), new ChainResponsePassed(command.Player), new ChainResolutionStarted(chain.Links.Count))
            : Accept(State.WithChain(next), new ChainResponsePassed(command.Player),
                new ChainResponseRequested(next.Responder, next.DecisionId));
    }

    private DuelActionResult AddTokenResponse(PendingChain chain, int zone)
    {
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(chain.Responder);
        var source = player.SpellTrapFields[zone];
        // Only the retained two-link experiment is exposed. Other response cards,
        // hand activations, negation, and longer chains fail closed.
        if (player.Id == State.ActivePlayer || chain.Links.Count != 1 ||
            chain.Links[0].Operation != ChainOperation.DestroyBoundSpellTrap ||
            source is null || source.IsFaceUp || source.SetOnTurn >= State.TurnNumber ||
            !chainEffects.TryGetValue(source.Card.GameCardId, out var effect) ||
            effect.Operation != ChainOperation.SummonFourTokens ||
            chain.Links[0].Activation.Target is not { } target || target.Player != player.Id ||
            target.Zone != zone || !target.IsStillPresent(source) ||
            player.MonsterZones.Count(m => m is null) < 4 ||
            player.MonsterZones.Any(m => m?.SummonedOnTurn == State.TurnNumber))
            return Reject(DuelError.UnsupportedChainResponse);
        var slots = player.SpellTrapFields.ToArray();
        slots[zone] = source with { IsFaceUp = true }; // same uninterrupted placement
        var receipt = new CommittedActivation(player.Id, effect.EffectId, source.Card, zone, null, null);
        var link = new ChainLink(2, receipt, source.PlacementId, effect.Operation);
        var next = new PendingChain(checked(State.Revision + 1), chain.Links.Append(link), Other(player.Id));
        return Accept(State.With(player: player.With(spellTraps: slots)).WithChain(next),
            new SpellActivated(player.Id, effect.EffectId, source.Card, zone), new ChainLinkAdded(link),
            new ChainResponseRequested(next.Responder, next.DecisionId));
    }

    private DuelActionResult ResolveChainLink(PendingChain chain)
    {
        var link = chain.Links[chain.NextLink - 1];
        var activation = link.Activation;
        var next = State;
        List<DuelEvent> events = [];
        switch (link.Operation)
        {
            case ChainOperation.DestroyOpposingFaceUpMonsters:
            {
                var opponent = next.GetPlayer(Other(activation.Player));
                var zones = opponent.MonsterZones.ToArray();
                var graveyard = opponent.Graveyard.ToList();
                for (int zone = 0; zone < zones.Length; zone++)
                    if (zones[zone] is { Position: not BattlePosition.FaceDownDefense } monster)
                    {
                        zones[zone] = null;
                        graveyard.Add(monster.Card);
                        events.Add(new MonsterDestroyed(opponent.Id, zone, monster.Card));
                    }
                next = next.With(player: opponent.With(monsters: zones, graveyard: graveyard));
                break;
            }
            case ChainOperation.DestroyBoundSpellTrap:
            {
                var target = activation.Target!;
                var player = next.GetPlayer(target.Player);
                // No native changed-target outcome is established. Preserve the
                // entire pre-command state and stop instead of inventing a fizzle.
                if (!target.IsStillPresent(player.SpellTrapFields[target.Zone]))
                    return Reject(DuelError.UnsupportedChainResolution);
                var slots = player.SpellTrapFields.ToArray();
                slots[target.Zone] = null;
                next = next.With(player: player.With(spellTraps: slots, graveyard: player.Graveyard.Append(target.Card)));
                events.Add(new SpellTrapDestroyed(player.Id, target.Zone, target.Card));
                break;
            }
            case ChainOperation.SummonFourTokens:
            {
                var player = next.GetPlayer(activation.Player);
                var zones = player.MonsterZones.ToArray();
                var empty = Enumerable.Range(0, 5).Where(z => zones[z] is null).Take(4).ToArray();
                if (empty.Length != 4) return Reject(DuelError.UnsupportedChainResolution);
                var profile = chainEffects[activation.Spell.GameCardId];
                int instance = next.Players.SelectMany(p => p.Deck.Concat(p.Hand).Concat(p.Graveyard)
                    .Concat(p.Banished).Concat(p.MonsterZones.OfType<FieldMonster>().Select(m => m.Card))
                    .Concat(p.SpellTrapZones.OfType<CardInstance>())).Max(c => c.InstanceId);
                for (int i = 0; i < 4; i++)
                {
                    var card = new CardInstance(checked(++instance), profile.TokenCardIds[i], player.Id);
                    var token = new FieldMonster(card, BattlePosition.FaceUpDefense, SummonedOnTurn: State.TurnNumber);
                    zones[empty[i]] = token;
                    events.Add(new TokenSpecialSummoned(player.Id, empty[i], token));
                }
                next = next.With(player: player.With(monsters: zones));
                break;
            }
            default: return Reject(DuelError.UnsupportedChainResolution);
        }
        events.Add(new ChainLinkResolved(link.Number, activation.EffectId));
        if (chain.NextLink > 1)
            return Accept(next.WithChain(new(checked(State.Revision + 1), chain.Links, chain.Responder,
                chain.ConsecutivePasses, chain.NextLink - 1)), events.ToArray());

        // Normal/Quick-Play sources remain on the field while links resolve.
        // A source already destroyed as a target must not be sent a second time.
        foreach (var entry in chain.Links.Reverse())
        {
            var receipt = entry.Activation;
            var player = next.GetPlayer(receipt.Player);
            var field = player.SpellTrapFields[receipt.SpellTrapZone];
            if (field is null || field.Card != receipt.Spell || field.PlacementId != entry.SourcePlacementId) continue;
            var slots = player.SpellTrapFields.ToArray();
            slots[receipt.SpellTrapZone] = null;
            next = next.With(player: player.With(spellTraps: slots, graveyard: player.Graveyard.Append(receipt.Spell)));
            events.Add(new SpellSentToGraveyard(player.Id, receipt.Spell, receipt.SpellTrapZone));
        }
        events.Add(new ChainCompleted());
        return Accept(next.WithChain(null).WithDecision(null, State.DecisionResult, researchSliceComplete: true), events.ToArray());
    }
}
