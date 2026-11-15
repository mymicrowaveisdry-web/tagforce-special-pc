using TagForceSpecial.Cards;

namespace TagForceSpecial.Core.Views;

/// <summary>Trusted adapter that owns command handles and gives clients authorized, immutable observations.</summary>
public sealed class DuelActionSource
{
    public const string ContractVersion = "authorized-actions-v1";
    public const int MaximumCandidates = 4096;
    private readonly DuelEngine engine;
    private readonly CardCatalog catalog;
    private readonly Dictionary<PlayerId, (DuelObservation View, ActionCandidate[] Commands)> cache = [];

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
        ActionCandidate[] commands = [];
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
            : state.PendingChain?.Declaration is { } declaring && declaring.Player == player
                ? new(declaring.DecisionId, "Declaration", PlayerProjection.Read(Array.Empty<CardView>()))
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

    private IEnumerable<ActionCandidate> Candidates(DuelState state, PlayerId player)
    {
        IEnumerable<ActionCandidate> candidates;
        if (state.EventEffects.PendingCommitment is not null || state.EventEffects.PendingTriggers is not null || state.PendingDecision is not null)
            candidates = DecisionActionCandidates.Build(state, player, catalog, engine.DeclarationValid);
        else if (state.OpenWindow is not null || state.PendingChain is null && state.PendingSummon is not null)
            candidates = ResponseActionCandidates.Build(state, player, catalog, engine.ActionEffects);
        else if (state.PendingChain?.SummonDecision is not null || (state.PendingChain?.ReplacementChoice?.Replacement ?? state.Battle?.Replacement) is not null ||
            state.PendingChain?.DefinitionSummon is not null || state.PendingChain?.Declaration is not null ||
            state.PendingChain?.DefinitionChoice is PendingTypeCall || state.PendingChain?.DefinitionCardChoice is not null)
            candidates = DecisionActionCandidates.Build(state, player, catalog, engine.DeclarationValid);
        else if (state.PendingChain is not null || state.Battle is not null)
            candidates = ResponseActionCandidates.Build(state, player, catalog, engine.ActionEffects);
        else
            candidates = TurnActionCandidates.Build(state, player, catalog, engine.ActionEffects, engine.TributeOptions, engine.SummonProcedureOptions);
        foreach (var candidate in candidates) yield return candidate;
    }
}
