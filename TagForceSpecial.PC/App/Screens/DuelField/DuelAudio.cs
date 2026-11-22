using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Maps an accepted change of the authorized duel view to presentation audio cues. No duel cues are mapped yet, so it stays silent.</summary>
public static class DuelAudio
{
    public static IReadOnlyList<AudioCue> Cues(DuelView? before, DuelView after) => [];
}
