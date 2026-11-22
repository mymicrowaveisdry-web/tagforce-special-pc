namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>PC-authored system-style screens drawn in the pixel font over an opaque fill: the name keyboard and the save prompt and its
/// completion. They stand in for PSP system dialogs, which are not original game UI.</summary>
public static class TitleFlowOverlay
{
    public static readonly (int R, int G, int B) Fill = (16, 16, 16), White = (255, 255, 255), Typed = (238, 196, 104);

    /// <summary>The lines for <paramref name="flow"/>'s step, or null when it has no system screen.</summary>
    public static IReadOnlyList<(string Text, int X, int Y, (int R, int G, int B) Color)>? Lines(TitleFlow flow, bool profilePresent) =>
        Lines(flow.Step, flow.Name, flow.ButtonFocus, profilePresent);

    public static IReadOnlyList<(string Text, int X, int Y, (int R, int G, int B) Color)>? Lines(TitleFlowStep step, string name, int buttonFocus,
        bool profilePresent) => step switch
    {
        TitleFlowStep.NameKeyboard =>
        [
            ($"NAME ({TitleFlow.NameLimit} LETTERS OR LESS)", 120, 70, White), (name + "_", 190, 110, Typed),
            ("TYPE ON KEYBOARD", 160, 160, White), ("ENTER/F1: FINISH   ESC: DELETE", 90, 190, White)
        ],
        TitleFlowStep.SystemSavePrompt =>
        [
            (profilePresent ? "DO YOU WANT TO OVERWRITE THE DATA?" : "DO YOU WANT TO SAVE THIS DATA?", 100, 100, White),
            ((buttonFocus == 0 ? "> " : "  ") + "YES", 170, 140, White), ((buttonFocus == 1 ? "> " : "  ") + "NO", 270, 140, White)
        ],
        TitleFlowStep.SystemSaveDone => [("SAVE COMPLETED.", 170, 120, White), ("ESC / B: BACK", 185, 170, White)],
        _ => null
    };
}
