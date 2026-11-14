using TagForceSpecial.Core;

namespace TagForceSpecial.Tests;

/// <summary>What a synthetic response negates.</summary>
public enum ResponseNegation { Activation, Effect }

/// <summary>Synthetic set negation responses built as effect definitions.</summary>
internal static class ResponseProbe
{
    /// <summary>
    /// A set card answering the preceding link: Spell Speed 3 is a Counter Trap, otherwise a Normal Trap (or a
    /// Quick-Play Spell when <paramref name="spell"/>).
    /// </summary>
    internal static EffectDefinition Negate(string effectId, uint cardId, ResponseNegation negation, ChainSpellSpeed speed = ChainSpellSpeed.Two,
        IEnumerable<uint>? discardCardIds = null, CardCategory? respondsTo = null, bool destroy = false, bool spell = false) =>
        new(effectId, cardId, speed == ChainSpellSpeed.Three ? EffectKind.CounterTrap : spell ? EffectKind.QuickPlaySpell : EffectKind.NormalTrap,
            [negation == ResponseNegation.Activation ? new NegateActivationStep(destroy) : new NegateEffectStep(destroy)],
            [new RespondsToActivationCondition(respondsTo)],
            cost: discardCardIds is null ? null : new DiscardCost(new CardIdFilter(discardCardIds)));

    /// <summary>A set Normal Trap negating the current attack during <paramref name="windows"/>.</summary>
    internal static EffectDefinition NegateAttack(string effectId, uint cardId, BattleTiming windows = BattleTiming.AttackDeclaration) =>
        new(effectId, cardId, EffectKind.NormalTrap, [new NegateAttackStep()], [new BattleTimingCondition(windows)],
            fieldTargets: [new AttackingMonsterTarget()]);

    /// <summary>A set Normal Trap destroying the attacker during <paramref name="windows"/>.</summary>
    internal static EffectDefinition DestroyAttacker(string effectId, uint cardId, BattleTiming windows = BattleTiming.AttackDeclaration) =>
        new(effectId, cardId, EffectKind.NormalTrap, [new DestroyTargetsStep()], [new BattleTimingCondition(windows)],
            fieldTargets: [new AttackingMonsterTarget()]);

    /// <summary>A set Normal Trap changing a combatant's stats until the attack ends.</summary>
    internal static EffectDefinition ModifyCombatant(string effectId, uint cardId, BattleTiming windows, int attack, int defense = 0,
        bool defender = false, bool duringCalculation = false) =>
        new(effectId, cardId, EffectKind.NormalTrap, [new ModifyCombatantStatsStep(attack, defense)],
            [new BattleTimingCondition(windows, duringCalculation)],
            fieldTargets: [defender ? new DefendingMonsterTarget() : new AttackingMonsterTarget()]);

    /// <summary>A set Normal Trap Special Summoning one of <paramref name="cardIds"/> from the hand during <paramref name="windows"/>.</summary>
    internal static EffectDefinition SummonFromHand(string effectId, uint cardId, BattleTiming windows, params uint[] cardIds) =>
        new(effectId, cardId, EffectKind.NormalTrap, [new SpecialSummonStep(SpecialSummonSources.Hand, cardIds)],
            [new BattleTimingCondition(windows)]);

    /// <summary>A set Counter Trap negating the opponent's Summon attempt of <paramref name="kinds"/>, paying <paramref name="lifePointCost"/> LP.</summary>
    internal static EffectDefinition NegateSummon(string effectId, uint cardId, int lifePointCost = 0, SummonKinds kinds = SummonKinds.Normal,
        bool group = false) =>
        new(effectId, cardId, EffectKind.CounterTrap, [new NegateSummonStep()],
            [new MonsterWouldBeSummonedCondition(kinds, ByOpponent: true, Group: group)],
            cost: lifePointCost > 0 ? new PayLifePointsCost(lifePointCost) : null);
}