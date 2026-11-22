using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Host;

/// <summary>Adapts the native PC profile store to the title route.</summary>
internal sealed class TitleProfileHost(PlayerProfileStore profiles) : ITitleFlowHost
{
    public bool ProfilePresent => profiles.Present;

    public bool TryLoadProfile(out string name, out DuelistLevelChoice level, out PlayerOptions options)
    {
        if (profiles.TryLoad() && profiles.Current is { Options: { } savedOptions } profile)
        {
            name = profile.Name;
            level = profile.Level;
            options = savedOptions;
            return true;
        }

        name = "";
        level = DuelistLevelChoice.FirstTimer;
        options = PlayerOptions.Defaults();
        return false;
    }

    public void CreateProfile(string name, DuelistLevelChoice level, PlayerOptions options) => SaveProfile(name, level, options);

    public void SaveProfile(string name, DuelistLevelChoice level, PlayerOptions options) => profiles.Save(name, level, options);
}
