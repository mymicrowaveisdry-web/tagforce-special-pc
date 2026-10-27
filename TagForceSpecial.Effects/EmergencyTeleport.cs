using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>
/// TO BE REMOVED AFTER ACTUAL CARD EFFECTS ARE IMPLEMENTED
/// PSP-observed deck Krebons / attack-position branch, optionally through direct
/// End Phase banishment. No hand summons, shuffle or responses. Research opt-in only.
/// </summary>
public static class EmergencyTeleport
{
    public const uint CardId = 7747;
    public const uint SupportedMonsterId = 7716;
    public const string EffectId = "emergency-teleport-activation-resolution-v1";

    public static DeckSpecialSummonSpell ActivationResolutionOnly() =>
        new(EffectId, CardId, [SupportedMonsterId]);

    /// <summary>
    /// Deck Krebons in attack position, then End Phase banishment or expiry after
    /// basic battle destruction. Stops in End Phase; not a full card implementation.
    /// </summary>
    public static DeckSpecialSummonSpell EndPhaseLifecycleOnly() =>
        new("emergency-teleport-end-phase-lifecycle-v1", CardId, [SupportedMonsterId], banishAtEndPhase: true);
}
