using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Bounded original experiments only; no general card-support claim.</summary>
public static class ChainResearch
{
    public static ChainEffectProfile LightningVortex() =>
        new("lightning-vortex-discard-commit-v1", 5217, ChainOperation.DestroyOpposingFaceUpMonsters);
    public static ChainEffectProfile MysticalSpaceTyphoon() =>
        new("mystical-space-typhoon-target-commit-v1", 4909, ChainOperation.DestroyBoundSpellTrap);
    // Token/empty-zone ordering is an explicit deterministic PC policy.
    public static ChainEffectProfile Scapegoat() =>
        new("scapegoat-targeted-response-v1", 4818, ChainOperation.SummonFourTokens, [3905, 3903, 3902, 3904]);
}
