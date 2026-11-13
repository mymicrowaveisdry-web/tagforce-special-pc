namespace TagForceSpecial.Core;

/// <summary>
/// Cards that stay on the field and keep acting: stat changes, Equip and revival links, activated monster effects,
/// Union, Toon, replacement, counters, upkeep and continuous negation.
/// </summary>
public sealed partial class DuelEngine
{
    public const string ToonProcedureId = "toon-special-summon";
    private const int ToonAttackCost = 500;

    private bool? lastingModifiers;
    private bool HasLastingModifiers => lastingModifiers ??= allEffects.Any(d => d.LastingModifiers.Count != 0 || d.AttackPerCounter != 0);

    private bool? monsterNegators;
    private bool HasMonsterNegators => monsterNegators ??= allEffects.Any(d => d.Negation == LastingNegation.FaceUpMonsters);

    private bool? spellNegators;
    private bool HasSpellNegators => spellNegators ??= allEffects.Any(d => d.Negation == LastingNegation.Spells);

    private bool? fieldLinks;
    private bool HasFieldLinks => fieldLinks ??= rules.Values.Any(r => r.IsToon) || allEffects.Any(d => d.Kind == EffectKind.EquipSpell ||
        d.Steps.Any(s => s is UnionEquipStep || s is SpecialSummonTargetStep { Link: RevivalLink.Haunted or RevivalLink.Equip }));

    internal bool IsToonCard(uint cardId) => rules.TryGetValue(cardId, out var rule) && rule.IsToon;

    /// <summary>The materials a monster's activated effect detaches as its cost; 0 when it has no such cost.</summary>
    internal int DetachCostFor(uint cardId, int effectIndex = 0) => EffectAt(cardId, effectIndex, out var d) && d.Cost is DetachCost detach ? detach.Count : 0;

    private static IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> ActiveCards(DuelState state) =>
        state.Players.SelectMany(p => Enumerable.Range(0, DuelPlayerState.FieldZoneIndex + 1).Select(zone => (Player: p.Id, Zone: zone, Field: p.SpellTrapAt(zone))))
            .Where(e => e.Field is { IsFaceUp: true, EffectActive: true }).Select(e => (e.Player, e.Zone, e.Field!));

    private bool IsSpellsNegated(DuelState state) => HasSpellNegators && ActiveCards(state)
        .Any(e => definitions.TryGetValue(e.Field.Card.GameCardId, out var d) && d.Negation == LastingNegation.Spells);

    private bool IsSpellCard(FieldSpellTrap field) => field.IsUnion || rules[field.Card.GameCardId].Category == CardCategory.Spell;

    /// <summary>Adds the stat changes of every active lasting card and of counters, in player and zone order.</summary>
    private void AddLastingModifiers(DuelState state, (MonsterBinding Binding, FieldMonster Monster)[] monsters, IReadOnlyList<MonsterBinding> negated, List<StatModifier> result)
    {
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (player, zone, field) in ActiveCards(state))
        {
            if (spellsNegated && IsSpellCard(field) || !definitions.TryGetValue(field.Card.GameCardId, out var definition)) continue;
            foreach (var lasting in definition.LastingModifiers)
            {
                int multiplier = LastingMultiplier(state, player, lasting.PerCount, lasting.CountFilter);
                foreach (var (target, monster) in monsters)
                    if (lasting.EquippedOnly ? target == field.EquippedTo
                        : (lasting.Side == TargetSide.Either || lasting.Side == TargetSide.Own == (target.Player == player)) &&
                          (lasting.Filter is null || MatchesFilter(monster.Card, lasting.Filter)))
                        result.Add(new(definition.EffectId, null, target, checked(lasting.Attack * multiplier), checked(lasting.Defense * multiplier),
                            SpellTrapSource: new(player, zone, field.Card, field.PlacementId)));
            }
        }
        foreach (var (target, monster) in monsters)
            if (monster.Counters > 0 && definitions.TryGetValue(monster.Card.GameCardId, out var definition) && definition.AttackPerCounter != 0 && !negated.Contains(target))
                result.Add(new(definition.EffectId, target, target, checked(definition.AttackPerCounter * monster.Counters), 0));
    }

    private int LastingMultiplier(DuelState state, PlayerId controller, LastingCount count, CardFilter? filter = null)
    {
        var player = state.GetPlayer(controller);
        return count switch
        {
            LastingCount.None => 1,
            LastingCount.OwnFaceUpMonsters => player.MonsterZones.Count(m => m is not null && m.Position != BattlePosition.FaceDownDefense &&
                (filter is null || MatchesFilter(m.Card, filter))),
            LastingCount.OwnSpellTraps => player.SpellTrapFields.Count(f => f is not null) + player.PendulumZones.Count(f => f is not null) + (player.FieldSpell is null ? 0 : 1),
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
    }

    private bool TargetPasses(DuelState state, FieldTarget spec, FieldCardTarget target) =>
        (spec.CardFilter is null || MatchesFilter(target.Card, spec.CardFilter)) &&
        (spec.MinXyzMaterials == 0 && spec.MaxXyzMaterials == int.MaxValue || target.Location == FieldLocation.MonsterZone &&
            state.GetPlayer(target.Player).MonsterZones[target.Zone] is { } holder &&
            holder.XyzMaterials.Count >= spec.MinXyzMaterials && holder.XyzMaterials.Count <= spec.MaxXyzMaterials) &&
        (!spec.NoUnionEquipped || target.Location != FieldLocation.MonsterZone || !HasUnionEquipped(state, new(target.Player, target.Zone, target.Card, target.PlacementId)));

    private static bool HasUnionEquipped(DuelState state, MonsterBinding monster) =>
        state.GetPlayer(monster.Player).SpellTrapFields.Any(f => f is { IsUnion: true } && f.EquippedTo == monster);

    private static DuelState UpdateSource(DuelState state, ChainLink link, Func<FieldSpellTrap, FieldSpellTrap> change)
    {
        var player = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        return state.With(player: player.WithSpellTrapAt(zone, change(player.SpellTrapAt(zone)!)));
    }

    private static DuelState AttachEquip(DuelState state, ChainLink link, FieldCardTarget target) =>
        UpdateSource(state, link, f => f with { EquippedTo = new(target.Player, target.Zone, target.Card, target.PlacementId) });

    /// <summary>Ties a monster just revived in that zone to its resolving source.</summary>
    private static DuelState AttachRevival(DuelState state, ChainLink link, int monsterZone, RevivalLink kind)
    {
        var monster = state.GetPlayer(link.Activation.Player).MonsterZones[monsterZone]!;
        MonsterBinding binding = new(link.Activation.Player, monsterZone, monster.Card, monster.PlacementId);
        return UpdateSource(state, link, f => kind == RevivalLink.Equip
            ? f with { EquippedTo = binding, Revival = kind } : f with { LinkedMonster = binding, Revival = kind });
    }

    /// <summary>A negated effect leaves an Equip Spell equipped; only its effects stop applying.</summary>
    private DuelState BindNegatedEquip(DuelState state, ChainLink link)
    {
        if (!IsLastingLink(link) || !LastingSourceInPlace(state, link)) return state;
        var definition = LinkDefinition(link);
        if (!definition.Steps.Any(s => s is EquipTargetStep)) return state;
        return QualifyingTargets(state, link, definition).FirstOrDefault() is { } target ? AttachEquip(state, link, target) : state;
    }

    /// <summary>Moves a resolving Union monster onto its target; skipped if it left or no Spell/Trap Zone is free.</summary>
    private DuelState EquipUnion(DuelState state, ChainLink link, FieldCardTarget target, List<DuelEvent> events)
    {
        var player = state.GetPlayer(link.Activation.Player);
        int zone = link.Activation.SpellTrapZone;
        int free = Array.IndexOf(player.SpellTrapFields.ToArray(), null);
        if (player.MonsterZones[zone] is not { Position: not BattlePosition.FaceDownDefense } source || source.Card != link.Activation.Spell ||
            source.PlacementId != link.SourcePlacementId || free < 0)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return state;
        }
        MonsterBinding binding = new(target.Player, target.Zone, target.Card, target.PlacementId);
        var monsters = player.MonsterZones.ToArray();
        monsters[zone] = null;
        var equip = new FieldSpellTrap(source.Card, true, checked(State.Revision + 1)) { EquippedTo = binding, IsUnion = true, EffectActive = true };
        events.Add(new UnionEquipped(player.Id, source.Card, free, binding));
        return state.With(player: player.With(monsters: monsters).WithSpellTrapAt(free, equip));
    }

    private DuelState ResolveUnequip(DuelState state, ChainLink link, List<DuelEvent> events)
    {
        var controller = link.Activation.Player;
        var player = state.GetPlayer(controller);
        int zone = link.Activation.SpellTrapZone;
        if (player.SpellTrapAt(zone) is not { IsUnion: true } field || field.Card != link.Activation.Spell || field.PlacementId != link.SourcePlacementId)
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SourceLeftField));
            return state;
        }
        int free = Array.IndexOf(player.MonsterZones.ToArray(), null);
        if (free < 0 || IsSummonLocked(state, controller))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.SummonUnavailable));
            return state;
        }
        return PlaceSpecialSummon(state.With(player: player.WithSpellTrapAt(zone, null)), controller, field.Card, free, BattlePosition.FaceUpAttack,
            DecisionCardSource.Hand, events, sourceAlreadyRemoved: true);
    }

    /// <summary>Places counters on a face-up card; counters cannot go on a face-down card.</summary>
    private static DuelState PlaceCounters(DuelState state, FieldCardTarget target, PlaceCounterStep step, List<DuelEvent> events)
    {
        var player = state.GetPlayer(target.Player);
        if (target.Location == FieldLocation.SpellTrapZone)
        {
            if (player.SpellTrapAt(target.Zone) is not { IsFaceUp: true } field) return state;
            int held = step.Protective ? field.ProtectionCounters : field.Counters;
            if (held >= step.Max) return state;
            int sum = Math.Min(step.Max, held + step.Count);
            events.Add(new SpellTrapCountersPlaced(player.Id, target.Zone, field.Card, sum - held, sum));
            return state.With(player: player.WithSpellTrapAt(target.Zone, step.Protective ? field with { ProtectionCounters = sum } : field with { Counters = sum }));
        }
        if (player.MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense } monster) return state;
        int current = step.Protective ? monster.ProtectionCounters : monster.Counters;
        if (current >= step.Max) return state;
        int total = Math.Min(step.Max, current + step.Count);
        var monsters = player.MonsterZones.ToArray();
        monsters[target.Zone] = step.Protective ? monster with { ProtectionCounters = total } : monster with { Counters = total };
        events.Add(new CountersPlaced(new(target.Player, target.Zone, target.Card, target.PlacementId), total - current, total));
        return state.With(player: player.With(monsters: monsters));
    }

    private bool? spellCounterGain;
    private bool HasSpellCounterGain => spellCounterGain ??= allEffects.Any(d => d.SpellCounterMax != 0);

    /// <summary>Each face-up card that collects Spell counters gains one when another card's Spell resolves.</summary>
    private DuelState AddSpellCounters(DuelState state, CardInstance resolved, List<DuelEvent> events)
    {
        if (!HasSpellCounterGain) return state;
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (player, zone, field) in ActiveCards(state).ToArray())
            if (field.Card != resolved && !(spellsNegated && IsSpellCard(field)) && definitions.TryGetValue(field.Card.GameCardId, out var definition) &&
                definition.SpellCounterMax > field.Counters)
                state = PlaceCounters(state, new(player, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId), new(1, definition.SpellCounterMax), events);
        foreach (var (binding, monster) in FieldMonsters(state).ToArray())
            if (monster.Position != BattlePosition.FaceDownDefense && !monster.BattleDestroyed && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                definitions.TryGetValue(monster.Card.GameCardId, out var definition) && definition.SpellCounterMax > monster.Counters)
                state = PlaceCounters(state, new(binding.Player, FieldLocation.MonsterZone, binding.Zone, binding.Card, binding.PlacementId), new(1, definition.SpellCounterMax), events);
        return state;
    }

    /// <summary>Places the source on top of its owner's Deck, provided it is still in their Graveyard.</summary>
    private DuelState ReturnSourceToDeckTop(DuelState state, ChainLink link, string effectId, List<DuelEvent> events)
    {
        var card = link.Activation.Spell;
        var owner = state.GetPlayer(card.Owner);
        if (!owner.Graveyard.Contains(card) || RuleLocked(state, LockedAction.BeReturnedToDeck, owner.Id, card, onField: false))
        {
            events.Add(new ChainLinkSkipped(link.Number, ChainSkipReason.TargetUnavailable));
            return state;
        }
        events.Add(new CardPlacedOnDeckTop(owner.Id, card, effectId));
        return state.With(player: owner.With(graveyard: owner.Graveyard.Where(c => c != card), deck: new[] { card }.Concat(owner.Deck)));
    }

    /// <summary>The state after a replacement took a card effect's destruction instead; null when none applies.</summary>
    private DuelState? ReplacedDestruction(DuelState state, FieldCardTarget target, List<DuelEvent> events)
    {
        if (target.Location == FieldLocation.MonsterZone)
            return ReplaceDestruction(state, new(target.Player, target.Zone, target.Card, target.PlacementId), DestructionCause.Effect, events);
        if (target.Location == FieldLocation.SpellTrapZone && PreventDestruction(state, LockedAction.BeDestroyedByEffect, target.Player, target.Card, null, events) is { } safe) return safe;
        if (target.Location == FieldLocation.SpellTrapZone && ProfileReplacement(state, target, DestructionCause.Effect, events) is { } swappedField) return swappedField;
        var player = state.GetPlayer(target.Player);
        if (target.Location != FieldLocation.SpellTrapZone || player.SpellTrapAt(target.Zone) is not { ProtectionCounters: > 0 } field) return null;
        events.Add(new SpellTrapDestructionReplaced(player.Id, target.Zone, field.Card));
        return state.With(player: player.WithSpellTrapAt(target.Zone, field with { ProtectionCounters = field.ProtectionCounters - 1 }));
    }

    private static IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> LinkedCards(DuelState state) =>
        state.Players.SelectMany(p => p.SpellTrapFields.Select((field, zone) => (Player: p.Id, Zone: zone, Field: field)))
            .Where(e => e.Field is not null && (e.Field.EquippedTo is not null || e.Field.LinkedMonster is not null))
            .Select(e => (e.Player, e.Zone, e.Field!));

    /// <summary>
    /// Applies the destructions that follow from this transition: Equip Cards whose monster is gone or face-down, both
    /// directions of a revival link, and Toons when Toon World is destroyed. Repeats until nothing more follows.
    /// </summary>
    private DuelState ApplyFieldLinks(DuelState before, DuelState next, List<DuelEvent> events)
    {
        if (!HasFieldLinks || next.Outcome is not null) return next;
        var known = LinkedCards(before).ToArray();
        for (int guard = 0; guard < 64; guard++)
        {
            var target = NextLinkedDestruction(LinkedCards(next).Concat(known).DistinctBy(e => (e.Field.Card.InstanceId, e.Field.PlacementId)), next, events);
            if (target is null) break;
            next = DestroyFieldCard(next, target, events, replaceable: false);
        }
        return next;
    }

    private FieldCardTarget? NextLinkedDestruction(IEnumerable<(PlayerId Player, int Zone, FieldSpellTrap Field)> linked, DuelState next, List<DuelEvent> events)
    {
        static FieldCardTarget MonsterTarget(MonsterBinding b) => new(b.Player, FieldLocation.MonsterZone, b.Zone, b.Card, b.PlacementId);
        foreach (var (player, zone, field) in linked)
        {
            FieldCardTarget source = new(player, FieldLocation.SpellTrapZone, zone, field.Card, field.PlacementId);
            if (source.IsStillPresent(next))
            {
                if (field.EquippedTo is { } equipped && (!equipped.IsStillPresent(next) ||
                        next.GetPlayer(equipped.Player).MonsterZones[equipped.Zone]!.Position == BattlePosition.FaceDownDefense))
                    return source;
                if (field.Revival == RevivalLink.Haunted && field.LinkedMonster is { } revived &&
                    events.OfType<MonsterDestroyed>().Any(e => e.Card == revived.Card && e.Zone == revived.Zone))
                    return source;
            }
            else
            {
                if (field.Revival == RevivalLink.Haunted && field.LinkedMonster is { } left && left.IsStillPresent(next)) return MonsterTarget(left);
                if (field.Revival == RevivalLink.Equip && field.EquippedTo is { } held && held.IsStillPresent(next) &&
                    events.OfType<SpellTrapDestroyed>().Any(e => e.Card == field.Card))
                    return MonsterTarget(held);
            }
        }
        if (events.OfType<SpellTrapDestroyed>().Any(e => rules.TryGetValue(e.Card.GameCardId, out var rule) && rule.IsToonWorld))
            foreach (var (binding, monster) in FieldMonsters(next))
                if (IsToonCard(monster.Card.GameCardId)) return MonsterTarget(binding);
        return null;
    }

    /// <summary>
    /// A protective counter, or the monster's Union equip, is lost instead of the monster, which is then not treated
    /// as destroyed. Null when nothing replaces this destruction.
    /// </summary>
    private DuelState? ReplaceDestruction(DuelState state, MonsterBinding monster, DestructionCause cause, List<DuelEvent> events, MonsterBinding? versus = null)
    {
        var player = state.GetPlayer(monster.Player);
        if (PreventDestruction(state, cause == DestructionCause.Battle ? LockedAction.BeDestroyedByBattle : LockedAction.BeDestroyedByEffect, monster.Player, monster.Card, player.MonsterZones[monster.Zone], events, versus) is { } kept) return kept;
        if (player.MonsterZones[monster.Zone] is { ProtectionCounters: > 0 } guarded)
        {
            var monsters = player.MonsterZones.ToArray();
            monsters[monster.Zone] = guarded with { ProtectionCounters = guarded.ProtectionCounters - 1 };
            events.Add(new DestructionReplaced(monster, monster.Card));
            return state.With(player: player.With(monsters: monsters));
        }
        if (ProfileReplacement(state, new(monster.Player, FieldLocation.MonsterZone, monster.Zone, monster.Card, monster.PlacementId), cause, events) is { } swapped) return swapped;
        if (IsSpellsNegated(state)) return null;
        for (int zone = 0; zone < 5; zone++)
            if (player.SpellTrapFields[zone] is { IsUnion: true, EffectActive: true } union && union.EquippedTo == monster &&
                definitions[union.Card.GameCardId].UnionReplaces.HasFlag(cause))
            {
                events.Add(new DestructionReplaced(monster, union.Card));
                return DestroyFieldCard(state, new(player.Id, FieldLocation.SpellTrapZone, zone, union.Card, union.PlacementId), events, replaceable: false);
            }
        return null;
    }

    private bool? quickMonsters;
    private bool HasQuickMonsters => quickMonsters ??= allEffects.Any(d => d.Kind is EffectKind.MonsterQuick or EffectKind.SpellTrapQuick);

    /// <summary>True when the player has a face-up monster whose quick effect could be activated in an open window now.</summary>
    private bool QuickMonsterReady(PlayerId playerId) => State.GetPlayer(playerId).MonsterZones.Any(m =>
        m is { Position: not BattlePosition.FaceDownDefense, BattleDestroyed: false } && effectsByCard.TryGetValue(m.Card.GameCardId, out var effects) &&
        effects.Any(definition => definition.Kind == EffectKind.MonsterQuick && definition.Origin == EffectOrigin.Field && !(definition.OncePerTurn && UsedThisTurn(m.Card, definition, m.PlacementId)) &&
            DefinitionActivationError(definition, ActivationContext.MonsterWindow, playerId, m.Card, null) == DuelError.None));

    /// <summary>Starts a monster effect in an open state, as a chain response, or in an open or battle window.</summary>
    private DuelActionResult BeginMonsterEffect(PlayerId playerId, int zone, PendingChain? chain, DuelState? from = null, bool window = false,
        IReadOnlyList<int>? detach = null, int effectIndex = 0)
    {
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        if (chain is null && !window && State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        var monster = State.GetPlayer(playerId).MonsterZones[zone];
        if (monster is null) return Reject(DuelError.MonsterZoneEmpty);
        if (monster.Position == BattlePosition.FaceDownDefense || monster.BattleDestroyed ||
            !EffectAt(monster.Card.GameCardId, effectIndex, out var definition) || !definition.IsMonsterEffect || definition.Origin != EffectOrigin.Field)
            return Reject(DuelError.UnsupportedMonsterEffect);
        // A Union's equip and unequip are one action per turn while it moves between zones on the field.
        long? placement = definition.Steps.Any(s => s is UnionEquipStep) ? null : monster.PlacementId;
        if (definition.OncePerTurn && UsedThisTurn(monster.Card, definition, placement)) return Reject(DuelError.EffectAlreadyUsed);
        var context = window ? ActivationContext.MonsterWindow : chain is null ? ActivationContext.MonsterField : ActivationContext.MonsterChainResponse;
        if (DefinitionActivationError(definition, context, playerId, monster.Card, null, chain?.Links[^1]) is not DuelError.None and var error)
            return Reject(error);
        if (definition.LocksOwnAttack && monster.LastAttackTurn == State.TurnNumber) return Reject(DuelError.ActivationConditionNotMet);
        CardInstance[] detached = [];
        if (definition.Cost is DetachCost detachCost)
        {
            var ids = detach ?? [];
            detached = monster.XyzMaterials.Where(c => ids.Contains(c.InstanceId)).ToArray();
            if (ids.Count != detachCost.Count || ids.Distinct().Count() != ids.Count || detached.Length != ids.Count) return Reject(DuelError.InvalidDecisionChoice);
        }
        else if (detach is { Count: > 0 }) return Reject(DuelError.InvalidDecisionChoice);
        var next = from ?? State;
        if (definition.OncePerTurn) next = RecordUse(next, monster.Card, definition, placement);
        DuelEvent[] head = [new MonsterEffectActivated(playerId, definition.EffectId, monster.Card, zone)];
        MonsterBinding binding = new(playerId, zone, monster.Card, monster.PlacementId);
        List<DuelEvent> paid = [.. head];
        if (definition.Cost is TributeSelfCost)
            next = SendFieldCardToGraveyard(RemovePlacement(next, new(playerId, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId)),
                monster.Card, new MonsterTributed(binding), paid);
        else if (definition.Cost is RemoveCounterCost remove)
        {
            var player = next.GetPlayer(playerId);
            var monsters = player.MonsterZones.ToArray();
            monsters[zone] = monster with { Counters = monster.Counters - remove.Count };
            next = next.With(player: player.With(monsters: monsters));
            paid.Add(new CountersRemoved(binding, remove.Count, monster.Counters - remove.Count));
        }
        else if (definition.Cost is DetachCost)
        {
            var player = next.GetPlayer(playerId);
            var monsters = player.MonsterZones.ToArray();
            monsters[zone] = monster with { XyzMaterials = Array.AsReadOnly(monster.XyzMaterials.Except(detached).ToArray()) };
            next = SendXyzMaterials(next.With(player: player.With(monsters: monsters)), monster.Card, detached, true, paid);
        }
        if (definition.LocksOwnAttack)
        {
            var player = next.GetPlayer(playerId);
            if (player.MonsterZones[zone] is { } current && current.Card == monster.Card)
            {
                var monsters = player.MonsterZones.ToArray();
                monsters[zone] = current with { AttackLockedTurn = State.TurnNumber };
                next = next.With(player: player.With(monsters: monsters));
            }
        }
        (next, head) = PayLifePointCost(next, playerId, definition, [.. paid]);
        return ContinueCommit(next, head, playerId, monster.Card, zone, definition, monster.PlacementId, chain);
    }

    /// <summary>A plain once-per-turn limit belongs to this copy where it is now; a card that left the field and returned is a new card.</summary>
    private bool UsedThisTurn(CardInstance card, EffectDefinition effect, long? placement = null) =>
        State.Bookkeeping.ActivatedUses.Any(u => u.InstanceId == card.InstanceId && u.TurnNumber == State.TurnNumber && u.Key == effect.OncePerTurnKey && u.PlacementId == placement);

    private DuelState RecordUse(DuelState state, CardInstance card, EffectDefinition effect, long? placement = null) => state.WithBookkeeping(state.Bookkeeping with
    {
        ActivatedUses = Array.AsReadOnly(state.Bookkeeping.ActivatedUses.Where(u => u.TurnNumber == State.TurnNumber).Append(new(card.InstanceId, State.TurnNumber, effect.OncePerTurnKey, placement)).ToArray())
    });

    private DuelActionResult ExecuteUnequip(PlayerId playerId, int zone)
    {
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        if (zone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(playerId);
        if (player.SpellTrapFields[zone] is not { IsUnion: true } field || !definitions.TryGetValue(field.Card.GameCardId, out var definition))
            return Reject(DuelError.UnsupportedMonsterEffect);
        if (UsedThisTurn(field.Card, definition)) return Reject(DuelError.EffectAlreadyUsed);
        if (player.MonsterZones.All(m => m is not null)) return Reject(DuelError.DecisionUnavailable);
        if (IsSummonLocked(State, playerId)) return Reject(DuelError.SummonLocked);
        var receipt = new CommittedActivation(playerId, definition.EffectId, field.Card, zone, null, null);
        var link = new ChainLink(1, receipt, field.PlacementId, ChainOperation.UnionUnequip, ChainSpellSpeed.One);
        var chain = new PendingChain(checked(State.Revision + 1), [link], Other(playerId));
        return Accept(RecordUse(State, field.Card, definition).WithCommittedActivation(receipt).WithChain(chain),
            new MonsterEffectActivated(playerId, definition.EffectId, field.Card, zone), new ActivationCommitted(receipt),
            new ChainLinkAdded(link), new ChainResponseRequested(chain.Responder, chain.DecisionId));
    }

    private bool UpkeepDue(PlayerId playerId, int zone, FieldSpellTrap field) =>
        field.EffectActive && field.IsFaceUp && field.UpkeepSettledTurn != State.TurnNumber && playerId == State.ActivePlayer &&
        definitions.TryGetValue(field.Card.GameCardId, out var definition) && definition.UpkeepLifePoints > 0 && definition.UpkeepPhase == State.Phase && zone < 5;

    private bool AnyUpkeepDue(PlayerId playerId)
    {
        var player = State.GetPlayer(playerId);
        return Enumerable.Range(0, 5).Any(zone => player.SpellTrapFields[zone] is { } field && UpkeepDue(playerId, zone, field));
    }

    /// <summary>A maintenance cost is not an effect: it uses no chain, cannot be negated and is still owed while the card is negated.</summary>
    private DuelActionResult ExecuteUpkeep(ResolveUpkeep command)
    {
        if (State.Phase is not (DuelPhase.Standby or DuelPhase.End)) return Reject(DuelError.WrongPhase);
        if (command.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
        var player = State.GetPlayer(command.Player);
        if (player.SpellTrapFields[command.SpellTrapZone] is not { } field || !UpkeepDue(player.Id, command.SpellTrapZone, field))
            return Reject(DuelError.NoUpkeepDue);
        var definition = definitions[field.Card.GameCardId];
        if (!command.Pay)
        {
            List<DuelEvent> events = [new UpkeepSettled(player.Id, field.Card, false)];
            var destroyed = DestroyFieldCard(State, new(player.Id, FieldLocation.SpellTrapZone, command.SpellTrapZone, field.Card, field.PlacementId), events, replaceable: false);
            return Accept(destroyed, events.ToArray());
        }
        int amount = definition.UpkeepLifePoints;
        if (player.LifePoints < amount) return Reject(DuelError.InsufficientLifePoints);
        int left = player.LifePoints - amount;
        var paid = State.With(player: player.With(lifePoints: left).WithSpellTrapAt(command.SpellTrapZone, field with { UpkeepSettledTurn = State.TurnNumber }));
        DuelEvent[] settled = [new LifePointsPaid(player.Id, amount, player.LifePoints, left, definition.EffectId), new UpkeepSettled(player.Id, field.Card, true)];
        if (left != 0) return Accept(paid, settled);
        var outcome = new DuelOutcome(Other(player.Id), player.Id, DuelEndReason.LifePointsDepleted);
        return Accept(paid.With(outcome: outcome), [.. settled, new DuelEnded(outcome)]);
    }

    private DuelActionResult ExecuteToonSummon(ToonSummon command)
    {
        if (command.ExpectedRevision != State.Revision) return Reject(DuelError.StaleSummonSelection);
        if (State.Phase is not (DuelPhase.Main1 or DuelPhase.Main2)) return Reject(DuelError.WrongPhase);
        var player = State.GetPlayer(command.Player);
        var card = player.Hand.SingleOrDefault(c => c.InstanceId == command.CardInstanceId);
        if (card is null) return Reject(DuelError.CardNotInHand);
        if (!IsToonCard(card.GameCardId)) return Reject(DuelError.UnsupportedSpecialSummon);
        if (!player.SpellTrapFields.Any(f => f is { IsFaceUp: true, EffectActive: true } && rules[f.Card.GameCardId].IsToonWorld))
            return Reject(DuelError.ToonSummonUnavailable);
        uint level = catalog[card.GameCardId].Statistics.Level;
        int required = level <= 4 ? 0 : level <= 6 ? 1 : 2;
        var zones = command.TributeZones;
        if (zones.Count != required || zones.Distinct().Count() != zones.Count || zones.Any(z => z is < 0 or >= 5 || player.MonsterZones[z] is not { } m ||
                !rules.TryGetValue(m.Card.GameCardId, out var tribute) || !tribute.AllowsTributeForSummon || RuleLocked(State, LockedAction.BeTributed, player.Id, m.Card, m)))
            return Reject(DuelError.InvalidTributeSelection);
        var after = State;
        List<DuelEvent> events = [];
        foreach (int zone in zones)
        {
            var monster = player.MonsterZones[zone]!;
            after = SendFieldCardToGraveyard(RemovePlacement(after, new(player.Id, FieldLocation.MonsterZone, zone, monster.Card, monster.PlacementId)), monster.Card,
                new MonsterTributed(new(player.Id, zone, monster.Card, monster.PlacementId)), events);
        }
        if (CheckSpecialSummon(after, player.Id, card, command.MonsterZone, command.Position, DecisionCardSource.Hand, ToonProcedureId) is not DuelError.None and var error)
            return Reject(error);
        var pending = new PendingSummon(checked(State.Revision + 1), checked(State.Revision + 1), player.Id, card, command.MonsterZone, Other(player.Id),
            IsSpecialSummon: true, Source: DecisionCardSource.Hand, Position: command.Position, ProcedureId: ToonProcedureId, ProperProcedureId: ToonProcedureId);
        var attempted = after.With(player: RemoveSpecialSource(after.GetPlayer(player.Id), card, DecisionCardSource.Hand)).WithSummon(pending);
        if (OpensSummonWindow(attempted, card.GameCardId))
            return Accept(attempted, [.. events, new SummonAttempted(pending), new SummonResponseRequested(pending)]);
        var next = PlaceSpecialSummon(after, player.Id, card, command.MonsterZone, command.Position, DecisionCardSource.Hand, events,
            ToonProcedureId, properProcedureId: ToonProcedureId);
        return Accept(next, events.ToArray());
    }

    /// <summary>Attack limits from a monster's own effect, then a Toon's rules including its LP cost.</summary>
    private DuelError AttackRestrictionError(DuelPlayerState player, DuelPlayerState opponent, FieldMonster attacker, int? targetZone, bool payAttackCost = true)
    {
        if (attacker.AttackLockedTurn == State.TurnNumber) return DuelError.AttackLocked;
        if (RuleLocked(State, LockedAction.Attack, player.Id, attacker.Card, attacker)) return DuelError.LockedByEffect;
        if (targetZone is int defending && opponent.MonsterZones[defending] is { } defender && RuleLocked(State, LockedAction.BeAttacked, opponent.Id, defender.Card, defender)) return DuelError.LockedByEffect;
        if (!IsToonCard(attacker.Card.GameCardId)) return DuelError.None;
        if (attacker.SummonedOnTurn == State.TurnNumber) return DuelError.ToonCannotAttack;
        if (payAttackCost && player.LifePoints <= ToonAttackCost) return DuelError.InsufficientLifePoints;
        return OpponentHasToon(opponent) && (targetZone is not int zone || opponent.MonsterZones[zone] is not { } target || !IsToonCard(target.Card.GameCardId))
            ? DuelError.ToonMustAttackToon : DuelError.None;
    }

    private bool OpponentHasToon(DuelPlayerState opponent) =>
        opponent.MonsterZones.Any(m => m is not null && m.Position != BattlePosition.FaceDownDefense && IsToonCard(m.Card.GameCardId));

    private bool CanAttackDirectly(DuelPlayerState opponent, FieldMonster attacker, DuelState? state = null)
    {
        state ??= State;
        return opponent.MonsterZones.All(m => m is null || RuleLocked(state, LockedAction.BeAttacked, opponent.Id, m.Card, m)) ||
            GetBattleAbilities(state, Other(opponent.Id), attacker).DirectAttack || IsToonCard(attacker.Card.GameCardId) && !OpponentHasToon(opponent);
    }

    private DuelPlayerState PayToonAttack(DuelPlayerState player, FieldMonster attacker, List<DuelEvent> events)
    {
        if (!IsToonCard(attacker.Card.GameCardId)) return player;
        events.Add(new LifePointsPaid(player.Id, ToonAttackCost, player.LifePoints, player.LifePoints - ToonAttackCost, "toon-attack"));
        return player.With(lifePoints: player.LifePoints - ToonAttackCost);
    }

    private DuelState ReplaceFieldSpell(DuelState state, PlayerId playerId, FieldSpellTrap old, List<DuelEvent> events)
    {
        var player = state.GetPlayer(playerId);
        return SendFieldCardToGraveyard(state.With(player: player.WithSpellTrapAt(DuelPlayerState.FieldZoneIndex, null)), old.Card,
            new FieldSpellReplaced(playerId, old.Card), events);
    }
}
