namespace TagForceSpecial.Core;

[Flags]
public enum DamageKinds { Battle = 1, Effect = 2, Any = Battle | Effect }

/// <summary>How a player's damage or LP gain is changed.</summary>
public enum DamageRuleKind
{
    /// <summary>The player's opponent takes the damage instead.</summary>
    RedirectToOpponent,
    /// <summary>The player gains the amount instead of taking damage.</summary>
    BecomesGain,
    /// <summary>An effect that would make the player gain LP inflicts that much damage instead.</summary>
    GainBecomesDamage
}

public enum DamageRuleScope { ThisTurn, ThisAttack }

public sealed record DamageRule(DamageRuleKind Kind, DamageKinds Applies = DamageKinds.Any);

/// <summary>A rule that applies while its source is face-up; <paramref name="Side"/> is whose damage or gain it changes, relative to its controller.</summary>
public sealed record DamageRuleProfile(string EffectId, uint CardId, DamageRule Rule, TargetSide Side = TargetSide.Own);

/// <summary>Applies a rule to the activating player for this turn, or for the attack in progress.</summary>
public sealed record ApplyDamageRuleStep(DamageRule Rule, DamageRuleScope Scope = DamageRuleScope.ThisTurn) : EffectStep;

internal sealed record TimedDamageRule(PlayerId Player, int TurnNumber, long? AttackId, DamageRule Rule);

public sealed partial class DuelEngine
{
    private bool HasDamageRule(DuelState state, PlayerId player, DamageRuleKind kind, DamageKinds damage)
    {
        if (state.Bookkeeping.TimedDamageRules.Any(t => t.Player == player && t.TurnNumber == state.TurnNumber && t.Rule.Kind == kind &&
                (t.Rule.Applies & damage) != 0 && (t.AttackId is null || state.Battle?.Attack?.Id == t.AttackId)))
            return true;
        if (damageRuleProfiles.Count == 0) return false;
        bool Applies(PlayerId controller, DamageRuleProfile profile) => profile.Rule.Kind == kind && (profile.Rule.Applies & damage) != 0 &&
            (profile.Side == TargetSide.Either || (profile.Side == TargetSide.Own) == (player == controller));
        foreach (var (binding, monster) in FieldMonsters(state))
            if (monster.Position != BattlePosition.FaceDownDefense && !state.EventEffects.NegatedMonsters.Contains(binding) &&
                damageRuleProfiles.TryGetValue(monster.Card.GameCardId, out var monsterProfiles) && monsterProfiles.Any(p => Applies(binding.Player, p)))
                return true;
        bool spellsNegated = IsSpellsNegated(state);
        return ActiveCards(state).Any(e => !(spellsNegated && IsSpellCard(e.Field)) &&
            damageRuleProfiles.TryGetValue(e.Field.Card.GameCardId, out var cardProfiles) && cardProfiles.Any(p => Applies(e.Player, p)));
    }

    /// <summary>Who takes damage meant for this player: their opponent when a redirect protects them.</summary>
    private PlayerId DamageRecipient(DuelState state, PlayerId recipient, DamageKinds damage) =>
        HasDamageRule(state, recipient, DamageRuleKind.RedirectToOpponent, damage) ? Other(recipient) : recipient;

    /// <summary>Inflicts battle damage after damage rules; ends the Duel at 0 LP.</summary>
    private DuelState ApplyBattleDamage(DuelState state, PlayerId recipient, int amount, List<DuelEvent> events)
    {
        recipient = DamageRecipient(state, recipient, DamageKinds.Battle);
        if (HasDamageRule(state, recipient, DamageRuleKind.BecomesGain, DamageKinds.Battle))
            return GainLifePoints(state, recipient, amount, "battle-damage", events, converted: true);
        var player = state.GetPlayer(recipient);
        int after = Math.Max(0, player.LifePoints - amount);
        events.Add(new BattleDamageDealt(recipient, amount, player.LifePoints, after));
        var next = state.With(player: player.With(lifePoints: after));
        if (after != 0) return next;
        var outcome = new DuelOutcome(Other(recipient), recipient, DuelEndReason.LifePointsDepleted);
        events.Add(new DuelEnded(outcome));
        return next.With(outcome: outcome);
    }

    private DuelState AddTimedDamageRule(DuelState state, PlayerId controller, ApplyDamageRuleStep step)
    {
        long? attack = step.Scope == DamageRuleScope.ThisAttack ? state.Battle?.Attack?.Id : null;
        if (step.Scope == DamageRuleScope.ThisAttack && attack is null) return state;
        return state.WithBookkeeping(state.Bookkeeping with { TimedDamageRules = Array.AsReadOnly(state.Bookkeeping.TimedDamageRules
            .Where(t => t.TurnNumber == state.TurnNumber).Append(new(controller, state.TurnNumber, attack, step.Rule)).ToArray()) });
    }
}
