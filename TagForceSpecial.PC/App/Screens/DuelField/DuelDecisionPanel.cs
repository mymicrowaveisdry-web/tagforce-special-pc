using Microsoft.Xna.Framework;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class DuelDecisionPanel
{
    private const int RowsPerPage = 5;
    private const int RowHeight = 24;
    private const DuelPart Part = DuelPart.Prompt;

    public static void DrawOriginal(DuelDrawList list, ListModal modal)
    {
        int start = modal.Index / RowsPerPage * RowsPerPage;
        var choices = modal.Choices.Skip(start).Take(RowsPerPage).ToArray();
        var rows = choices.Select(choice => new MenuItem(Label(choice), new SubmitCommand(choice.Id))).ToArray();
        list.Add(new DuelFieldMenu(Part, rows, modal.Index - start, 314, 224));
        list.Add(new DuelFieldText(Part, Title(modal), 314, 54.42f, 0.6f, 0.54f, 0xffffff, 128) { Centered = true });
    }

    public static void Draw(DuelDrawList list, ListModal modal)
    {
        int start = modal.Index / RowsPerPage * RowsPerPage;
        int end = Math.Min(start + RowsPerPage, modal.Choices.Count);
        var box = new Rectangle(154, 43, 312, 50 + (end - start) * RowHeight);
        list.Fill(Part, box, new Color(19, 29, 45), SceneLayer.Modal);
        list.Border(Part, box, new Color(65, 89, 116), SceneLayer.ModalHighlight);
        list.Text(Part, Title(modal), box.X + 12, box.Y + 10, DuelPalette.Gold, SceneLayer.ModalText);

        for (int i = start; i < end; i++)
        {
            int y = box.Y + 27 + (i - start) * RowHeight;
            bool selected = i == modal.Index;
            list.Fill(Part, new Rectangle(box.X + 7, y, box.Width - 14, RowHeight - 3),
                selected ? new Color(43, 85, 117) : new Color(29, 44, 63), SceneLayer.ModalHighlight);
            list.Add(new DuelWrapped(Part, Label(modal.Choices[i]), box.X + 13, y + 2, 47, 2, Color.White)
                { Ellipsis = true, Layer = SceneLayer.ModalText });
        }

        string footer = modal.Cancelable ? "Move / Confirm / Back" : "Move / Confirm";
        list.Text(Part, footer, box.X + 12, box.Bottom - 13, DuelPalette.Muted, SceneLayer.ModalText);
        string count = $"{modal.Index + 1}/{modal.Choices.Count}";
        list.Text(Part, count, box.Right - 12 - 6 * count.Length, box.Bottom - 13, DuelPalette.Muted, SceneLayer.ModalText);
    }

    private static string Title(ListModal modal)
    {
        if (modal.Parent is ResponseModal) return "Choose response";
        if (modal.Choices.All(choice => choice.Label.StartsWith("Tribute ", StringComparison.Ordinal))) return "Choose tributes";
        return modal.Choices.FirstOrDefault(choice => choice.Kind != ActionKind.FinishChoice)?.Kind switch
        {
            ActionKind.ChooseTarget or ActionKind.Attack => "Choose target",
            ActionKind.ChoosePosition or ActionKind.Position => "Choose position",
            ActionKind.ChooseZone => "Choose zone",
            ActionKind.ChooseCard => "Choose card",
            ActionKind.Discard => "Choose discard",
            ActionKind.ReplayAttack or ActionKind.CancelReplay => "Attack replay",
            ActionKind.OrderTriggers or ActionKind.DeclineTriggers => "Order effects",
            _ => "Choose action"
        };
    }

    private static string Label(ChoiceView choice) => choice is { Kind: ActionKind.ChoosePosition, Position: { } position }
        ? position switch
        {
            BattlePosition.FaceUpAttack => "Face-up Attack",
            BattlePosition.FaceUpDefense => "Face-up Defense",
            BattlePosition.FaceDownDefense => "Face-down Defense",
            _ => choice.Label
        }
        : choice.Label;
}
