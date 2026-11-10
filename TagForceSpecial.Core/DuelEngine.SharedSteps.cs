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
        var kinds = group.Select(d => d.Kind).ToArray();
        if (kinds.Contains(EffectKind.SpellTrapIgnition))
            return kinds[0] is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell && kinds.Skip(1).All(k => k == EffectKind.SpellTrapIgnition);
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
    private DuelActionResult BeginFieldEffect(PlayerId playerId, int zone, int effectIndex)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or > DuelPlayerState.FieldZoneIndex) return Reject(DuelError.InvalidSpellTrapZone);
        var field = State.GetPlayer(playerId).SpellTrapAt(zone);
        if (field is not { IsFaceUp: true, EffectActive: true } || !EffectAt(field.Card.GameCardId, effectIndex, out var definition) || definition.Kind != EffectKind.SpellTrapIgnition)
            return Reject(DuelError.UnsupportedSpellEffect);
        if (definition.OncePerTurn && UsedThisTurn(field.Card, definition)) return Reject(DuelError.EffectAlreadyUsed);
        if (DefinitionActivationError(definition, ActivationContext.SpellTrapField, playerId, field.Card, field) is not DuelError.None and var error) return Reject(error);
        var next = definition.OncePerTurn ? RecordUse(State, field.Card, definition) : State;
        var (paid, head) = PayLifePointCost(next, playerId, definition, [new FieldEffectActivated(playerId, definition.EffectId, field.Card, zone)]);
        return ContinueCommit(paid, head, playerId, field.Card, zone, definition, field.PlacementId, null);
    }

    private bool DiscardQualifies(CardInstance card, CardFilter? filter) => filter switch
    {
        null => true,
        CardIdFilter ids => ids.CardIds.Contains(card.GameCardId),
        _ => MatchesFilter(card, filter)
    };
}
