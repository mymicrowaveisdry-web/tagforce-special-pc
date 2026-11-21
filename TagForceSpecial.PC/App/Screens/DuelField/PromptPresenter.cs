using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Draws the current prompt, a page of legal choices with the selected one highlighted, the control hint and the last notice.</summary>
public static class PromptPresenter
{
    public const int PageSize = 4;

    public static void Draw(DuelDrawList list, DuelView view, int choice, string notice)
    {
        list.Wrapped(DuelPart.Prompt, view.Prompt, 350, 146, 20, 2, DuelPalette.Gold);
        int start = choice / PageSize * PageSize;
        for (int i = start; i < Math.Min(start + PageSize, view.Choices.Count); i++)
        {
            int y = 168 + (i - start) * 17;
            if (i == choice) list.Fill(DuelPart.Prompt, new(348, y - 2, 124, 17), new(49, 65, 79));
            list.Wrapped(DuelPart.Prompt, view.Choices[i].Label, 350, y, 20, 2, i == choice ? DuelPalette.Gold : Microsoft.Xna.Framework.Color.White);
        }
        list.Text(DuelPart.Prompt, view.Choices.Count == 0 ? "WAIT" : $"{choice + 1}/{view.Choices.Count}  UP/DOWN", 350, 240, DuelPalette.Muted);
        list.Text(DuelPart.Prompt, "ENTER/A SELECT  TAB/Y INSPECT  ESC/B MENU", 8, 256, DuelPalette.Muted);
        list.Text(DuelPart.Prompt, notice.Length > 77 ? notice[..77] : notice, 8, 265, DuelPalette.Gold);
    }
}
