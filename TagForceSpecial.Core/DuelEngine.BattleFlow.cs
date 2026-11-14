namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    private BattleResponseWindow NewBattleWindow(BattleTiming timing, PlayerId responder) =>
        new(checked(State.Revision + 1), timing, responder);

    private static BattleDamageCalculation CalculateBasicBattle(int attack, int? defendingValue,
        BattlePosition? defendingPosition, PlayerId attacker, PlayerId defender, bool piercing = false)
    {
        if (defendingPosition is null) return new(attack, defender, false, false);
        int difference = attack - defendingValue!.Value;
        if (defendingPosition == BattlePosition.FaceUpAttack)
            return new(Math.Abs(difference), difference < 0 ? attacker : defender,
                difference < 0 || difference == 0 && attack > 0,
                difference > 0 || difference == 0 && attack > 0);
        return new(difference > 0 && piercing ? difference : Math.Max(0, -difference), difference < 0 ? attacker : defender, false, difference > 0);
    }

    private DuelActionResult ExecuteBattleFlow(DuelCommand command)
    {
        var battle = State.Battle!;
        if (battle.Replacement is { } replacement)
            return command is ChooseDestructionReplacement answer ? AnswerBattleReplacement(battle, replacement, answer) : Reject(DuelError.DecisionPending);
        if (command is ChooseDestructionReplacement) return Reject(DuelError.NoPendingDecision);
        if (command is PassBattleResponse pass)
        {
            if (pass.DecisionId != battle.Window.DecisionId) return Reject(DuelError.StaleBattleDecision);
            if (battle.Window.Closed) return Reject(DuelError.BattleWindowClosed);
            if (pass.Player != battle.Window.Responder) return Reject(DuelError.NotBattleResponder);
            var window = battle.Window with { DecisionId = checked(State.Revision + 1), Responder = Other(pass.Player),
                ConsecutivePasses = battle.Window.ConsecutivePasses + 1 };
            List<DuelEvent> events = [new BattleResponsePassed(pass.Player)];
            if (window.Closed)
            {
                events.Add(new BattleWindowClosed(window.Timing));
                if (battle.Step == BattleStep.Battle && battle.Attack is { ReplayRequired: true, Cancellation: null } attack)
                    events.Add(new AttackReplayRequested(attack.Id));
            }
            else events.Add(new BattleResponseRequested(window));
            return Accept(State.WithBattle(battle with { Window = window }), events.ToArray());
        }
        if (command is ActivateBattleResponse activate)
        {
            if (activate.DecisionId != battle.Window.DecisionId) return Reject(DuelError.StaleBattleDecision);
            if (battle.Window.Closed) return Reject(DuelError.BattleWindowClosed);
            if (activate.Player != battle.Window.Responder) return Reject(DuelError.NotBattleResponder);
            if (activate.SpellTrapZone is < 0 or >= 5) return Reject(DuelError.InvalidSpellTrapZone);
            var source = State.GetPlayer(command.Player).SpellTrapFields[activate.SpellTrapZone];
            if (source is null || !EffectAt(source.Card.GameCardId, activate.EffectIndex, out var definition))
                return Reject(DuelError.UnsupportedBattleResponse);
            return BeginBattleDefinition(command.Player, activate.SpellTrapZone, source, definition);
        }
        if (command is ActivateZoneEffectBattleResponse or ActivateFieldEffectBattleResponse)
        {
            var (decision, responder) = command switch
            {
                ActivateZoneEffectBattleResponse z => (z.DecisionId, z.Player),
                ActivateFieldEffectBattleResponse f => (f.DecisionId, f.Player),
                _ => throw new InvalidOperationException()
            };
            if (decision != battle.Window.DecisionId) return Reject(DuelError.StaleBattleDecision);
            if (battle.Window.Closed) return Reject(DuelError.BattleWindowClosed);
            if (responder != battle.Window.Responder) return Reject(DuelError.NotBattleResponder);
            return command is ActivateZoneEffectBattleResponse zone
                ? BeginZoneEffect(zone.Player, zone.CardInstanceId, zone.EffectIndex, null, window: true)
                : BeginFieldEffect(responder, ((ActivateFieldEffectBattleResponse)command).SpellTrapZone, ((ActivateFieldEffectBattleResponse)command).EffectIndex, window: true);
        }
        if (command is ActivateHandBattleResponse hand)
        {
            if (hand.DecisionId != battle.Window.DecisionId) return Reject(DuelError.StaleBattleDecision);
            if (battle.Window.Closed) return Reject(DuelError.BattleWindowClosed);
            if (hand.Player != battle.Window.Responder) return Reject(DuelError.NotBattleResponder);
            return BeginBattleHandDefinition(hand);
        }
        if (command is ActivateMonsterBattleResponse quick)
        {
            if (quick.DecisionId != battle.Window.DecisionId) return Reject(DuelError.StaleBattleDecision);
            if (battle.Window.Closed) return Reject(DuelError.BattleWindowClosed);
            if (quick.Player != battle.Window.Responder) return Reject(DuelError.NotBattleResponder);
            return BeginMonsterEffect(quick.Player, quick.MonsterZone, null, window: true, detach: quick.DetachInstanceIds, effectIndex: quick.EffectIndex);
        }
        if (command.Player != State.ActivePlayer) return Reject(DuelError.NotActivePlayer);
        if (command is AdvanceBattle advance && advance.DecisionId != battle.Window.DecisionId ||
            command is ChooseAttackReplay replay && replay.DecisionId != battle.Window.DecisionId)
            return Reject(DuelError.StaleBattleDecision);
        if (!battle.Window.Closed) return Reject(DuelError.BattleWindowPending);
        if (command is DeclareAttack declaration) return DeclareStagedAttack(declaration.AttackerZone, declaration.TargetZone);
        if (command is ChooseAttackReplay choice) return ChooseBattleReplay(choice);
        if (command is ChangePhase { Target: DuelPhase.Main2 or DuelPhase.End } change && battle.Step == BattleStep.End &&
            (change.Target == DuelPhase.End) != PhaseSkipPending(State, State.ActivePlayer, DuelPhase.Main2))
            return Reject(DuelError.PhaseSkipped);
        if (command is ChangePhase { Target: DuelPhase.End } && battle.Step == BattleStep.End)
        {
            List<DuelEvent> ending = [new PhaseChanged(DuelPhase.Battle, DuelPhase.End)];
            var ended = SpendTurnSkips(ResolveEndPhaseBanishments(State.With(phase: DuelPhase.End).WithBattle(null), ending), ending);
            return Accept(ended, ending.ToArray());
        }
        if (command is ChangePhase { Target: DuelPhase.Main2 } && battle.Step == BattleStep.End)
            return Accept(State.With(phase: DuelPhase.Main2).WithBattle(null), new PhaseChanged(DuelPhase.Battle, DuelPhase.Main2));
        if (command is not AdvanceBattle) return Reject(DuelError.WrongBattleStep);
        return AdvanceBattleFlow();
    }

    private DuelActionResult DeclareStagedAttack(int attackerZone, int? targetZone)
    {
        var battle = State.Battle!;
        if (battle.Step != BattleStep.Battle || battle.Attack is not null) return Reject(DuelError.WrongBattleStep);
        if (attackerZone is < 0 or >= 5 || targetZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var player = State.GetPlayer(State.ActivePlayer); var opponent = State.GetPlayer(Other(player.Id));
        var attacker = player.MonsterZones[attackerZone];
        if (attacker is null) return Reject(DuelError.MonsterZoneEmpty);
        if (attacker.Position != BattlePosition.FaceUpAttack) return Reject(DuelError.AttackerNotInAttackPosition);
        if (AttackAllowanceError(player, attacker, targetZone) is not DuelError.None and var allowanceError) return Reject(allowanceError);
        var defender = targetZone is int zone ? opponent.MonsterZones[zone] : null;
        if (targetZone is not null && defender is null) return Reject(DuelError.MonsterZoneEmpty);
        if (targetZone is null && !CanAttackDirectly(opponent, attacker)) return Reject(DuelError.DirectAttackBlocked);
        if (AttackRestrictionError(player, opponent, attacker, targetZone) is not DuelError.None and var toonError) return Reject(toonError);
        if (!rules[attacker.Card.GameCardId].AllowsBasicBattle || defender is not null && !rules[defender.Card.GameCardId].AllowsBasicBattle)
            return Reject(DuelError.UnsupportedBattleCard);
        var stats = catalog[attacker.Card.GameCardId].Statistics;
        var defending = defender is null ? null : catalog[defender.Card.GameCardId].Statistics;
        if (stats.Attack is null || defender is not null &&
            (defender.Position == BattlePosition.FaceUpAttack ? defending!.Attack : defending!.Defense) is null)
            return Reject(DuelError.UnsupportedBattleStatistics);
        var source = new MonsterBinding(player.Id, attackerZone, attacker.Card, attacker.PlacementId);
        var target = defender is null ? null : new MonsterBinding(opponent.Id, targetZone!.Value, defender.Card, defender.PlacementId);
        var attack = new PendingBattleAttack(checked(State.Revision + 1), source, target,
            FieldMonsters(State).Where(e => e.Binding.Player == opponent.Id).Select(e => e.Binding));
        var monsters = player.MonsterZones.ToArray(); monsters[attackerZone] = attacker with { LastAttackTurn = State.TurnNumber };
        var window = NewBattleWindow(BattleTiming.AttackDeclaration, opponent.Id);
        List<DuelEvent> declared = [];
        player = PayToonAttack(player, attacker, declared);
        return Accept(RecordBattleAttack(State.With(player: player.With(monsters: monsters)), attack.Id, source, target).WithBattle(battle with { Attack = attack, Window = window }),
            [.. declared, new AttackDeclared(player.Id, attackerZone, targetZone), new BattleResponseRequested(window)]);
    }

    private DuelActionResult ChooseBattleReplay(ChooseAttackReplay command)
    {
        var battle = State.Battle!;
        if (battle.Step != BattleStep.Battle || battle.Attack is not { ReplayRequired: true, Cancellation: null } attack)
            return Reject(DuelError.NoAttackReplay);
        if (command.Cancel)
        {
            if (command.TargetZone is not null) return Reject(DuelError.InvalidDecisionChoice);
            var cancelled = attack with { Cancellation = BattleAttackCancellation.ReplayDeclined };
            return FinishStagedAttack(State.WithBattle(battle with { Attack = cancelled }),
                [new AttackCancelled(attack.Id, BattleAttackCancellation.ReplayDeclined)]);
        }
        if (command.TargetZone is < 0 or >= 5) return Reject(DuelError.InvalidMonsterZone);
        var opponent = State.GetPlayer(Other(State.ActivePlayer));
        var defender = command.TargetZone is int zone ? opponent.MonsterZones[zone] : null;
        if (command.TargetZone is not null && defender is null) return Reject(DuelError.MonsterZoneEmpty);
        var player = State.GetPlayer(State.ActivePlayer);
        var attacker = player.MonsterZones[attack.Attacker.Zone]!;
        if (AttackAllowanceError(player, attacker, command.TargetZone, attack.Id) is not DuelError.None and var allowanceError) return Reject(allowanceError);
        if (command.TargetZone is null && !CanAttackDirectly(opponent, attacker)) return Reject(DuelError.DirectAttackBlocked);
        if (AttackRestrictionError(player, opponent, attacker, command.TargetZone, payAttackCost: false) is not DuelError.None and var restrictionError) return Reject(restrictionError);
        if (defender is not null && !rules[defender.Card.GameCardId].AllowsBasicBattle) return Reject(DuelError.UnsupportedBattleCard);
        if (defender is not null && (defender.Position == BattlePosition.FaceUpAttack ? catalog[defender.Card.GameCardId].Statistics.Attack :
                catalog[defender.Card.GameCardId].Statistics.Defense) is null) return Reject(DuelError.UnsupportedBattleStatistics);
        var target = defender is null ? null : new MonsterBinding(opponent.Id, command.TargetZone!.Value, defender.Card, defender.PlacementId);
        var changed = new PendingBattleAttack(attack.Id, attack.Attacker, target,
            FieldMonsters(State).Where(e => e.Binding.Player == opponent.Id).Select(e => e.Binding));
        var window = NewBattleWindow(BattleTiming.BattleStep, State.ActivePlayer);
        return Accept(RecordBattleAttack(State, attack.Id, attack.Attacker, target, replay: true).WithBattle(battle with { Attack = changed, Window = window }),
            new AttackReplayChosen(attack.Id, command.TargetZone), new BattleResponseRequested(window));
    }

    private DuelActionResult AdvanceBattleFlow()
    {
        var battle = State.Battle!;
        if (battle.Step == BattleStep.Start)
            return EnterBattleStage(BattleStep.Battle, null, BattleTiming.BattleStep);
        if (battle.Step == BattleStep.End) return Reject(DuelError.WrongBattleStep);
        if (battle.Step == BattleStep.Battle)
        {
            if (battle.Attack is null)
            {
                if (RequiredAttackPending()) return Reject(DuelError.MustAttack);
                return EnterBattleStage(BattleStep.End, null, BattleTiming.End);
            }
            if (battle.Attack.Cancellation is not null) return FinishStagedAttack(State, []);
            if (battle.Attack.ReplayRequired) return Reject(DuelError.AttackReplayPending);
            return EnterBattleStage(BattleStep.Damage, DamageStep.Start, BattleTiming.DamageStart);
        }
        var attack = battle.Attack!;
        switch (battle.DamageStep)
        {
            case DamageStep.Start:
            {
                var next = State; List<DuelEvent> events = [];
                if (attack.Cancellation is null && attack.Defender is { } target && target.IsStillPresent(next))
                {
                    var player = next.GetPlayer(target.Player); var monster = player.MonsterZones[target.Zone]!;
                    if (monster.Position == BattlePosition.FaceDownDefense)
                    {
                        var monsters = player.MonsterZones.ToArray(); monsters[target.Zone] = monster with { Position = BattlePosition.FaceUpDefense, FlippedOnTurn = next.TurnNumber };
                        next = next.With(player: player.With(monsters: monsters));
                        attack = attack with { RevealedDefender = target };
                        events.Add(new MonsterRevealed(target.Player, target.Zone, target.Card));
                    }
                }
                return EnterBattleStage(BattleStep.Damage, DamageStep.BeforeCalculation, BattleTiming.BeforeCalculation,
                    next.WithBattle(battle with { Attack = attack }), events);
            }
            case DamageStep.BeforeCalculation:
                return EnterBattleStage(BattleStep.Damage, DamageStep.Calculation, BattleTiming.Calculation);
            case DamageStep.Calculation:
                return CalculateStagedDamage();
            case DamageStep.AfterCalculation:
            {
                var next = State; List<DuelEvent> events = [];
                foreach (var (binding, monster) in FieldMonsters(next).Where(e => e.Monster.BattleDestroyed).ToArray())
                {
                    var player = next.GetPlayer(binding.Player); var monsters = player.MonsterZones.ToArray(); monsters[binding.Zone] = null;
                    next = next.With(player: player.With(monsters: monsters));
                    next = SendFieldCardToGraveyard(next, monster.Card, new MonsterDestroyed(player.Id, binding.Zone, monster.Card)
                        { ByBattle = true, DestroyedBy = binding.Card == attack.Attacker.Card ? attack.Defender : attack.Attacker }, events);
                }
                return EnterBattleStage(BattleStep.Damage, DamageStep.End, BattleTiming.DamageEnd, next, events);
            }
            case DamageStep.End:
                return FinishStagedAttack(State, []);
            default: return Reject(DuelError.WrongBattleStep);
        }
    }

    private DuelActionResult EnterBattleStage(BattleStep step, DamageStep? damage, BattleTiming timing,
        DuelState? next = null, List<DuelEvent>? events = null)
    {
        next ??= State; events ??= [];
        var old = next.Battle!; var window = NewBattleWindow(timing, State.ActivePlayer);
        if (old.Step != step) events.Add(new BattleStepChanged(old.Step, step));
        if (damage is { } value && old.DamageStep != damage) events.Add(new DamageStepChanged(old.DamageStep, value));
        events.Add(new BattleResponseRequested(window));
        return Accept(next.WithBattle(old with { Step = step, DamageStep = damage, Window = window }), events.ToArray());
    }

    private DuelActionResult CalculateStagedDamage(IReadOnlyList<ReplacementDecision>? answers = null)
    {
        try { return CalculateStagedDamageCore(answers); }
        finally { replacementContext = null; }
    }

    private DuelActionResult CalculateStagedDamageCore(IReadOnlyList<ReplacementDecision>? answers)
    {
        var battle = State.Battle! with { Replacement = null }; var attack = battle.Attack!; var next = State;
        List<DuelEvent> events = [];
        BattleDamageCalculation? calculation = null;
        if (attack.Cancellation is null && attack.Attacker.IsStillPresent(next) &&
            (attack.Defender is null || attack.Defender.IsStillPresent(next)))
        {
            var attacker = next.GetPlayer(attack.Attacker.Player).MonsterZones[attack.Attacker.Zone]!;
            var defender = attack.Defender is { } target ? next.GetPlayer(target.Player).MonsterZones[target.Zone] : null;
            var attackStats = GetMonsterStatistics(next, attack.Attacker.Player, attack.Attacker.Zone)!;
            var defenseStats = attack.Defender is { } binding ? GetMonsterStatistics(next, binding.Player, binding.Zone) : null;
            var defending = defender?.Position == BattlePosition.FaceUpAttack ? defenseStats?.Attack : defenseStats?.Defense;
            if (attackStats.Attack is not int value || defender is not null && defending is null)
                return Reject(DuelError.UnsupportedBattleStatistics);
            calculation = CalculateBasicBattle(value, defending, defender?.Position, attack.Attacker.Player, Other(attack.Attacker.Player),
                GetBattleAbilities(next, attack.Attacker.Player, attacker).Piercing);
            FieldCardTarget[] batch = [.. new[] { calculation.DestroyAttacker ? attack.Attacker : null, calculation.DestroyDefender ? attack.Defender : null }
                .OfType<MonsterBinding>().Select(b => new FieldCardTarget(b.Player, FieldLocation.MonsterZone, b.Zone, b.Card, b.PlacementId))];
            if (batch.Length != 0)
            {
                // Optional replacements are answered before calculation; nothing can happen between it and the destruction.
                if (answers is null && OptionalOffers(next, batch, DestructionCause.Battle) is { Count: > 0 } offers)
                {
                    var pending = new PendingReplacement(checked(State.Revision + 1), offers, []);
                    return Accept(State.WithBattle(battle with { Replacement = pending }), new DestructionReplacementRequested(pending));
                }
                replacementContext = new(batch, answers ?? []);
            }
            events.Add(new BattleDamageCalculated(attack.Id, calculation));
            if (calculation.Damage > 0)
            {
                next = ApplyBattleDamage(next, calculation.DamagedPlayer, calculation.Damage, events);
                if (next.Outcome is not null) return Accept(next.WithBattle(battle with { Attack = attack with { Calculation = calculation } }), events.ToArray());
            }
            void Mark(MonsterBinding binding)
            {
                if (ReplaceDestruction(next, binding, DestructionCause.Battle, events, binding == attack.Attacker ? attack.Defender : attack.Attacker) is { } replaced)
                {
                    next = replaced;
                    return;
                }
                var player = next.GetPlayer(binding.Player); var monsters = player.MonsterZones.ToArray();
                monsters[binding.Zone] = monsters[binding.Zone]! with { BattleDestroyed = true };
                next = next.With(player: player.With(monsters: monsters));
            }
            if (calculation.DestroyAttacker) Mark(attack.Attacker);
            if (calculation.DestroyDefender) Mark(attack.Defender!);
        }
        else events.Add(new BattleDamageCalculationSkipped(attack.Id));
        if (attack.RevealedDefender is { } flipped) events.Add(new BattleFlipEffectWindowOpened(flipped));
        next = next.WithBattle(battle with { Attack = attack with { Calculation = calculation } });
        return EnterBattleStage(BattleStep.Damage, DamageStep.AfterCalculation, BattleTiming.AfterCalculation, next, events);
    }

    private DuelActionResult FinishStagedAttack(DuelState next, List<DuelEvent> events)
    {
        var battle = next.Battle!; var id = battle.Attack!.Id;
        var window = NewBattleWindow(BattleTiming.BattleStep, State.ActivePlayer);
        events.Add(new BattleAttackFinished(id));
        if (battle.Step != BattleStep.Battle) events.Add(new BattleStepChanged(battle.Step, BattleStep.Battle));
        events.Add(new BattleResponseRequested(window));
        return Accept(next.WithBattle(new(BattleStep.Battle, null, window)), events.ToArray());
    }

    private DuelState PrepareBattleState(DuelState before, DuelState next, List<DuelEvent> events)
    {
        if (next.Battle is not { } battle) return next;
        if (next.Phase != DuelPhase.Battle) return next.WithBattle(null);
        var attack = battle.Attack;
        if (attack is { Cancellation: null } && (battle.Step != BattleStep.Damage ||
            battle.DamageStep is DamageStep.Start or DamageStep.BeforeCalculation or DamageStep.Calculation))
        {
            var currentAttacker = attack.Attacker.IsStillPresent(next)
                ? next.GetPlayer(attack.Attacker.Player).MonsterZones[attack.Attacker.Zone] : null;
            bool allowanceLost = false;
            if (currentAttacker is not null)
            {
                var abilities = GetBattleAbilities(next, attack.Attacker.Player, currentAttacker);
                int priorAttacks = AttacksThisTurn(next, currentAttacker).Count(a => a.Id != attack.Id);
                allowanceLost = battle.Step == BattleStep.Battle && priorAttacks >= abilities.AttacksPerBattlePhase && !abilities.AttackAll;
            }
            if (currentAttacker is null || allowanceLost ||
                currentAttacker.Position != BattlePosition.FaceUpAttack ||
                RuleLocked(next, LockedAction.Attack, attack.Attacker.Player, attack.Attacker.Card,
                    next.GetPlayer(attack.Attacker.Player).MonsterZones[attack.Attacker.Zone]))
            {
                attack = attack with { Cancellation = BattleAttackCancellation.AttackerUnavailable };
                events.Add(new AttackCancelled(attack.Id, BattleAttackCancellation.AttackerUnavailable));
            }
            else if (battle.Step == BattleStep.Battle && before.Battle?.Attack?.Id == attack.Id)
            {
                var oldRoster = FieldMonsters(before).Where(e => e.Binding.Player != attack.Attacker.Player).Select(e => e.Binding);
                var newRoster = FieldMonsters(next).Where(e => e.Binding.Player != attack.Attacker.Player).Select(e => e.Binding);
                var opponent = next.GetPlayer(Other(attack.Attacker.Player));
                var attacker = next.GetPlayer(attack.Attacker.Player).MonsterZones[attack.Attacker.Zone]!;
                bool illegalTarget = attack.Defender is { } target
                    ? !target.IsStillPresent(next) || RuleLocked(next, LockedAction.BeAttacked, target.Player, target.Card, opponent.MonsterZones[target.Zone])
                    : !CanAttackDirectly(opponent, attacker, next);
                if (!oldRoster.SequenceEqual(newRoster) || illegalTarget) attack = attack with { ReplayRequired = true };
            }
        }
        var window = battle.Window with { DecisionId = checked(State.Revision + 1) };
        if (before.PendingChain is not null && next.PendingChain is null)
        {
            window = NewBattleWindow(window.Timing == BattleTiming.AttackDeclaration ? BattleTiming.BattleStep : window.Timing, next.ActivePlayer);
            events.Add(new BattleResponseRequested(window));
        }
        return next.WithBattle(battle with { Attack = attack, Window = window });
    }
}
