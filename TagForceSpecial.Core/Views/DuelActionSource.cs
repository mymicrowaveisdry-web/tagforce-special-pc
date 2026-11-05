using TagForceSpecial.Cards;

namespace TagForceSpecial.Core.Views;

/// <summary>Trusted adapter. Owns command handles; clients get authorized, immutable observations only.</summary>
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
            : state.EventEffects.PendingCommitment is not null || state.EventEffects.PendingTriggers is not null
                ? "Trigger commitments/order are outside this action adapter"
            : state.PendingSummon is not null ? "Summon-negation window is outside this action adapter"
            : state.PendingChain?.SummonDecision is not null ? "Effect summon choice is outside this action adapter" : "";
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
        AuthorizedDecision? decision = state.PendingDecision is { } d && d.Player == player
            ? new(d.DecisionId, d.Kind.ToString(), PlayerProjection.Read(d.CardOptions.OrderBy(c => c.GameCardId).ThenBy(c => c.InstanceId)
                .Select(c => PlayerProjection.Card(c, true, null, catalog))))
            : state.PendingChain is { } chain && actor == player ? new(chain.DecisionId, "Chain", PlayerProjection.Read(Array.Empty<CardView>()))
            : state.Battle is { } battle && actor == player ? new(battle.Window.DecisionId,
                battle.Attack?.ReplayRequired == true && battle.Window.Closed ? "AttackReplay" : "Battle", PlayerProjection.Read(Array.Empty<CardView>())) : null;
        var view = new DuelObservation(state.Revision, player, actor, state.ActivePlayer, state.TurnNumber, state.Phase,
            PlayerProjection.Create(state, player, player, catalog),
            PlayerProjection.Create(state, Other(player), player, catalog), decision, status, unsupported,
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
    private static PlayerId Actor(DuelState state) => state.PendingDecision?.Player
        ?? (state.PendingChain is { } chain ? chain.NextLink > 0 ? state.ActivePlayer : chain.Responder
            : state.Battle is { } battle && !battle.Window.Closed ? battle.Window.Responder : state.ActivePlayer);

    private static Bound B(DuelCommand command, ActionKind kind, string label, int? card = null, int? zone = null,
        int? target = null, BattlePosition? position = null, DuelPhase? phase = null, PlayerId? targetPlayer = null)
        => new(command, new(0, $"{kind}|{card}|{zone}|{target}|{position}|{phase}|{targetPlayer}", kind, label, card, zone, target, position, phase));

    private IEnumerable<Bound> Candidates(DuelState state, PlayerId player)
    {
        var own = state.GetPlayer(player);
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
            yield break;
        }
        if (state.PendingChain is { } chain)
        {
            for (int z = 0; z < 5; z++)
                yield return B(new ActivateChainResponse(player, chain.DecisionId, z), ActionKind.ChainResponse, $"Respond zone {z + 1}", zone: z);
            yield return B(new PassChainResponse(player, chain.DecisionId), ActionKind.ChainPass, "Pass response");
            yield return B(new ResolveNextChainLink(player, chain.DecisionId), ActionKind.ResolveChain, "Resolve chain link");
            yield break;
        }
        if (state.Battle is { } battle)
        {
            for (int z = 0; z < 5; z++)
                yield return B(new ActivateBattleResponse(player, battle.Window.DecisionId, z), ActionKind.BattleResponse, $"Battle response [{z + 1}]", zone: z);
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
                yield return B(new ActivateSpell(player, card.InstanceId, z), ActionKind.ActivateSpell, $"Activate {name} [{z + 1}]", card.InstanceId, z);
                yield return B(new SetMonster(player, card.InstanceId, z), ActionKind.SetMonster, $"Set {name} [{z + 1}]", card.InstanceId, z);
                yield return B(new SetSpellTrap(player, card.InstanceId, z), ActionKind.SetSpellTrap, $"Set {name} [{z + 1}]", card.InstanceId, z);
            }
            yield return B(new DiscardForHandLimit(player, card.InstanceId), ActionKind.Discard, $"Discard {name}", card.InstanceId);
        }
        for (int z = 0; z < 5; z++)
        {
            yield return B(new ActivateSetSpell(player, z), ActionKind.ActivateSetSpell, $"Activate set [{z + 1}]", own.SpellTrapFields[z]?.Card.InstanceId, z);
        }
        foreach (var attack in Attacks(own, player)) yield return attack;
        foreach (var phase in Enum.GetValues<DuelPhase>())
            yield return B(new ChangePhase(player, phase), ActionKind.Phase, $"Phase: {phase}", phase: phase);
        yield return B(new EndTurn(player), ActionKind.EndTurn, "End turn");
        for (int z = 0; z < 5; z++)
            foreach (var position in Enum.GetValues<BattlePosition>())
                yield return B(new ChangeMonsterPosition(player, z, position), ActionKind.Position, $"Position {z + 1}: {position}", own.MonsterZones[z]?.Card.InstanceId, z, position: position);
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
