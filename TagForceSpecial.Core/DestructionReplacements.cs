namespace TagForceSpecial.Core;

/// <summary>Which cards a destruction replacement protects.</summary>
public enum ReplacementSubject { Self, Equipped, Monsters }

/// <summary>What is done instead of the destruction.</summary>
public enum ReplacementAction { DetachMaterial, DestroySource, SendSourceToGraveyard, SendFromDeckToGraveyard, BanishFromGraveyard, DestroyOwnMonster }

/// <summary>The replacement's cost; <paramref name="Cards"/> limits the card a Deck, Graveyard or field action uses, and LP are paid first.</summary>
public sealed record ReplacementCost(ReplacementAction Action, CardFilter? Cards = null, int LifePoints = 0);

/// <summary>
/// "If [subject] would be destroyed, (you can) [cost] instead", while the source is face-up and its effect applies.
/// <paramref name="All"/> protects every matching card in one destruction for a single cost; <paramref name="OnlyIfSole"/> needs it to be the only card destroyed.
/// </summary>
public sealed record DestructionReplacementProfile(string EffectId, uint CardId, ReplacementSubject Subject, ReplacementCost Cost,
    DestructionCause Cause = DestructionCause.Any, bool Optional = true, bool OncePerTurn = false, CardFilter? Cards = null,
    TargetSide Side = TargetSide.Own, bool All = false, bool OnlyIfSole = false);

/// <summary>One optional replacement the controller may use; <see cref="Subject"/> is null when it covers every matching card.</summary>
public sealed record ReplacementOffer(PlayerId Player, string EffectId, CardInstance Source, CardInstance? Subject, IReadOnlyList<CardInstance> CardOptions);
public sealed record ReplacementDecision(string EffectId, int SourceInstanceId, int? SubjectInstanceId, bool Use, int? CardInstanceId);

/// <summary>Optional replacements asked one at a time before a destruction is applied.</summary>
public sealed record PendingReplacement(long DecisionId, IReadOnlyList<ReplacementOffer> Offers, IReadOnlyList<ReplacementDecision> Decisions)
{
    public ReplacementOffer Current => Offers[Decisions.Count];
}

/// <summary>A resolving effect paused before it destroys cards, while optional replacements are answered.</summary>
public sealed class PendingReplacementChoice : PendingDefinitionChoice
{
    public PendingReplacement Replacement { get; }
    internal PendingReplacementChoice(PendingReplacement replacement, string effectId, int linkNumber, int stepIndex)
        : base(replacement.DecisionId, replacement.Current.Player, effectId, linkNumber, stepIndex) => Replacement = replacement;
}

/// <summary>Uses or declines the current optional replacement; a replacement that needs a card names it.</summary>
public sealed record ChooseDestructionReplacement(PlayerId Player, long DecisionId, bool Use, int? CardInstanceId = null) : DuelCommand(Player);
internal sealed record ReplacementAnswers(PlayerId Player, IReadOnlyList<ReplacementDecision> Decisions) : DuelCommand(Player);
public sealed record DestructionReplacementRequested(PendingReplacement Pending) : DuelEvent;

public sealed partial class DuelEngine
{
    private readonly record struct ActiveReplacement(PlayerId Controller, DestructionReplacementProfile Profile, FieldCardTarget Source, MonsterBinding? Equipped);

    /// <summary>The cards one destruction is about to destroy and the answers given for it.</summary>
    private sealed record ReplacementContext(IReadOnlyList<FieldCardTarget> Batch, IReadOnlyList<ReplacementDecision> Decisions)
    {
        public HashSet<(string EffectId, int Source)> Paid { get; } = [];
        /// <summary>All-at-once replacements already paid for; they keep covering this destruction after their source is gone.</summary>
        public List<ActiveReplacement> PaidForAll { get; } = [];
    }

    private ReplacementContext? replacementContext;

    private static bool SameCard(FieldCardTarget a, FieldCardTarget b) => a.Card == b.Card && a.PlacementId == b.PlacementId;

    private IEnumerable<ActiveReplacement> ActiveReplacements(DuelState state)
    {
        if (replacementProfiles.Count == 0) yield break;
        foreach (var (binding, monster) in FieldMonsters(state))
            if (monster.Position != BattlePosition.FaceDownDefense && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                replacementProfiles.TryGetValue(monster.Card.GameCardId, out var monsterProfiles))
                foreach (var profile in monsterProfiles)
                    yield return new(binding.Player, profile, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, monster.Card, monster.PlacementId), null);
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (player, zone, field) in ActiveCards(state))
            if (!(spellsNegated && IsSpellCard(field)) && replacementProfiles.TryGetValue(field.Card.GameCardId, out var cardProfiles))
                foreach (var profile in cardProfiles)
                    yield return new(player, profile, new(player, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId), field.EquippedTo);
    }

    private bool Covers(DuelState state, ActiveReplacement active, FieldCardTarget subject, DestructionCause cause)
    {
        var profile = active.Profile;
        if ((profile.Cause & cause) == 0) return false;
        switch (profile.Subject)
        {
            case ReplacementSubject.Self:
                return SameCard(subject, active.Source);
            case ReplacementSubject.Equipped:
                return active.Equipped is { } equipped && subject.Location == FieldLocation.MonsterZone && equipped.Card == subject.Card && equipped.PlacementId == subject.PlacementId;
            default:
                if (subject.Location != FieldLocation.MonsterZone || profile.Side != TargetSide.Either && (profile.Side == TargetSide.Own) != (subject.Player == active.Controller)) return false;
                return state.GetPlayer(subject.Player).MonsterZones[subject.Zone] is { Position: not BattlePosition.FaceDownDefense } &&
                    (profile.Cards is null || MatchesFilter(subject.Card, profile.Cards));
        }
    }

    private static bool NeedsCard(ReplacementAction action) => action is ReplacementAction.DetachMaterial or ReplacementAction.SendFromDeckToGraveyard
        or ReplacementAction.BanishFromGraveyard or ReplacementAction.DestroyOwnMonster;

    /// <summary>The cards this replacement's cost could use, given the cards about to be destroyed.</summary>
    private CardInstance[] CostOptions(DuelState state, ActiveReplacement active, FieldCardTarget subject, IReadOnlyList<FieldCardTarget>? batch)
    {
        var cost = active.Profile.Cost;
        var player = state.GetPlayer(active.Controller);
        return cost.Action switch
        {
            ReplacementAction.DetachMaterial => active.Source.Location == FieldLocation.MonsterZone && player.MonsterZones[active.Source.Zone] is { } host ? [.. host.XyzMaterials] : [],
            ReplacementAction.SendFromDeckToGraveyard => [.. player.Deck.Where(c => cost.Cards is null || MatchesFilter(c, cost.Cards))],
            ReplacementAction.BanishFromGraveyard => [.. player.Graveyard.Where(c => cost.Cards is null || MatchesFilter(c, cost.Cards, inGraveyard: true))],
            ReplacementAction.DestroyOwnMonster => [.. player.MonsterZones.Select((m, zone) => (m, zone))
                .Where(e => e.m is { Position: not BattlePosition.FaceDownDefense } && e.m.Card != subject.Card && (cost.Cards is null || MatchesFilter(e.m.Card, cost.Cards)) &&
                    !(batch ?? []).Any(t => t.Card == e.m.Card))
                .Select(e => e.m!.Card)],
            _ => []
        };
    }

    private bool Usable(DuelState state, ActiveReplacement active, FieldCardTarget subject, IReadOnlyList<FieldCardTarget>? batch)
    {
        var profile = active.Profile;
        bool selfSubject = SameCard(subject, active.Source);
        // A card destroyed at the same time cannot be used instead of another card.
        if (!selfSubject && batch is not null && batch.Any(t => SameCard(t, active.Source))) return false;
        if (selfSubject && profile.Cost.Action is ReplacementAction.DestroySource or ReplacementAction.SendSourceToGraveyard) return false;
        if (profile.OnlyIfSole && batch is not { Count: 1 }) return false;
        if (profile.OncePerTurn && state.Bookkeeping.LockUses.Any(u => u.InstanceId == active.Source.Card.InstanceId && u.EffectId == profile.EffectId && u.TurnNumber == state.TurnNumber))
            return false;
        if (profile.Cost.LifePoints > 0 && state.GetPlayer(active.Controller).LifePoints <= profile.Cost.LifePoints) return false;
        return !NeedsCard(profile.Cost.Action) || CostOptions(state, active, subject, batch).Length != 0;
    }

    /// <summary>The optional replacements that could apply to this destruction, turn player's first.</summary>
    private List<ReplacementOffer> OptionalOffers(DuelState state, IReadOnlyList<FieldCardTarget> batch, DestructionCause cause)
    {
        List<ReplacementOffer> offers = [];
        foreach (var active in ActiveReplacements(state).Where(a => a.Profile.Optional).OrderBy(a => a.Controller != state.ActivePlayer))
        {
            var subjects = batch.Where(s => Covers(state, active, s, cause) && Usable(state, active, s, batch)).ToArray();
            if (subjects.Length == 0) continue;
            if (active.Profile.All)
                offers.Add(new(active.Controller, active.Profile.EffectId, active.Source.Card, null, CostOptions(state, active, subjects[0], batch)));
            else
                offers.AddRange(subjects.Select(s => new ReplacementOffer(active.Controller, active.Profile.EffectId, active.Source.Card, s.Card, CostOptions(state, active, s, batch))));
        }
        return offers;
    }

    /// <summary>Applies a mandatory replacement, or an optional one the controller chose, instead of destroying this card; null when none applies.</summary>
    private DuelState? ProfileReplacement(DuelState state, FieldCardTarget subject, DestructionCause cause, List<DuelEvent> events)
    {
        var context = replacementContext;
        if (context?.PaidForAll.FirstOrDefault(a => Covers(state, a, subject, cause)) is { Profile: not null } paid)
            return Replaced(state, subject, paid.Source.Card, events);
        foreach (var active in ActiveReplacements(state).ToArray())
        {
            var profile = active.Profile;
            if (!Covers(state, active, subject, cause)) continue;
            var key = (profile.EffectId, active.Source.Card.InstanceId);
            if (profile.All && context is not null && context.Paid.Contains(key)) return Replaced(state, subject, active.Source.Card, events);
            if (!Usable(state, active, subject, context?.Batch)) continue;
            int? pick = null;
            if (profile.Optional)
            {
                if (context?.Decisions.FirstOrDefault(d => d.Use && d.EffectId == profile.EffectId && d.SourceInstanceId == key.Item2 &&
                        (profile.All || d.SubjectInstanceId == subject.Card.InstanceId)) is not { } decision) continue;
                pick = decision.CardInstanceId;
            }
            var options = CostOptions(state, active, subject, context?.Batch);
            var card = options.FirstOrDefault(c => c.InstanceId == pick) ?? options.FirstOrDefault();
            state = PayReplacement(state, active, card, events);
            if (profile.OncePerTurn)
                state = state.WithBookkeeping(state.Bookkeeping with { LockUses = Array.AsReadOnly(state.Bookkeeping.LockUses.Where(u => u.TurnNumber == state.TurnNumber)
                    .Append(new(active.Source.Card.InstanceId, profile.EffectId, state.TurnNumber)).ToArray()) });
            context?.Paid.Add(key);
            if (profile.All) context?.PaidForAll.Add(active);
            return Replaced(state, subject, active.Source.Card, events);
        }
        return null;
    }

    private static DuelState Replaced(DuelState state, FieldCardTarget subject, CardInstance source, List<DuelEvent> events)
    {
        events.Add(subject.Location == FieldLocation.MonsterZone
            ? new DestructionReplaced(new(subject.Player, subject.Zone, subject.Card, subject.PlacementId), source)
            : new SpellTrapDestructionReplaced(subject.Player, subject.Zone, subject.Card));
        return state;
    }

    private DuelState PayReplacement(DuelState state, ActiveReplacement active, CardInstance? card, List<DuelEvent> events)
    {
        var cost = active.Profile.Cost;
        if (cost.LifePoints > 0)
        {
            var payer = state.GetPlayer(active.Controller);
            state = state.With(player: payer.With(lifePoints: payer.LifePoints - cost.LifePoints));
            events.Add(new LifePointsPaid(payer.Id, cost.LifePoints, payer.LifePoints, payer.LifePoints - cost.LifePoints, active.Profile.EffectId));
        }
        var source = active.Source;
        switch (cost.Action)
        {
            case ReplacementAction.DetachMaterial:
            {
                var player = state.GetPlayer(source.Player);
                var zones = player.MonsterZones.ToArray();
                zones[source.Zone] = zones[source.Zone]! with { XyzMaterials = Array.AsReadOnly(zones[source.Zone]!.XyzMaterials.Where(c => c != card).ToArray()) };
                return SendXyzMaterials(state.With(player: player.With(monsters: zones)), source.Card, [card!], true, events);
            }
            case ReplacementAction.DestroySource:
                return source.IsStillPresent(state) ? DestroyFieldCard(state, source, events, replaceable: false) : state;
            case ReplacementAction.SendSourceToGraveyard:
                return source.IsStillPresent(state)
                    ? RemoveFieldSpell(state, new(source.Player, source.Zone, source.Card, source.PlacementId), new SpellSentToGraveyard(source.Player, source.Card, source.Zone), events)
                    : state;
            case ReplacementAction.SendFromDeckToGraveyard:
                return MoveChosenDeckCard(state, active.Controller, card!.InstanceId, toHand: false, active.Profile.EffectId, events);
            case ReplacementAction.BanishFromGraveyard:
            {
                var owner = state.GetPlayer(active.Controller);
                events.Add(new CardBanishedFromGraveyard(owner.Id, card!, active.Profile.EffectId));
                return state.With(player: owner.With(graveyard: owner.Graveyard.Where(c => c != card), banished: owner.Banished.Append(card!)));
            }
            default:
            {
                var player = state.GetPlayer(active.Controller);
                int zone = Array.FindIndex(player.MonsterZones.ToArray(), m => m?.Card == card);
                return zone < 0 ? state : DestroyFieldCard(state, new(player.Id, FieldLocation.MonsterZone, zone, card!, player.MonsterZones[zone]!.PlacementId), events, replaceable: false);
            }
        }
    }

    /// <summary>The field cards a destroying definition step is about to destroy; null for other steps.</summary>
    private IReadOnlyList<FieldCardTarget>? DestructionBatch(DuelState state, ChainLink[] links, ChainLink link, EffectDefinition definition, EffectStep step,
        PlayerId controller) => step switch
    {
        NegateActivationStep { DestroySource: true } or NegateEffectStep { DestroySource: true } when link.TargetLinkNumber is int number &&
            NegatedSource(state, links[number - 1]) is { } negated => [negated],
        DestroyTargetsStep or DestroyAndBanishTargetsStep => QualifyingTargets(state, link, definition)
            .Where(t => t.Location is FieldLocation.MonsterZone or FieldLocation.SpellTrapZone).ToArray(),
        DestroyAllStep all => FieldMatches(state, controller, all.Kind, all.Side, all.Filter, link.Activation.Spell.InstanceId)
            .Where(t => all.Cards is null || MatchesFilter(t.Card, all.Cards)).ToArray(),
        DestroyOpponentFaceUpMonstersStep => FieldMonsters(state).Where(e => e.Binding.Player != controller && e.Monster.Position != BattlePosition.FaceDownDefense)
            .Select(e => new FieldCardTarget(e.Binding.Player, FieldLocation.MonsterZone, e.Binding.Zone, e.Monster.Card, e.Monster.PlacementId)).ToArray(),
        _ => null
    };

    /// <summary>The field card a negation that also destroys would destroy, if it is still in place.</summary>
    private static FieldCardTarget? NegatedSource(DuelState state, ChainLink target)
    {
        var receipt = target.Activation;
        FieldCardTarget? source = target.Operation switch
        {
            ChainOperation.MonsterEffect => new(receipt.Player, FieldLocation.MonsterZone, receipt.SpellTrapZone, receipt.Spell, target.SourcePlacementId),
            ChainOperation.ZoneEffect => null,
            _ when target.Trigger is null && receipt.SpellTrapZone is >= 0 and < 5 =>
                new(receipt.Player, FieldLocation.SpellTrapZone, receipt.SpellTrapZone, receipt.Spell, target.SourcePlacementId),
            _ => null
        };
        return source is not null && source.IsStillPresent(state) ? source : null;
    }

    private bool ValidReplacementAnswer(PendingReplacement pending, ChooseDestructionReplacement answer, out ReplacementDecision decision)
    {
        var offer = pending.Current;
        decision = new(offer.EffectId, offer.Source.InstanceId, offer.Subject?.InstanceId, answer.Use, answer.CardInstanceId);
        if (answer.DecisionId != pending.DecisionId || answer.Player != offer.Player) return false;
        if (!answer.Use) return answer.CardInstanceId is null;
        return offer.CardOptions.Count == 0 ? answer.CardInstanceId is null : offer.CardOptions.Any(c => c.InstanceId == answer.CardInstanceId);
    }

    private DuelActionResult AnswerChainReplacement(PendingChain chain, PendingReplacementChoice choice, ChooseDestructionReplacement answer)
    {
        var pending = choice.Replacement;
        if (answer.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (answer.Player != pending.Current.Player) return Reject(DuelError.NotActivePlayer);
        if (!ValidReplacementAnswer(pending, answer, out var decision)) return Reject(DuelError.InvalidDecisionChoice);
        ReplacementDecision[] decisions = [.. pending.Decisions, decision];
        if (decisions.Length < pending.Offers.Count)
        {
            var next = new PendingReplacement(checked(State.Revision + 1), pending.Offers, decisions);
            var paused = new PendingReplacementChoice(next, choice.EffectId, choice.LinkNumber, choice.StepIndex);
            return Accept(State.WithChain(new(next.DecisionId, chain.Links, chain.Responder, chain.ConsecutivePasses, chain.NextLink, definitionChoice: paused)),
                new DestructionReplacementRequested(next));
        }
        return ResolveChainLink(chain, definitionChoice: new ReplacementAnswers(answer.Player, decisions));
    }

    private DuelActionResult AnswerBattleReplacement(BattleState battle, PendingReplacement pending, ChooseDestructionReplacement answer)
    {
        if (answer.DecisionId != pending.DecisionId) return Reject(DuelError.StaleDecision);
        if (answer.Player != pending.Current.Player) return Reject(DuelError.NotActivePlayer);
        if (!ValidReplacementAnswer(pending, answer, out var decision)) return Reject(DuelError.InvalidDecisionChoice);
        ReplacementDecision[] decisions = [.. pending.Decisions, decision];
        if (decisions.Length < pending.Offers.Count)
        {
            var next = new PendingReplacement(checked(State.Revision + 1), pending.Offers, decisions);
            return Accept(State.WithBattle(battle with { Replacement = next }), new DestructionReplacementRequested(next));
        }
        return CalculateStagedDamage(decisions);
    }
}
