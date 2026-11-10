namespace TagForceSpecial.Core;

/// <summary>Small effect steps many cards share: stat changes that end with the turn, flips by effect and extra Normal Summons.</summary>
public sealed partial class DuelEngine
{
    /// <summary>Adds a stat change that ends when the End Phase begins.</summary>
    private DuelState ModifyUntilEnd(DuelState state, FieldCardTarget target, ModifyTargetsUntilEndStep stats, string effectId, List<DuelEvent> events)
    {
        var effects = state.EventEffects;
        var modifier = new StatModifier(effectId, null, new(target.Player, target.Zone, target.Card, target.PlacementId), stats.Attack, stats.Defense,
            checked(State.Revision + 1), State.TurnNumber);
        events.Add(new MonsterModifierCreated(modifier));
        return state.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    /// <summary>Turns a face-up monster face-down; Tokens and Xyz Monsters cannot be. Counters and a Gemini's effects are lost.</summary>
    private DuelState FlipFaceDown(DuelState state, FieldCardTarget target, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (player.MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense } monster || IsTokenId(monster.Card.GameCardId) || IsXyzCard(monster.Card))
            return state;
        var zones = player.MonsterZones.ToArray();
        zones[target.Zone] = monster with { Position = BattlePosition.FaceDownDefense, Counters = 0, GeminiActive = false };
        events.Add(new MonsterPositionChanged(player.Id, target.Zone, monster.Position, BattlePosition.FaceDownDefense));
        return state.With(player: player.With(monsters: zones));
    }

    /// <summary>Flips a face-down monster face-up by an effect, which counts as flipped for Flip effects but is not a Flip Summon.</summary>
    private static DuelState FlipFaceUp(DuelState state, FieldCardTarget target, FlipTargetsFaceUpStep step, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (player.MonsterZones[target.Zone] is not { Position: BattlePosition.FaceDownDefense } monster) return state;
        var zones = player.MonsterZones.ToArray();
        zones[target.Zone] = monster with { Position = step.Position };
        events.Add(new MonsterPositionChanged(player.Id, target.Zone, BattlePosition.FaceDownDefense, step.Position));
        events.Add(new MonsterFlippedByEffect(new(target.Player, target.Zone, target.Card, target.PlacementId)));
        return state.With(player: player.With(monsters: zones));
    }

    /// <summary>Allows one more Normal Summon or Set this turn; further grants in the same turn add nothing.</summary>
    private static DuelState GrantNormalSummon(DuelState state, string effectId, List<DuelEvent> events)
    {
        if (state.Bookkeeping.ExtraNormalSummonTurn == state.TurnNumber) return state;
        events.Add(new ExtraNormalSummonGranted(state.ActivePlayer, effectId));
        return state.WithBookkeeping(state.Bookkeeping with { ExtraNormalSummonTurn = state.TurnNumber, ExtraNormalSummonUsed = false });
    }

    private static bool NormalSummonAvailable(DuelState state) =>
        !state.NormalSummonUsed || state.Bookkeeping.ExtraNormalSummonTurn == state.TurnNumber && !state.Bookkeeping.ExtraNormalSummonUsed;

    /// <summary>Spends the turn's Normal Summon, or the extra one once that is gone.</summary>
    private static DuelState SpendNormalSummon(DuelState state) => state.NormalSummonUsed
        ? state.WithBookkeeping(state.Bookkeeping with { ExtraNormalSummonUsed = true })
        : state.With(normalSummonUsed: true);

    /// <summary>
    /// The state with every card that could be discarded already in the Graveyard, so a card about to be discarded counts
    /// as a possible target. Null when the cost cannot supply the targets by itself.
    /// </summary>
    private DuelState? DiscardPreview(PlayerId playerId, CardInstance source, EffectDefinition definition)
    {
        if (definition.Cost is not DiscardCost discard) return null;
        int needed = definition.FieldTargets.OfType<ZoneTarget>().Where(z => z.Zone == CardZone.Graveyard).Sum(z => z.MinCount);
        if (needed == 0 || needed > discard.Count) return null;
        var player = State.GetPlayer(playerId);
        return State.With(player: player.With(graveyard: player.Graveyard.Concat(player.Hand.Where(c => c != source && DiscardQualifies(c, discard.Filter)))));
    }

    /// <summary>Several effects are allowed on monsters, as "choose 1" options of one kind, or as activated effects after a lasting card's own.</summary>
    private static bool EffectGroupsValid(EffectDefinition[] effects) => effects.GroupBy(d => d.CardId).All(group =>
    {
        var kinds = group.Where(d => d.Origin == EffectOrigin.Field).Select(d => d.Kind).ToArray();
        if (kinds.Length == 0) return group.All(d => d.IsMonsterEffect);
        if (kinds.Any(k => k is EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick))
            return kinds[0] is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell && kinds.Skip(1).All(k => k is EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick);
        return kinds.Length == 1 || kinds.All(k => k is EffectKind.MonsterIgnition or EffectKind.MonsterQuick) ||
            kinds.All(k => k == kinds[0] && k is EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap);
    });

    private bool EffectAt(uint cardId, int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EffectDefinition? definition)
    {
        definition = effectsByCard.TryGetValue(cardId, out var effects) && index >= 0 && index < effects.Length ? effects[index] : null;
        return definition is not null;
    }

    /// <summary>How many separately chosen effects this card has; 1 when it has none registered.</summary>
    internal int EffectTotal(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects) ? effects.Length : 1;

    private EffectDefinition LinkDefinition(ChainLink link) => link.Trigger?.Profile.Definition ?? effectsById[link.Activation.EffectId];

    /// <summary>Starts a face-up Spell or Trap's activated effect. The card stays in place and the effect resolves even if the card then leaves.</summary>
    private DuelActionResult BeginFieldEffect(PlayerId playerId, int zone, int effectIndex, PendingChain? chain = null, DuelState? from = null, bool window = false)
    {
        if (chain is null && !window && State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or > DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        var field = State.GetPlayer(playerId).SpellTrapAt(zone);
        if (field is not { IsFaceUp: true, EffectActive: true } || !EffectAt(field.Card.GameCardId, effectIndex, out var definition) || !definition.IsFaceUpEffect || definition.Origin != EffectOrigin.Field)
            return Reject(DuelError.UnsupportedSpellEffect);
        if (definition.OncePerTurn && UsedThisTurn(field.Card, definition)) return Reject(DuelError.EffectAlreadyUsed);
        var context = window ? ActivationContext.SpellTrapWindow : chain is null ? ActivationContext.SpellTrapField : ActivationContext.SpellTrapChain;
        if (DefinitionActivationError(definition, context, playerId, field.Card, field, chain?.Links[^1]) is not DuelError.None and var error) return Reject(error);
        var start = from ?? State;
        var next = definition.OncePerTurn ? RecordUse(start, field.Card, definition) : start;
        var (paid, head) = PayLifePointCost(next, playerId, definition, [new FieldEffectActivated(playerId, definition.EffectId, field.Card, zone)]);
        return ContinueCommit(paid, head, playerId, field.Card, zone, definition, field.PlacementId, chain);
    }

    /// <summary>The effects of this card used from the hand or Graveyard.</summary>
    internal IEnumerable<int> ZoneEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].Origin != EffectOrigin.Field) : [];

    /// <summary>The Quick Effects of this card used while it is face-up.</summary>
    internal IEnumerable<int> FaceUpEffectIndexes(uint cardId) => effectsByCard.TryGetValue(cardId, out var effects)
        ? Enumerable.Range(0, effects.Length).Where(i => effects[i].IsFaceUpEffect && effects[i].Origin == EffectOrigin.Field) : [];

    private bool? zoneQuicks;
    private bool HasZoneOrFaceUpQuicks => zoneQuicks ??= allEffects.Any(d => d.Kind is EffectKind.MonsterQuick or EffectKind.SpellTrapQuick && (d.Origin != EffectOrigin.Field || d.IsFaceUpEffect));

    /// <summary>True when the player could use a quick effect from any location in an open window now.</summary>
    private bool QuickEffectReady(PlayerId playerId)
    {
        if (QuickMonsterReady(playerId)) return true;
        if (!HasZoneOrFaceUpQuicks) return false;
        var player = State.GetPlayer(playerId);
        bool Ready(EffectDefinition d, CardInstance card) => d.Kind is EffectKind.MonsterQuick or EffectKind.SpellTrapQuick && !(d.OncePerTurn && UsedThisTurn(card, d));
        foreach (var card in player.Hand.Concat(player.Graveyard))
            if (effectsByCard.TryGetValue(card.GameCardId, out var effects))
            {
                var origin = player.Hand.Contains(card) ? EffectOrigin.Hand : EffectOrigin.Graveyard;
                if (effects.Any(d => d.Origin == origin && Ready(d, card) && DefinitionActivationError(d, ActivationContext.ZoneWindow, playerId, card, null) == DuelError.None)) return true;
            }
        return ActiveCards(State).Where(e => e.Player == playerId).Any(e => effectsByCard.TryGetValue(e.Field.Card.GameCardId, out var effects) &&
            effects.Any(d => d.IsFaceUpEffect && Ready(d, e.Field.Card) && DefinitionActivationError(d, ActivationContext.SpellTrapWindow, playerId, e.Field.Card, e.Field) == DuelError.None));
    }

    private static bool SpellTrapSourcePresent(DuelState state, MonsterBinding source) =>
        state.GetPlayer(source.Player).SpellTrapAt(source.Zone) is { IsFaceUp: true, EffectActive: true } field && field.Card == source.Card && field.PlacementId == source.PlacementId;

    private bool SelfSummonPossible(PlayerId playerId, CardInstance card, EffectDefinition definition, SpecialSummonSelfStep step)
    {
        var from = definition.Origin == EffectOrigin.Hand ? DecisionCardSource.Hand : DecisionCardSource.Graveyard;
        return Enumerable.Range(0, 5).Any(zone => CheckSpecialSummon(State, playerId, card, zone, step.Position ?? BattlePosition.FaceUpAttack, from) == DuelError.None);
    }

    /// <summary>Starts an effect used from the hand or Graveyard. A self-cost moves the card first, so it never targets itself.</summary>
    private DuelActionResult BeginZoneEffect(PlayerId playerId, int instanceId, int effectIndex, PendingChain? chain, DuelState? from = null, bool window = false)
    {
        if (chain is null && !window && State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(playerId);
        var inHand = player.Hand.SingleOrDefault(c => c.InstanceId == instanceId);
        var card = inHand ?? player.Graveyard.SingleOrDefault(c => c.InstanceId == instanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!EffectAt(card.GameCardId, effectIndex, out var definition) || definition.Origin != (inHand is not null ? EffectOrigin.Hand : EffectOrigin.Graveyard))
            return Reject(DuelError.UnsupportedMonsterEffect);
        if (definition.OncePerTurn && UsedThisTurn(card, definition)) return Reject(DuelError.EffectAlreadyUsed);
        var context = window ? ActivationContext.ZoneWindow : chain is null ? ActivationContext.ZoneField : ActivationContext.ZoneChain;
        if (DefinitionActivationError(definition, context, playerId, card, null, chain?.Links[^1]) is not DuelError.None and var error) return Reject(error);
        var next = from ?? State;
        if (definition.OncePerTurn) next = RecordUse(next, card, definition);
        List<DuelEvent> paid = [new MonsterEffectActivated(playerId, definition.EffectId, card, -1)];
        if (definition.Cost is SendSelfCost self)
        {
            var owner = next.GetPlayer(playerId);
            if (self.Destination == SelfDestination.Banish)
            {
                next = next.With(player: owner.With(graveyard: owner.Graveyard.Where(c => c != card), banished: owner.Banished.Append(card)));
                paid.Add(new CardBanishedFromGraveyard(playerId, card, definition.EffectId));
            }
            else
            {
                next = next.With(player: owner.With(hand: owner.Hand.Where(c => c != card), graveyard: owner.Graveyard.Append(card)));
                paid.Add(new ActivationCostPaid(playerId, definition.EffectId, card));
            }
        }
        var (charged, head) = PayLifePointCost(next, playerId, definition, [.. paid]);
        return ContinueCommit(charged, head, playerId, card, -1, definition, 0, chain);
    }

    private bool DiscardQualifies(CardInstance card, CardFilter? filter) => filter switch
    {
        null => true,
        CardIdFilter ids => ids.CardIds.Contains(card.GameCardId),
        _ => MatchesFilter(card, filter)
    };
}
