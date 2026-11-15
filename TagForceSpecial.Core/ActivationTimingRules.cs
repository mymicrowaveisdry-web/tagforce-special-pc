namespace TagForceSpecial.Core;

/// <summary>The source and response window in which an effect is requested.</summary>
internal enum ActivationContext { Hand, OwnSetCard, OpenWindow, ChainResponse, HandChainResponse, BattleWindow, BattleHand, SummonWindow, MonsterField, MonsterChainResponse, MonsterWindow, SpellTrapField, SpellTrapChain, SpellTrapWindow, ZoneField, ZoneChain, ZoneWindow }

/// <summary>Checks source, damage-step and spell-speed timing before any effect requirements or costs.</summary>
internal static class ActivationTimingRules
{
    private const BattleTiming DamageStepTimings = BattleTiming.DamageStart | BattleTiming.BeforeCalculation |
        BattleTiming.Calculation | BattleTiming.AfterCalculation | BattleTiming.DamageEnd;

    internal static DuelError Check(DuelState state, EffectDefinition definition, ActivationContext context,
        FieldSpellTrap? source, ChainLink? preceding)
    {
        // A Pendulum Effect is used only from an active scale.
        if (definition.Origin == EffectOrigin.PendulumZone && context is not (ActivationContext.SpellTrapField or ActivationContext.SpellTrapChain or ActivationContext.SpellTrapWindow))
            return DuelError.UnsupportedSpellEffect;
        // The Damage Step admits only definitions whose battle timing condition lists the current timing.
        if (state.Battle is { } battle && (battle.Window.Timing & DamageStepTimings) != 0 &&
            !definition.Conditions.OfType<BattleTimingCondition>().Any(c => c.Windows.HasFlag(battle.Window.Timing)))
            return DuelError.ActivationTimingNotAllowed;
        bool fromHand = context is ActivationContext.Hand or ActivationContext.HandChainResponse or ActivationContext.BattleHand;
        bool setThisTurn = source is not null && source.SetOnTurn >= state.TurnNumber;
        bool fromMonster = context is ActivationContext.MonsterField or ActivationContext.MonsterChainResponse or ActivationContext.MonsterWindow or ActivationContext.SpellTrapField or
            ActivationContext.SpellTrapChain or ActivationContext.SpellTrapWindow or ActivationContext.ZoneField or ActivationContext.ZoneChain or ActivationContext.ZoneWindow;
        if (!fromHand && !fromMonster && (source is null || source.IsFaceUp))
            return context switch
            {
                ActivationContext.ChainResponse or ActivationContext.OpenWindow => DuelError.UnsupportedChainResponse,
                ActivationContext.BattleWindow => DuelError.UnsupportedBattleResponse,
                ActivationContext.SummonWindow => DuelError.UnsupportedSummonResponse,
                _ => DuelError.UnsupportedSpellEffect
            };
        bool slowerThanPreceding = preceding is not null && (int)definition.SpellSpeed < (int)preceding.SpellSpeed;
        var timing = (context, definition.Kind) switch
        {
            (ActivationContext.MonsterField, EffectKind.MonsterIgnition or EffectKind.MonsterQuick) => DuelError.None,
            (ActivationContext.MonsterChainResponse, EffectKind.MonsterQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.MonsterWindow, EffectKind.MonsterQuick) => DuelError.None,
            (ActivationContext.SpellTrapField, EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.SpellTrapChain, EffectKind.SpellTrapQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.SpellTrapWindow, EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.ZoneField, EffectKind.MonsterIgnition or EffectKind.MonsterQuick or EffectKind.SpellTrapIgnition or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.ZoneChain, EffectKind.MonsterQuick or EffectKind.SpellTrapQuick) => slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.ZoneWindow, EffectKind.MonsterQuick or EffectKind.SpellTrapQuick) => DuelError.None,
            (ActivationContext.Hand, EffectKind.NormalSpell or EffectKind.QuickPlaySpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.None,
            (ActivationContext.Hand, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.BattleHand, EffectKind.QuickPlaySpell) => DuelError.None,
            // Only Counter Traps answer a Summon that has not happened yet.
            (ActivationContext.SummonWindow, EffectKind.CounterTrap) => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard, EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, EffectKind.QuickPlaySpell or EffectKind.NormalTrap or EffectKind.ContinuousTrap)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            // A Counter Trap that answers an attack is activated in the battle window it waits for.
            (ActivationContext.BattleWindow, EffectKind.CounterTrap) when definition.Conditions.Any(c => c is OpponentAttackDeclaredCondition or BattleTimingCondition)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            // So is one whose own timing is a Summon that just happened, in the window after it.
            (ActivationContext.OpenWindow, EffectKind.CounterTrap) when definition.Conditions.Any(c => c is MonsterSummonedCondition)
                => setThisTurn ? DuelError.SetThisTurn : DuelError.None,
            (ActivationContext.OwnSetCard or ActivationContext.OpenWindow or ActivationContext.BattleWindow, _) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, EffectKind.NormalSpell or EffectKind.ContinuousSpell or EffectKind.FieldSpell or EffectKind.EquipSpell) => DuelError.ActivationTimingNotAllowed,
            (ActivationContext.ChainResponse, _) => setThisTurn ? DuelError.SetThisTurn
                : slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            (ActivationContext.HandChainResponse, EffectKind.QuickPlaySpell) =>
                slowerThanPreceding ? DuelError.ChainResponseSpeedNotAllowed : DuelError.None,
            _ => DuelError.ActivationTimingNotAllowed
        };
        return timing;
    }
}
