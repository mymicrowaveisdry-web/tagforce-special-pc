using TagForceSpecial.Cards;

namespace TagForceSpecial.Core.Views;

/// <summary>Trusted adapter that owns command handles and gives clients authorized, immutable observations.</summary>
public sealed class DuelActionSource
{
    public const string ContractVersion = "authorized-actions-v1";
    public const int MaximumCandidates = 4096;
    private readonly DuelEngine engine;
    private readonly CardCatalog catalog;
    private readonly Dictionary<PlayerId, (DuelObservation View, Bound[] Commands)> cache = [];
    private sealed record Bound(DuelCommand Command, LegalAction Action);

    public DuelActionSource(DuelEngine engine, CardCatalog catalog) => (this.engine, this.catalog) = (engine, catalog);

    public DuelObservation Observe(PlayerId player)
    {
        if (!Enum.IsDefined(player)) throw new ArgumentOutOfRangeException(nameof(player));
        if (cache.TryGetValue(player, out var previous) && previous.View.Revision == engine.State.Revision)
            return previous.View;
        var state = engine.State;
        PlayerId actor = Actor(state);
        string unsupported = state.ResearchSliceComplete ? "Research boundary reached"
            : "";
        var status = state.Outcome is not null ? ActionAvailability.Finished
            : unsupported.Length > 0 ? ActionAvailability.Unsupported
            : actor == player ? ActionAvailability.Ready : ActionAvailability.Waiting;
        Bound[] commands = [];
        if (status == ActionAvailability.Ready)
        {
            var candidates = Candidates(state, player).Take(MaximumCandidates + 1).ToArray();
            if (candidates.Length > MaximumCandidates)
            {
                status = ActionAvailability.Unsupported;
                unsupported = "Legal-action discovery budget exceeded";
            }
            else
                commands = candidates.Where(c => engine.CheckCommand(c.Command) == DuelError.None)
                    .Select((c, i) => c with { Action = c.Action with { Token = i } }).ToArray();
            if (status == ActionAvailability.Ready && commands.Length == 0)
            {
                status = ActionAvailability.Unsupported;
                unsupported = "No supported legal command; duel is not complete";
            }
        }
        AuthorizedDecision? decision = state.EventEffects.PendingCommitment is { } commitment && commitment.Player == player
            // Target options can be face-down, so only cost cards and public or face-up targets are listed.
            ? new(commitment.DecisionId, "TriggerCommitment", PlayerProjection.Read(commitment.CardOptions.Concat(commitment.ZoneOptions.Where(o => Shown(state, o, player)).Select(o => o.Card)).Select(c => PlayerProjection.Card(c, true, null, catalog))))
            : state.EventEffects.PendingCommitment is null && state.EventEffects.PendingTriggers is { } triggers && triggers.Player == player
            ? new(triggers.DecisionId, "TriggerOrder", PlayerProjection.Read(triggers.Options.Select(t => PlayerProjection.Card(t.Source.Card, true, null, catalog))))
            : state.PendingDecision is { } d && d.Player == player
            ? new(d.DecisionId, d.Kind.ToString(), PlayerProjection.Read(d.CardOptions.OrderBy(c => c.GameCardId).ThenBy(c => c.InstanceId)
                .Select(c => PlayerProjection.Card(c, true, null, catalog))))
            // Deck options are private: only the choosing player is shown them.
            : (state.PendingChain?.ReplacementChoice?.Replacement ?? state.Battle?.Replacement) is { } replacement && replacement.Current.Player == player
                ? new(replacement.DecisionId, "Replacement", PlayerProjection.Read(replacement.Current.CardOptions
                    .Select(c => PlayerProjection.Card(c, true, null, catalog))))
            : state.PendingChain?.DefinitionSummon is { } summon && summon.Player == player
                ? new(summon.DecisionId, "DefinitionSummon", PlayerProjection.Read(summon.Options
                    .Select(o => PlayerProjection.Card(o.Card, true, null, catalog))))
            : state.PendingChain?.DefinitionCardChoice is { } pick && pick.Player == player
                ? new(pick.DecisionId, "DefinitionCard", PlayerProjection.Read(pick.Options
                    .Select(c => PlayerProjection.Card(c, true, null, catalog))))
            : state.PendingChain?.DefinitionChoice is PendingTypeCall call && call.Player == player
                ? new(call.DecisionId, "TypeCall", PlayerProjection.Read(Array.Empty<CardView>()))
            : state.OpenWindow is { } window && actor == player ? new(window.DecisionId, "OpenWindow", PlayerProjection.Read(Array.Empty<CardView>()))
            : state.PendingChain is null && state.PendingSummon is { } attempt && actor == player
                ? new(attempt.DecisionId, "SummonWindow", PlayerProjection.Read(Array.Empty<CardView>()))
            : state.PendingChain is { } chain && actor == player ? new(chain.DecisionId, "Chain", PlayerProjection.Read(Array.Empty<CardView>()))
            : state.Battle is { } battle && actor == player ? new(battle.Window.DecisionId,
                battle.Attack?.ReplayRequired == true && battle.Window.Closed ? "AttackReplay" : "Battle", PlayerProjection.Read(Array.Empty<CardView>())) : null;
        var view = new DuelObservation(state.Revision, player, actor, state.ActivePlayer, state.TurnNumber, state.Phase,
            PlayerProjection.Create(state, player, player, catalog),
            PlayerProjection.Create(state, Other(player), player, catalog, engine.HandRevealed(Other(player))), decision, status, unsupported,
            PlayerProjection.Read(commands.Select(c => c.Action)), state.Outcome);
        cache[player] = (view, commands);
        return view;
    }

    public ActionExecution Execute(PlayerId player, long revision, int token)
    {
        if (!Enum.IsDefined(player)) return new(false, DuelError.InvalidPlayer, engine.State.Revision, null);
        // Check revision before discovering or resolving a token: stale handles cannot alias new choices.
        if (revision != engine.State.Revision) return new(false, DuelError.StaleDecision, engine.State.Revision, null);
        var observation = Observe(player);
        if (observation.Availability != ActionAvailability.Ready || token < 0 || token >= observation.Actions.Count)
            return new(false, engine.State.Outcome is null ? DuelError.InvalidDecisionChoice : DuelError.DuelFinished, engine.State.Revision, null);
        var bound = cache[player].Commands[token];
        var result = engine.Execute(bound.Command);
        return new(result.Succeeded, result.Error, result.State.Revision, bound.Action.Kind);
    }

    private static PlayerId Other(PlayerId player) => player == PlayerId.One ? PlayerId.Two : PlayerId.One;
    private static PlayerId Actor(DuelState state) => state.EventEffects.PendingCommitment?.Player
        ?? state.EventEffects.PendingTriggers?.Player
        ?? state.PendingDecision?.Player
        ?? state.PendingChain?.DefinitionChoice?.Player
        ?? state.Battle?.Replacement?.Current.Player
        ?? state.OpenWindow?.Responder
        ?? (state.PendingChain is null ? state.PendingSummon?.Responder : null)
        ?? (state.PendingChain is { } chain ? chain.NextLink > 0 ? state.ActivePlayer : chain.Responder
            : state.Battle is { } battle && !battle.Window.Closed ? battle.Window.Responder : state.ActivePlayer);

    private static Bound B(DuelCommand command, ActionKind kind, string label, int? card = null, int? zone = null,
        int? target = null, BattlePosition? position = null, DuelPhase? phase = null, PlayerId? targetPlayer = null)
        => new(command, new(0, $"{kind}|{card}|{zone}|{target}|{position}|{phase}|{targetPlayer}", kind, label, card, zone, target, position, phase));

    /// <summary>Graveyard and banished cards are public; a field card is shown to its controller or while face-up.</summary>
    private static bool Shown(DuelState state, FieldCardTarget option, PlayerId viewer) => option.Location switch
    {
        FieldLocation.Graveyard or FieldLocation.Banished => true,
        _ when option.Player == viewer => true,
        FieldLocation.MonsterZone => state.GetPlayer(option.Player).MonsterZones[option.Zone] is { Position: not BattlePosition.FaceDownDefense },
        FieldLocation.PendulumZone => true,
        _ => option.Zone == DuelPlayerState.FieldZoneIndex ? state.GetPlayer(option.Player).FieldSpell is { IsFaceUp: true }
            : state.GetPlayer(option.Player).SpellTrapFields[option.Zone] is { IsFaceUp: true }
    };

    private IEnumerable<Bound> CommitmentActions(PendingTriggerCommitment commitment, PlayerId player)
    {
        if (commitment.Kind == TriggerCommitmentKind.DiscardCost)
            foreach (var card in commitment.CardOptions)
                yield return B(new ChooseTriggerCostCard(player, commitment.DecisionId, card.InstanceId), ActionKind.ChooseCard,
                    $"Discard {catalog[card.GameCardId].Name}", card.InstanceId);
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

    private static IEnumerable<CardInstance[]> Combinations(IReadOnlyList<CardInstance> cards, int count, int start)
    {
        if (count == 0) { yield return []; yield break; }
        for (int i = start; i <= cards.Count - count; i++)
            foreach (var rest in Combinations(cards, count - 1, i + 1)) yield return [cards[i], .. rest];
    }

    private const int MaximumTriggerOrders = 256;

    private IEnumerable<(CardInstance Card, int Index)> ZoneEffects(DuelPlayerState own) =>
        own.Hand.Concat(own.Graveyard).SelectMany(c => engine.ZoneEffectIndexes(c.GameCardId).Select(i => (c, i)));

    private IEnumerable<(int Zone, CardInstance Card, int Index)> FaceUpEffects(DuelPlayerState own) =>
        Enumerable.Range(0, DuelPlayerState.FieldZoneIndex + 1).Select(z => (Zone: z, Field: z == DuelPlayerState.FieldZoneIndex ? own.FieldSpell : own.SpellTrapFields[z]))
            .Where(e => e.Field is { IsFaceUp: true }).SelectMany(e => engine.FaceUpEffectIndexes(e.Field!.Card.GameCardId).Select(i => (e.Zone, e.Field!.Card, i)));

    private int EffectsOf(CardInstance? card) => card is null ? 1 : engine.EffectTotal(card.GameCardId);

    /// <summary>The first effect keeps the action keys it always had.</summary>
    private static int? Option(int effectIndex) => effectIndex == 0 ? null : effectIndex;

    /// <summary>Every legal activation order; optional triggers also allow any subset, including none.</summary>
    private static IEnumerable<TriggerActivation[]> TriggerOrders(IReadOnlyList<TriggerActivation> options, bool mandatory, TriggerActivation[] prefix)
    {
        if (!mandatory || prefix.Length == options.Count) yield return prefix;
        foreach (var next in options.Except(prefix))
            foreach (var order in TriggerOrders(options, mandatory, [.. prefix, next])) yield return order;
    }

    private IEnumerable<Bound> Candidates(DuelState state, PlayerId player)
    {
        var own = state.GetPlayer(player);
        if (state.EventEffects.PendingCommitment is { } commitment)
        {
            foreach (var bound in CommitmentActions(commitment, player)) yield return bound;
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
        if (state.OpenWindow is { } window)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateChainResponse(player, window.DecisionId, z, i), ActionKind.ChainResponse, $"Activate set zone {z + 1}", zone: z, target: Option(i));
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterChainResponse(player, window.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Activate monster [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            foreach (var (card, i) in ZoneEffects(own))
                yield return B(new ActivateZoneEffectResponse(player, window.DecisionId, card.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[card.GameCardId].Name}", card.InstanceId, target: Option(i));
            foreach (var (z, card, i) in FaceUpEffects(own))
                yield return B(new ActivateFieldEffectResponse(player, window.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[card.GameCardId].Name} effect", card.InstanceId, z, Option(i));
            yield return B(new PassChainResponse(player, window.DecisionId), ActionKind.ChainPass, "Pass");
            yield break;
        }
        if (state.PendingChain is null && state.PendingSummon is { } summonAttempt)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateSummonNegation(player, summonAttempt.DecisionId, z, i), ActionKind.ChainResponse, $"Negate with zone {z + 1}", zone: z, target: Option(i));
            yield return B(new PassSummonResponse(player, summonAttempt.DecisionId), ActionKind.ChainPass, "Pass");
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
        if (state.PendingChain?.DefinitionChoice is PendingTypeCall call)
        {
            foreach (var category in call.Options)
                yield return B(new CallCardType(player, call.DecisionId, category), ActionKind.ChooseCard, $"Call {category}", target: (int)category);
            yield break;
        }
        if (state.PendingChain?.DefinitionCardChoice is { } pick)
        {
            foreach (var card in pick.Options)
                yield return B(new ChooseDefinitionCard(player, pick.DecisionId, card.InstanceId), ActionKind.ChooseCard,
                    $"Choose {catalog[card.GameCardId].Name}", card.InstanceId);
            yield break;
        }
        if (state.PendingChain is { } chain)
        {
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateChainResponse(player, chain.DecisionId, z, i), ActionKind.ChainResponse, $"Respond zone {z + 1}", zone: z, target: Option(i));
            for (int z = 0; z < 5; z++)
                for (int i = 0; i < EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterChainResponse(player, chain.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Respond with monster [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            foreach (var card in own.Hand)
                for (int z = 0; z < 5; z++)
                    for (int i = 0; i < EffectsOf(card); i++)
                        yield return B(new ActivateHandChainResponse(player, chain.DecisionId, card.InstanceId, z, i), ActionKind.ChainResponse,
                            $"Respond with {catalog[card.GameCardId].Name} [{z + 1}]", card.InstanceId, z, Option(i));
            foreach (var (zoneCard, i) in ZoneEffects(own))
                yield return B(new ActivateZoneEffectResponse(player, chain.DecisionId, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
            foreach (var (z, faceUp, i) in FaceUpEffects(own))
                yield return B(new ActivateFieldEffectResponse(player, chain.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
            yield return B(new PassChainResponse(player, chain.DecisionId), ActionKind.ChainPass, "Pass response");
            yield return B(new ResolveNextChainLink(player, chain.DecisionId), ActionKind.ResolveChain, "Resolve chain link");
            yield break;
        }
        if (state.Battle is { } battle)
        {
            for (int z = 0; z < 5; z++)
            {
                for (int i = 0; i < EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                    yield return B(new ActivateBattleResponse(player, battle.Window.DecisionId, z, i), ActionKind.BattleResponse, $"Battle response [{z + 1}]", zone: z, target: Option(i));
                for (int i = 0; i < EffectsOf(own.MonsterZones[z]?.Card); i++)
                    yield return B(new ActivateMonsterBattleResponse(player, battle.Window.DecisionId, z, EffectIndex: i), ActionKind.MonsterEffect, $"Battle monster effect [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z, Option(i));
            }
            foreach (var card in own.Hand)
                for (int z = 0; z < 5; z++)
                    for (int i = 0; i < EffectsOf(card); i++)
                        yield return B(new ActivateHandBattleResponse(player, battle.Window.DecisionId, card.InstanceId, z, i), ActionKind.BattleResponse,
                            $"Activate {catalog[card.GameCardId].Name} [{z + 1}]", card.InstanceId, z, Option(i));
            foreach (var (zoneCard, i) in ZoneEffects(own))
                yield return B(new ActivateZoneEffectBattleResponse(player, battle.Window.DecisionId, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
            foreach (var (z, faceUp, i) in FaceUpEffects(own))
                yield return B(new ActivateFieldEffectBattleResponse(player, battle.Window.DecisionId, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
            yield return B(new PassBattleResponse(player, battle.Window.DecisionId), ActionKind.BattlePass, "Pass battle response");
            yield return B(new AdvanceBattle(player, battle.Window.DecisionId), ActionKind.AdvanceBattle, "Advance battle step");
            yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId, Cancel: true), ActionKind.CancelReplay, "Cancel attack replay");
            yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId), ActionKind.ReplayAttack, "Replay direct attack");
            for (int z = 0; z < 5; z++)
                yield return B(new ChooseAttackReplay(player, battle.Window.DecisionId, z), ActionKind.ReplayAttack, $"Replay target [{z + 1}]", target: z);
            foreach (var attack in Attacks(own, player)) yield return attack;
            yield return B(new ChangePhase(player, DuelPhase.Main2), ActionKind.Phase, "Phase: Main2", phase: DuelPhase.Main2);
            yield break;
        }
        yield return B(new ResolveDrawStep(player), ActionKind.Draw, "Draw / begin turn");
        foreach (var card in own.Hand)
        {
            string name = catalog[card.GameCardId].Name;
            for (int z = 0; z < 5; z++)
            {
                yield return B(new NormalSummon(player, card.InstanceId, z), ActionKind.Summon, $"Summon {name} [{z + 1}]", card.InstanceId, z);
                for (int i = 0; i < EffectsOf(card); i++)
                    yield return B(new ActivateSpell(player, card.InstanceId, z, i), ActionKind.ActivateSpell, $"Activate {name} [{z + 1}]", card.InstanceId, z, Option(i));
                yield return B(new SetMonster(player, card.InstanceId, z), ActionKind.SetMonster, $"Set {name} [{z + 1}]", card.InstanceId, z);
                yield return B(new SetSpellTrap(player, card.InstanceId, z), ActionKind.SetSpellTrap, $"Set {name} [{z + 1}]", card.InstanceId, z);
            }
            yield return B(new ActivateSpell(player, card.InstanceId, DuelPlayerState.FieldZoneIndex), ActionKind.ActivateSpell, $"Activate {name} [Field]", card.InstanceId, DuelPlayerState.FieldZoneIndex);
            yield return B(new SetSpellTrap(player, card.InstanceId, DuelPlayerState.FieldZoneIndex), ActionKind.SetSpellTrap, $"Set {name} [Field]", card.InstanceId, DuelPlayerState.FieldZoneIndex);
            yield return B(new DiscardForHandLimit(player, card.InstanceId), ActionKind.Discard, $"Discard {name}", card.InstanceId);
            if (!engine.IsToonCard(card.GameCardId)) continue;
            foreach (var tributes in ZoneSubsets(own))
                for (int z = 0; z < 5; z++)
                    foreach (var position in new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense })
                        yield return B(new ToonSummon(player, card.InstanceId, z, position, tributes, state.Revision), ActionKind.Summon,
                            $"Toon Summon {name} [{z + 1}] {position}", card.InstanceId, z, position: position, target: tributes.Aggregate(0, (mask, t) => mask | 1 << t));
        }
        yield return B(new ActivateSetSpell(player, DuelPlayerState.FieldZoneIndex), ActionKind.ActivateSetSpell, "Activate set [Field]", own.FieldSpell?.Card.InstanceId, DuelPlayerState.FieldZoneIndex);
        foreach (var (command, instance, zone, position, tag) in engine.SummonProcedureOptions(state, player))
            yield return B(command, ActionKind.Summon, $"Special Summon {tag}", instance, zone, tag, position);
        for (int z = 0; z < 5; z++)
        {
            if (own.MonsterZones[z] is { } activated)
            {
                for (int i = 0; i < EffectsOf(activated.Card); i++)
                {
                    int detachCount = engine.DetachCostFor(activated.Card.GameCardId, i);
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
            for (int i = 0; i < EffectsOf(own.SpellTrapFields[z]?.Card); i++)
                yield return B(new ActivateSetSpell(player, z, i), ActionKind.ActivateSetSpell, $"Activate set [{z + 1}]", own.SpellTrapFields[z]?.Card.InstanceId, z, Option(i));
        }
        foreach (var (z, faceUp, i) in FaceUpEffects(own))
            yield return B(new ActivateCardEffect(player, z, i), ActionKind.CardEffect, $"Use {catalog[faceUp.GameCardId].Name} effect", faceUp.InstanceId, z, Option(i));
        foreach (var (zoneCard, i) in ZoneEffects(own))
            yield return B(new ActivateZoneEffect(player, zoneCard.InstanceId, i), ActionKind.CardEffect, $"Use {catalog[zoneCard.GameCardId].Name}", zoneCard.InstanceId, target: Option(i));
        foreach (var attack in Attacks(own, player)) yield return attack;
        foreach (var phase in Enum.GetValues<DuelPhase>())
            yield return B(new ChangePhase(player, phase), ActionKind.Phase, $"Phase: {phase}", phase: phase);
        yield return B(new EndTurn(player), ActionKind.EndTurn, "End turn");
        for (int z = 0; z < 5; z++)
            foreach (var position in Enum.GetValues<BattlePosition>())
                yield return B(new ChangeMonsterPosition(player, z, position), ActionKind.Position, $"Position {z + 1}: {position}", own.MonsterZones[z]?.Card.InstanceId, z, position: position);
    }

    /// <summary>Sets of 0 to 2 occupied Monster Zones, the Tribute choices a Toon Summon can use.</summary>
    private static IEnumerable<int[]> ZoneSubsets(DuelPlayerState own)
    {
        int[] occupied = Enumerable.Range(0, 5).Where(z => own.MonsterZones[z] is not null).ToArray();
        yield return [];
        for (int i = 0; i < occupied.Length; i++)
        {
            yield return [occupied[i]];
            for (int j = i + 1; j < occupied.Length; j++) yield return [occupied[i], occupied[j]];
        }
    }

    private static IEnumerable<Bound> Attacks(DuelPlayerState own, PlayerId player)
    {
        for (int z = 0; z < 5; z++)
        {
            for (int target = 0; target < 5; target++)
                yield return B(new DeclareAttack(player, z, target), ActionKind.Attack, $"Attack {z + 1} -> {target + 1}", own.MonsterZones[z]?.Card.InstanceId, z, target);
            yield return B(new DeclareAttack(player, z), ActionKind.Attack, $"Direct attack [{z + 1}]", own.MonsterZones[z]?.Card.InstanceId, z);
        }
    }
}
