using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>Commitment-only profiles; these do not implement the cards' resolution.</summary>
public static class ActivationResearch
{
    public static ActivationContract LightningVortex(IEnumerable<uint> allowedDiscardIds) =>
        new("lightning-vortex-discard-commit-v1", 5217, allowedDiscardIds);

    public static ActivationContract MysticalSpaceTyphoon() =>
        new("mystical-space-typhoon-target-commit-v1", 4909, requiresSpellTrapTarget: true);
}
