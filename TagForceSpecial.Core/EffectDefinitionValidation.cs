namespace TagForceSpecial.Core;

/// <summary>Checks an effect definition's parts against the operations Core supports and returns their normalized copies.</summary>
internal static class EffectDefinitionValidation
{
    /// <summary>Throws on the first unsupported part, in the definition constructor's order; returns the steps, lasting modifiers, conditions and field targets.</summary>
    internal static (EffectStep[] Steps, LastingStatModifier[] Lasting, EffectCondition[] Conditions, EffectTarget[] Fields) Validate(
        string effectId, uint cardId, EffectKind kind, IEnumerable<EffectStep> steps, IEnumerable<EffectCondition>? conditions, EffectCost? cost,
        EffectTarget? target, bool locksOtherSummonsThisTurn, IEnumerable<EffectTarget>? fieldTargets, IEnumerable<LastingStatModifier>? lastingModifiers,
        LastingNegation negation, int upkeepLifePoints, bool oncePerTurn, int attackPerCounter, bool isMarker, DuelPhase upkeepPhase,
        DestructionCause unionReplaces, int spellCounterMax, bool locksOwnAttack, string? oncePerTurnKey, EffectOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(steps);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var snapshot = steps.Take(33).ToArray();
        var lasting = (lastingModifiers ?? []).Take(9).ToArray();
        bool lastingKind = kind is EffectKind.ContinuousSpell or EffectKind.ContinuousTrap or EffectKind.FieldSpell or EffectKind.EquipSpell;
        bool monsterKind = kind is EffectKind.MonsterIgnition or EffectKind.MonsterQuick;
        bool unionEquip = snapshot.Any(s => s is UnionEquipStep);
        if (lasting.Length > 8 || lasting.Any(m => m is null) || lasting.Distinct().Count() != lasting.Length ||
            lasting.Length != 0 && !lastingKind && !unionEquip)
            throw new ArgumentException("Only lasting cards and Union equips take up to 8 distinct lasting modifiers.", nameof(lastingModifiers));
        if (!Enum.IsDefined(negation) || upkeepLifePoints < 0 || attackPerCounter < 0 ||
            (negation != LastingNegation.None || upkeepLifePoints != 0) && !lastingKind ||
            oncePerTurn && !monsterKind && kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) || oncePerTurnKey is not null && (!oncePerTurn || oncePerTurnKey.Length == 0) ||
            (attackPerCounter != 0 || locksOwnAttack) && !monsterKind)
            throw new ArgumentException("Negation and upkeep belong to lasting cards; once-per-turn and counter stats to activated monster effects.", nameof(negation));
        if (upkeepPhase is not (DuelPhase.Standby or DuelPhase.End) || unionReplaces is not (DestructionCause.Battle or DestructionCause.Any) ||
            spellCounterMax < 0 || spellCounterMax != 0 && !lastingKind && !monsterKind)
            throw new ArgumentException("Upkeep runs in the Standby or End Phase; a Union replaces battle or any destruction; Spell counters need a card that stays.", nameof(upkeepPhase));
        // A marker has no effect of its own: other cards' rules read it on the field.
        if (snapshot.Length > 32 || snapshot.Length == 0 && lasting.Length == 0 && negation == LastingNegation.None && !(isMarker && lastingKind) ||
            isMarker && (snapshot.Length != 0 || lasting.Length != 0 || negation != LastingNegation.None))
            throw new ArgumentException("A definition needs 1–32 resolution steps, or a lasting effect instead.", nameof(steps));
        foreach (var step in snapshot)
            if (IsInvalidStep(step, kind))
                throw new ArgumentException("Steps must be supported Core operations with positive amounts.", nameof(steps));
        // A step that reads a die roll or coin toss must follow one in the same effect, or it would read an earlier effect's result.
        int filteredDiscard = Array.FindIndex(snapshot, s => s is DiscardCardsStep { Filter: not null });
        if (filteredDiscard >= 0 && (cost is not null || snapshot.Count(s => s is DiscardCardsStep) != 1 ||
            snapshot.Take(filteredDiscard).Any(s => s is DrawCardsStep or AddFromDeckToHandStep or AddFromGraveyardToHandStep or AddFromDeckOrGraveyardStep)))
            throw new ArgumentException("Filtered discards require an unmodified hand projection and no activation cost.", nameof(steps));
        int roll = Array.FindIndex(snapshot, s => s is RollDieStep), toss = Array.FindIndex(snapshot, s => s is TossCoinsStep),
            mill = Array.FindIndex(snapshot, s => s is MillTopOfDeckStep), dig = Array.FindIndex(snapshot, s => s is ExcavateStep);
        static EffectCount CountOf(EffectStep s) => s switch
        {
            GainLifePointsStep g => g.PerCount, DealDamageStep d => d.PerCount, ModifyAllUntilEndStep m => m.PerCount, _ => EffectCount.None
        };
        for (int i = 0; i < snapshot.Length; i++)
            if ((snapshot[i] is StopUnlessRollStep || CountOf(snapshot[i]) == EffectCount.DieRoll) && (roll < 0 || roll > i) ||
                (snapshot[i] is StopUnlessHeadsStep || CountOf(snapshot[i]) == EffectCount.Heads) && (toss < 0 || toss > i) ||
                (snapshot[i] is StopUnlessMilledStep || CountOf(snapshot[i]) == EffectCount.MilledLevel) && (mill < 0 || mill > i) ||
                snapshot[i] is TakeExcavatedStep or SendExcavatedToGraveyardStep && (dig < 0 || dig > i))
                throw new ArgumentException("A die or coin result is read only after the effect rolls or tosses.", nameof(steps));
        var required = (conditions ?? []).Take(9).ToArray();
        if (required.Length > 8 || required.Any(c => c is null) || required.Distinct().Count() != required.Length)
            throw new ArgumentException("Supply up to 8 distinct conditions.", nameof(conditions));
        if (required.OfType<MonsterSummonedCondition>().Any(c => c.Kinds is <= 0 or > SummonKinds.Any || !Enum.IsDefined(c.Stat) || c.Min < 0 || c.Min > c.Max))
            throw new ArgumentException("A summon condition needs summon kinds and a valid stat range.", nameof(conditions));
        if (snapshot.Any(s => s is RedirectRespondedDamageStep) != required.Any(c => c is RespondsToDamageCondition) ||
            required.Any(c => c is RespondsToDamageCondition) && kind is not (EffectKind.QuickPlaySpell or EffectKind.NormalTrap or EffectKind.CounterTrap) ||
            required.Any(c => c is DirectAttackCondition) && !required.Any(c => c is OpponentAttackDeclaredCondition) ||
            required.Any(c => c is TributeCountCondition or XyzSummonedCondition) && !required.Any(c => c is MonsterSummonedCondition) ||
            required.OfType<TributeCountCondition>().Any(c => c.Tributes is < 1 or > 3))
            throw new ArgumentException("How a monster was Summoned narrows a summon condition.", nameof(conditions));
        if (required.OfType<BattleTimingCondition>().Any(c => c.Windows == BattleTiming.None || (c.Windows & ~BattleTiming.All) != 0 ||
                c.Windows.HasFlag(BattleTiming.Calculation) && !c.DuringCalculation))
            throw new ArgumentException("A battle timing condition needs supported timings; during calculation needs an explicit exception.", nameof(conditions));
        if (cost is not (null or DiscardCost or PayLifePointsCost or TributeCost or PayHalfLifePointsCost or TributeSelfCost or RemoveCounterCost or BanishFromGraveyardCost or DetachCost or SendSelfCost or RevealSelfCost) ||
            cost is TributeSelfCost or RemoveCounterCost or DetachCost && !monsterKind ||
            cost is DiscardCost { Count: < 1 or > 5 } or TributeCost { Count: < 1 or > 5 }) throw new ArgumentOutOfRangeException(nameof(cost));
        if (!Enum.IsDefined(origin) || origin is EffectOrigin.Hand or EffectOrigin.Graveyard && !monsterKind && kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) &&
                !(kind == EffectKind.MonsterTrigger && origin == EffectOrigin.Hand) ||
            origin == EffectOrigin.PendulumZone && (kind is not (EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick or EffectKind.ContinuousSpell) ||
                cost is SendSelfCost or RevealSelfCost) ||
            cost is SendSelfCost self && (!Enum.IsDefined(self.Destination) || origin == EffectOrigin.Field || origin == EffectOrigin.Hand && self.Destination == SelfDestination.Banish ||
                origin == EffectOrigin.Graveyard && self.Destination != SelfDestination.Banish) || cost is RevealSelfCost && origin != EffectOrigin.Hand ||
            snapshot.Any(s => s is SpecialSummonSelfStep) && origin == EffectOrigin.Field)
            throw new ArgumentException("Hand and Graveyard effects belong to activated effects, with a self-cost that matches where the card is.", nameof(origin));
        if (required.OfType<MonsterWouldBeSummonedCondition>().Any(c => c.Kinds is <= 0 or > SummonKinds.Any) ||
            snapshot.Any(s => s is NegateSummonStep) != required.Any(c => c is MonsterWouldBeSummonedCondition) || snapshot.Any(s => s is NegateSummonStep) && kind != EffectKind.CounterTrap)
            throw new ArgumentException("Summon negation needs a Counter Trap with a would-be-Summoned condition.", nameof(steps));
        if (target is not (null or SpellTrapOnFieldTarget)) throw new ArgumentOutOfRangeException(nameof(target));
        bool responds = required.Any(c => c is RespondsToSpellActivationCondition or RespondsToActivationCondition);
        if (required.OfType<RespondsToActivationCondition>().Any(c => c.Category is not (null or CardCategory.Monster or CardCategory.Spell or CardCategory.Trap)))
            throw new ArgumentOutOfRangeException(nameof(conditions));
        if (responds && kind is EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell or EffectKind.SpellTrapIgnition)
            throw new ArgumentException("A Spell Speed 1 Spell cannot be a chain response.", nameof(conditions));
        if (snapshot.Any(s => s is NegateActivationStep or NegateEffectStep) && !responds)
            throw new ArgumentException("Negating an activation requires a response condition.", nameof(steps));
        if ((target is not null) != snapshot.Any(s => s is DestroyTargetStep))
            throw new ArgumentException("A Spell/Trap target and a target-destruction step go together.", nameof(target));
        var fields = (fieldTargets ?? []).Take(6).ToArray();
        bool fieldSteps = snapshot.Any(EffectDefinition.IsFieldTargetStep), zoneSteps = snapshot.Any(EffectDefinition.IsZoneTargetStep);
        if (fields.Any(f => f is not (FieldTarget or ZoneTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster or TriggerSourceMonster)) ||
            fields.Any(f => f is SummonedMonsterTarget) && (fields.Length != 1 || !required.Any(c => c is MonsterSummonedCondition)) ||
            fields.Any(f => f is AttackingMonsterTarget or DefendingMonsterTarget) && (fields.Length != 1 ||
                !required.Any(c => c is BattleTimingCondition || c is OpponentAttackDeclaredCondition && fields[0] is AttackingMonsterTarget)) ||
            snapshot.Any(s => s is ModifyCombatantStatsStep) && fields is not [AttackingMonsterTarget or DefendingMonsterTarget] ||
            snapshot.Any(s => s is NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep) && fields is not [AttackingMonsterTarget] || fields.Sum(EffectDefinition.MaxCount) > 5 || fields.Length != 0 && target is not null ||
            (fields.Length != 0) != (fieldSteps || zoneSteps) || fieldSteps && zoneSteps ||
            fieldSteps && fields.Any(f => f is not (FieldTarget or AttackingMonsterTarget or DefendingMonsterTarget or SummonedMonsterTarget or TriggerTargetedMonster or TriggerSourceMonster)) || zoneSteps && fields.Any(f => f is not ZoneTarget) ||
            fields.SkipLast(1).Any(f => EffectDefinition.MinCount(f) != EffectDefinition.MaxCount(f)) ||
            snapshot.Any(s => s is ChangeTargetsToDefenseStep) && fields.Any(f => f is FieldTarget { Kind: not FieldCardKind.Monster }) ||
            snapshot.Any(s => s is SpecialSummonTargetStep) && (fields.Length != 1 || fields[0] is not ZoneTarget { MaxCount: 1, MustBeSummonable: true, Zone: CardZone.Graveyard }))
            throw new ArgumentException("Field targets (up to 5) and the steps that use them go together.", nameof(fieldTargets));
        bool revivalEquip = snapshot.Any(s => s is SpecialSummonTargetStep { Link: RevivalLink.Equip });
        bool attaches = snapshot.Any(s => s is EquipTargetStep);
        if (kind == EffectKind.EquipSpell && attaches == revivalEquip || attaches && kind != EffectKind.EquipSpell || revivalEquip && kind != EffectKind.EquipSpell ||
            attaches && fields is not [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Filter: FieldCardFilter.FaceUp }] ||
            snapshot.Any(s => s is SpecialSummonTargetStep { Link: RevivalLink.Haunted }) && kind != EffectKind.ContinuousTrap ||
            unionEquip && (kind != EffectKind.MonsterIgnition || !oncePerTurn || fields is not [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own, Filter: FieldCardFilter.FaceUp, NoUnionEquipped: true }]) ||
            snapshot.Any(s => s is PlaceCounterStep) && fields is not ([TriggerSourceMonster] or [FieldTarget { Count: 1 }]) ||
            (snapshot.Any(s => s is AttachSourceStep or SpecialSummonTargetStep { Link: RevivalLink.Material }) &&
                kind is not (EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap)) ||
            snapshot.Any(s => s is AttachSourceStep or DetachAllStep or RankUpStep or AttachFromStep { ToTarget: true }) &&
                (fields.Length == 0 || fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster, Count: 1 }) ||
                 snapshot.Any(s => s is AttachSourceStep a && a.Target >= fields.Length || s is DetachAllStep d && d.Target >= fields.Length)) ||
            snapshot.Any(s => s is RankUpStep) && (fields is not [FieldTarget { Side: TargetSide.Own }] ||
                kind is not (EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.NormalTrap)) ||
            snapshot.Any(s => s is TakeOpponentMaterialStep) && !snapshot.Any(s => s is RankUpStep) ||
            snapshot.Any(s => s is AttachFromStep { ToTarget: false }) && (!monsterKind || fields.Length != 0) ||
            snapshot.Any(s => s is AttachTargetsToSelfStep) && (!monsterKind || fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster })))
            throw new ArgumentException("Equip, revival, Union and counter steps need their card kinds and bound targets.", nameof(steps));
        if (snapshot.OfType<DestroyTargetAtStep>().Any(s => s.Target >= fields.Length) ||
            snapshot.OfType<GiveTargetControlStep>().Any(s => s.Target >= fields.Length || fields[s.Target] is not FieldTarget { Kind: FieldCardKind.Monster, Side: TargetSide.Own, Count: 1 }) ||
            snapshot.Any(s => s is DestroyTargetAtStep or GiveTargetControlStep) && fields.Any(f => f is FieldTarget { Count: not 1 }))
            throw new ArgumentException("A step on one target needs a single target at that place; giving control needs your own monster.", nameof(steps));
        if (fields.Any(f => f is FieldTarget { CardFilter: SharedTraitFilter } || f is ZoneTarget { Filter: SharedTraitFilter } z && (fields.Length != 1 || z.MinCount != z.MaxCount || z.MinCount < 2)))
            throw new ArgumentException("A shared-trait filter belongs to a single Graveyard or banished target slot of a fixed count.", nameof(fieldTargets));
        // Taking a monster the opponent just Summoned binds it automatically.
        bool takesSummoned = snapshot.Any(s => s is TakeControlStep) && fields is [SummonedMonsterTarget] && required.OfType<MonsterSummonedCondition>().All(c => c.ByOpponent);
        if (snapshot.Any(s => s is TakeControlStep or GiveControlStep) && !takesSummoned && fields.Any(f => f is not FieldTarget { Kind: FieldCardKind.Monster }) ||
            snapshot.Any(s => s is TakeControlStep) && fields.Any(f => f is FieldTarget { Side: not TargetSide.Opponent }) ||
            snapshot.Any(s => s is GiveControlStep) && fields.Any(f => f is FieldTarget { Side: not TargetSide.Own }) ||
            snapshot.Any(s => s is SwitchControlStep) && fields is not ([FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Opponent },
                FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own }] or [FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Own },
                FieldTarget { Kind: FieldCardKind.Monster, Count: 1, Side: TargetSide.Opponent }]) ||
            snapshot.Any(s => s is TakeControlStep { Duration: ControlDuration.WhileEquipped } or GiveControlStep { Duration: ControlDuration.WhileEquipped }) &&
                kind != EffectKind.EquipSpell ||
            snapshot.Any(s => s is TakeControlStep { Duration: ControlDuration.WhileSourceFaceUp } or GiveControlStep { Duration: ControlDuration.WhileSourceFaceUp }
                or TakeChosenControlStep { Duration: ControlDuration.WhileSourceFaceUp }) && !monsterKind && kind != EffectKind.MonsterTrigger)
            throw new ArgumentException("Control steps need monster targets on the side they take from or give, one per side to switch.", nameof(steps));
        if (kind == EffectKind.MonsterTrigger && (required.Length != 0 || cost is not null || target is not null || fields is not ([] or [TriggerTargetedMonster] or [TriggerSourceMonster] or [ZoneTarget { MinCount: 1, MaxCount: 1 }] or [FieldTarget { Count: 1, ForTribute: false }]) || locksOtherSummonsThisTurn) ||
            kind != EffectKind.MonsterTrigger && fields.Any(f => f is TriggerTargetedMonster or TriggerSourceMonster))
            throw new ArgumentException("A monster trigger takes its timing and costs from its trigger profile.", nameof(kind));
        return (snapshot, lasting, required, fields);
    }

    /// <summary>A count filter needs a count that enumerates cards.</summary>
    private static bool Filterable(EffectCount count, CardFilter? filter) => filter is null || count is EffectCount.OpponentMonsters or EffectCount.AllMonsters or
        EffectCount.OpponentHand or EffectCount.OpponentGraveyard or EffectCount.OpponentBanished or EffectCount.OwnMonsters or EffectCount.OwnFaceUpMonsters;

    private static bool IsInvalidStep(EffectStep step, EffectKind kind) => step switch
    {
        GainLifePointsStep s => s.Amount <= 0 || !Enum.IsDefined(s.Players) || !Enum.IsDefined(s.PerCount) || !Filterable(s.PerCount, s.CountFilter),
        DealDamageStep s => s.Amount <= 0 || !Enum.IsDefined(s.PerCount) || !Filterable(s.PerCount, s.CountFilter),
        DamageBothPlayersStep s => s.ToOpponent <= 0 || s.ToSelf <= 0,
        TakeDamageStep s => s.Amount <= 0,
        BanishSourceWhenItLeavesStep => false,
        ModifyTargetsStep s => s.Attack == 0 && s.Defense == 0,
        WeakenEventMonsterStep s => s.Attack <= 0 || kind != EffectKind.MonsterTrigger,
        PayDownToGainStep s => s.Remaining < 1 || kind != EffectKind.MonsterTrigger,
        SetDeclaredLevelStep s => s.Filter is null || s.MinLevel < 1 || s.MaxLevel > 12 || s.MinLevel > s.MaxLevel,
        AddFromDeckOrGraveyardStep s => s.Filter is null,
        AddFromGraveyardToHandStep s => s.Filter is null,
        AddFromFaceUpExtraDeckStep s => s.Filter is null,
        ReduceSourceScaleStep s => s.By < 1 || s.Minimum < 0 || kind != EffectKind.MonsterTrigger,
        DestroyAboveSourceScaleStep s => s.Except is null || kind != EffectKind.MonsterTrigger,
        BanishEventMonsterStep or BanishSourceCardStep => kind != EffectKind.MonsterTrigger,
        RedirectRespondedDamageStep => false,
        DiscardRandomStep s => s.Player is not (TargetSide.Own or TargetSide.Opponent),
        // Validated by their constructors; the engine checks Token rules and the seed.
        SummonTokensStep or ShuffleDeckStep or SpecialSummonStep => false,
        DrawCardsStep s => s.Count is < 1 or > 10 || !Enum.IsDefined(s.Players),
        DiscardCardsStep s => s.Count is < 1 or > 5 || !Enum.IsDefined(s.Players) || s.Minimum is { } min && (min < 1 || min > s.Count || s.Players != EffectPlayers.Self),
        MillTopOfDeckStep s => s.Count is < 1 or > 60,
        AddFromDeckToHandStep s => s.Filter is null,
        SendFromDeckToGraveyardStep s => s.Filter is null,
        DestroyTargetStep or DestroyOpponentFaceUpMonstersStep or NegateActivationStep or NegateEffectStep or NegateSummonStep => false,
        DestroyTargetsStep or ReturnTargetsToHandStep or ChangeTargetsToDefenseStep => false,
        AddTargetsToHandStep or BanishTargetsStep or ReturnTargetsToGraveyardStep or ShuffleTargetsIntoDeckStep => false,
        SpecialSummonTargetStep s => s.Position is not (null or BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense) || !Enum.IsDefined(s.Link),
        EquipTargetStep or UnionEquipStep or PlaceCounterStep or FlipTargetsFaceDownStep or GrantNormalSummonStep or StopUnlessNoOwnMonstersStep => false,
        SpecialSummonSelfStep s => s.Position is not (null or BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense),
        ApplyRuleLockStep s => s.Lock.Actions == LockedAction.None || s.Lock.Scope != LockScope.Side || s.Lock.UsesPerTurn != 0,
        ModifyTargetsUntilEndStep s => s.Attack == 0 && s.Defense == 0,
        FlipTargetsFaceUpStep s => s.Position is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense),
        ReturnSourceToDeckTopStep => kind != EffectKind.MonsterTrigger,
        BanishFieldTargetsStep or NegateAttackStep or DamageByTargetAttackStep or GainByTargetAttackStep or DestroyAndBanishTargetsStep => false,
        AttachSourceStep or AttachTargetsToSelfStep or DetachAllStep or TakeOpponentMaterialStep => false,
        AttachFromStep s => s.Filter is null || !s.Hand && !s.Graveyard,
        RankUpStep s => s.Into is null || s.RankChange is 0 or < -12 or > 12,
        ApplyGraveyardRedirectStep s => s.Rule is null || !Enum.IsDefined(s.Rule.To) || !Enum.IsDefined(s.Rule.Owner),
        RollDieStep or LookAtOpponentHandStep => false,
        TossCoinsStep s => s.Count is < 1 or > 5,
        StopUnlessRollStep s => s.Values.Count == 0 || s.Values.Any(v => v is < 1 or > 6),
        StopUnlessHeadsStep s => s.Minimum < 1,
        ModifyAllUntilEndStep s => s.Attack == 0 && s.Defense == 0 || !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.PerCount),
        ChooseFromOpponentHandStep s => !Enum.IsDefined(s.To),
        EachPlayerTossesCoinStep s => s.HeadsGain < 0 || s.TailsDamage < 0 || s.HeadsGain == 0 && s.TailsDamage == 0,
        DiceDuelStep s => s.PerPip < 1 || s.OnSix < 1,
        OpponentCallsRandomHandCardStep or ReturnAttackerToHandStep => false,
        StopUnlessHandHoldsStep s => s.Filter is null || !Enum.IsDefined(s.Side),
        ExcavateStep s => s.Count is < 1 or > 10,
        TakeExcavatedStep or SendExcavatedToGraveyardStep => false,
        OpponentSendsFromDeckStep s => s.Filter is null,
        OpponentAddsFromDeckStep s => s.Filter is null,
        OpponentDiscardsMatchingStep s => s.Filter is null,
        TakeControlStep s => !Enum.IsDefined(s.Duration),
        GiveControlStep s => !Enum.IsDefined(s.Duration),
        SwitchControlStep or SwapChosenMonstersStep => false,
        DestroyTargetAtStep s => s.Target is < 0 or > 5,
        GiveTargetControlStep s => s.Target is < 0 or > 5,
        TakeChosenControlStep s => s.Filter is null || !Enum.IsDefined(s.Duration) || s.Duration == ControlDuration.WhileEquipped,
        StopUnlessActivatedThisTurnStep => false,
        SkipPhaseStep s => s.Phase is not (DuelPhase.Draw or DuelPhase.Standby or DuelPhase.Battle or DuelPhase.Main2) ||
            s.Count is < 1 or > 3 || !Enum.IsDefined(s.Player),
        EndBattlePhaseStep => false,
        LockTargetsUntilEndStep s => s.Actions == LockedAction.None,
        StopUnlessOwnTurnStep => false,
        ChangeTargetsTraitStep s => !Enum.IsDefined(s.Trait),
        ChangeTargetsNameStep => false,
        SetTargetScaleStep => false,
        StopUnlessSpecialSummonedStep or SendSourceToGraveyardStep => kind != EffectKind.MonsterTrigger,
        StopUnlessMilledStep s => s.Filter is null,
        ApplyDamageRuleStep s => s.Rule is null || !Enum.IsDefined(s.Rule.Kind) || (s.Rule.Applies & DamageKinds.Any) == 0 || !Enum.IsDefined(s.Scope),
        ModifyCombatantStatsStep s => s.Attack == 0 && s.Defense == 0,
        BanishAllStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
        DestroyAllStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Filter),
        ReturnAllToHandStep s => !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Side),
        ChangeAllToDefenseStep s => !Enum.IsDefined(s.Side),
        DestroyExtremeMonsterStep s => !Enum.IsDefined(s.Side) || !Enum.IsDefined(s.Stat) || !Enum.IsDefined(s.Extreme) || !Enum.IsDefined(s.Filter),
        _ => true
    };
}
