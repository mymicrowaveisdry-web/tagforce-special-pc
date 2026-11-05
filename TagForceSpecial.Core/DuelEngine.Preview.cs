namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    public bool UsesStagedBattle => experimentalBattleFlow;
    /// <summary>
    /// Checks a command using the same transition as Execute, without publishing state or events.
    /// Configuration and published snapshots are immutable; only the fork's State is replaced.
    /// Call on the same owner thread as Execute. Presentation must limit candidates to commands
    /// available from the player's own information; this is not a player visibility API.
    /// </summary>
    public DuelError CheckCommand(DuelCommand command)
    {
        var probe = (DuelEngine)MemberwiseClone();
        return probe.Execute(command).Error;
    }
}
