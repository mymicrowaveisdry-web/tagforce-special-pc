namespace TagForceSpecial.Core;

/// <summary>Applies immediate LP payments and constructs committed activation values without publishing engine state.</summary>
internal static class ActivationCommitment
{
    internal static (DuelState State, DuelEvent[] Head) PayLifePointCost(DuelState state, PlayerId playerId, EffectDefinition definition, DuelEvent[] head)
    {
        if (definition.Cost is not (PayLifePointsCost or PayHalfLifePointsCost)) return (state, head);
        var payer = state.GetPlayer(playerId);
        int amount = definition.Cost is PayLifePointsCost pay ? pay.Amount : payer.LifePoints / 2;
        return (state.With(player: payer.With(lifePoints: payer.LifePoints - amount)),
            [.. head, new LifePointsPaid(playerId, amount, payer.LifePoints, payer.LifePoints - amount, definition.EffectId)]);
    }

    /// <summary>Checks uninterrupted source placement, allowing the existing self-Tribute exceptions.</summary>
    internal static bool SourceIntact(PendingSpellActivation activation, DuelPlayerState player, EffectDefinition definition) =>
        definition.Origin != EffectOrigin.Field || (activation.IsMonsterEffect
            ? definition.Cost is TributeSelfCost or TributeCost ||
                player.MonsterZones[activation.SpellTrapZone] is { } monster && monster.Card == activation.Spell && monster.PlacementId == activation.SourcePlacementId
            : player.SpellTrapAt(activation.SpellTrapZone) is { IsFaceUp: true } source && source.Card == activation.Spell && source.PlacementId == activation.SourcePlacementId);

    /// <summary>Automatic targets are resolved synchronously only when the answers contain no explicit field targets.</summary>
    internal static CommittedActivation CreateReceipt(PendingSpellActivation activation, EffectDefinition definition,
        DecisionAnswer[] answers, Func<IReadOnlyList<FieldCardTarget>?> automaticTargets)
    {
        var target = answers.SingleOrDefault(a => a.Kind == DecisionKind.SpellTrapTarget)?.Target;
        var receipt = new CommittedActivation(activation.Player, definition.EffectId, activation.Spell,
            activation.SpellTrapZone, activation.DiscardedCost, target,
            answers.Skip(definition.CostTargetSteps).Any(a => a.FieldTarget is not null)
                ? Array.AsReadOnly(answers.Skip(definition.CostTargetSteps).Select(a => a.FieldTarget).OfType<FieldCardTarget>().ToArray())
                // A cost was chosen but the targets are bound automatically.
                : automaticTargets());
        return receipt;
    }

    /// <summary>Decision IDs and summon-attempt identity come from the accepted state, not the working placement state.</summary>
    internal static (ChainLink Link, PendingChain Pending) StartLink(DuelState acceptedState, CommittedActivation receipt,
        EffectDefinition definition, long placement, PendingChain? chain)
    {
        var link = new ChainLink((chain?.Links.Count ?? 0) + 1, receipt, placement,
            definition.Origin != EffectOrigin.Field ? ChainOperation.ZoneEffect : definition.IsMonsterEffect ? ChainOperation.MonsterEffect : definition.IsFaceUpEffect ? ChainOperation.FieldEffect : ChainOperation.Definition, definition.SpellSpeed,
            TargetLinkNumber: definition.Steps.Any(s => s is NegateActivationStep or NegateEffectStep) || chain is not null && definition.Steps.Any(s => s is NegateSummonStep)
                ? chain!.Links[^1].Number : null,
            SummonAttemptId: chain is null && definition.Steps.Any(s => s is NegateSummonStep) ? acceptedState.PendingSummon?.AttemptId : null);
        var pending = new PendingChain(checked(acceptedState.Revision + 1), (chain?.Links ?? []).Append(link),
            receipt.Player == PlayerId.One ? PlayerId.Two : PlayerId.One);
        return (link, pending);
    }
}
