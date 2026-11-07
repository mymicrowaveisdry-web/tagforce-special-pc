namespace TagForceSpecial.Core;

public sealed partial class DuelEngine
{
    public bool UsesStagedBattle => experimentalBattleFlow;
    /// <summary>
    /// Checks a command with the same transition as Execute, without publishing state or events. Not a player
    /// visibility API.
    /// </summary>
    public DuelError CheckCommand(DuelCommand command)
    {
        var probe = (DuelEngine)MemberwiseClone();
        return probe.Execute(command).Error;
    }
}
