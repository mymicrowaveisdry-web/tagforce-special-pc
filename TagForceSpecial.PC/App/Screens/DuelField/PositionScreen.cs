using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.PC.DuelUI;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>The battle position choice (index 0 = face-up Attack, 1 = face-up Defense).</summary>
public sealed record PositionModal(IReadOnlyList<ChoiceView> Choices, int Index) : DuelModal;

/// <summary>The original battle position screen (Order 3.2(d), Gusto Gulldo's Deck summon, order32-tribute-3 v-04): "Select the battle
/// position of the card." over the card upright (Atk Pos.) and sideways (Def Pos.), the selected one framed in cyan; Atk is selected first;
/// ○ confirms. Inferred: Left/Right switch (the Def frame is the Atk frame moved 112 px), × does nothing (the effect's summon is mandatory).</summary>
public static class PositionScreen
{
    public const float OptionPitch = 112;
    public const int MessageOffset = 1180722, AtkOffset = 401538, DefOffset = 401556;
    private static readonly IReadOnlyDictionary<int, (int, int)> Black = new Dictionary<int, (int, int)> { [0] = (0x000000, 64) };

    /// <summary>A decision between exactly face-up Attack and face-up Defense.</summary>
    public static bool Applies(IReadOnlyList<ChoiceView> choices) =>
        choices.Count == 2 && choices.All(c => c.Kind == ActionKind.ChoosePosition)
        && choices.Select(c => c.Position).ToHashSet().SetEquals(new BattlePosition?[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense });

    /// <summary>Attack first, as the original lists it.</summary>
    public static PositionModal Open(IReadOnlyList<ChoiceView> choices) =>
        new([.. choices.OrderBy(c => c.Position == BattlePosition.FaceUpAttack ? 0 : 1)], 0);

    public static void Draw(DuelDrawList list, DuelFieldProfile field, PositionModal modal)
    {
        const DuelPart part = DuelPart.Prompt;
        list.FieldPiece(part, field, "position.clear");
        list.FieldPiece(part, field, "position.window");
        list.Add(new DuelRichText(part, CardAlbumResources.CardInfoKey, MessageOffset, null, 199.8f, 78.42f, 0.6f, 0.54f, Black));
        list.FieldPiece(part, field, "position.highlight", new(Offset: new(modal.Index * OptionPitch, 0)));
        if (modal.Choices[0].Subject is { Hidden: false, CardId: { } id })
        {
            list.Field(part, "position.face.atk", 0, new(Texture: CardSpriteRenderer.FaceKey(id)));
            list.Field(part, "position.face.def", 0, new(Texture: CardSpriteRenderer.FaceKey(id)));
        }
        list.Add(new DuelRichText(part, DuelFieldResources.BasicKey, AtkOffset, null, 228.8f, 159.42f, 0.6f, 0.54f, Black));
        list.Add(new DuelRichText(part, DuelFieldResources.BasicKey, DefOffset, null, 340.8f, 159.42f, 0.6f, 0.54f, Black));
    }
}
