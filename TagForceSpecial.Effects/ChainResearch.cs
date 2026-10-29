using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Bounded original experiments only; no general card-support claim. TO BE REMOVED ONCE THE ACTUAL DUEL ENGINE IS FINISHED</summary>
public static class ChainResearch
{
    public static ChainEffectProfile LightningVortex() =>
        new("lightning-vortex-discard-commit-v1", 5217, ChainOperation.DestroyOpposingFaceUpMonsters);
    public static ChainEffectProfile MysticalSpaceTyphoon() =>
        new("mystical-space-typhoon-target-commit-v1", 4909, ChainOperation.DestroyBoundSpellTrap);
    // Token/empty-zone ordering is an explicit deterministic PC policy.
    public static ChainEffectProfile Scapegoat() =>
        new("scapegoat-targeted-response-v1", 4818, ChainOperation.SummonFourTokens, [3905, 3903, 3902, 3904]);

    /// <summary>Static-code-backed set response; broad Quick-Play windows remain outside this slice.</summary>
    public static ChainResponseProfile MysticalSpaceTyphoonResponse() =>
        new("mystical-space-typhoon-target-commit-v1", 4909, ChainOperation.DestroyBoundSpellTrap, ChainSpellSpeed.Two);

    /// <summary>
    /// Static-code-backed activation negation and destruction, with an explicit
    /// discard allowlist. Protection, other timing windows and complete card support remain gated.
    /// </summary>
    public static ChainResponseProfile MagicJammer(IEnumerable<uint> allowedDiscardIds)
    {
        ArgumentNullException.ThrowIfNull(allowedDiscardIds);
        return new("magic-jammer-static-response-v1", 4862, ChainOperation.NegateActivation, ChainSpellSpeed.Three,
            allowedDiscardIds, respondsToCategory: CardCategory.Spell, destroyNegatedSource: true);
    }
}
