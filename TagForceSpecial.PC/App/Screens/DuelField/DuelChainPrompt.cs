using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>An original YES (0) / NO (1) prompt with <paramref name="Selected"/> highlighted: by default the Chain prompt for
/// <paramref name="CardName"/>'s activation with the eligible response cards' faces; <see cref="Layout"/> selects another message.</summary>
public sealed record DuelChainPrompt(DuelPart Part, string CardName, IReadOnlyList<uint> Responses, int Selected) : DuelDraw(Part)
{
    public DuelPromptLayout Layout { get; init; } = DuelPromptLayout.Chain;
}

/// <summary>A YES/NO prompt's message (cardinfo offset, "%s"/"%d" argument) and vertical layout: window from <paramref name="Top"/> to
/// <paramref name="Bottom"/>, buttons moved down by <paramref name="ButtonDy"/> from the Chain prompt's.</summary>
public sealed record DuelPromptLayout(int MessageOffset, float Top, float Bottom, float ButtonDy)
{
    /// <summary>'"@3%s@0" is @3activated@0. @3Chain@0 another card or effect?' (Scapegoat and Jar of Greed captures).</summary>
    public static DuelPromptLayout Chain { get; } = new(1156968, 71, 194, 0);

    /// <summary>Measured (Tribute Summon of one monster, order32-win-5 s2): "You must @3Tribute@0 @21@0 monster in order to @3Tribute
    /// Summon@0 this @3monster@0. @3Tribute Summon@0?" in a window from y 85 to 180, buttons at y 144; the plural message (offset 1188438,
    /// "%d") uses the same layout (inferred).</summary>
    public static DuelPromptLayout Tribute(int count) => new(count == 1 ? 1188230 : 1188438, 85, 180, 26);
}

/// <summary>Measured Chain prompt (Scapegoat and Jar of Greed captures): after a depth clear, a window04 window from y 71 to 194 centred on
/// x 314 with half-width ⌈first line / 2 + 15⌉; the cardinfo message (black, names and keywords pink, numbers teal, shadow 64) from
/// (+9.8, +9.42), wrapped by words at 240 pixels (inferred between the two captures' bounds) with 11.72-pixel lines; 20 × 30 response faces
/// from (304, 154); YES (244, 118) and NO (324, 118) buttons with white labels at (259.5 / 342.5, 122.42), 0.5 × 0.54. The Tribute prompt
/// keeps the centre, text origin and buttons, moved per <see cref="DuelPromptLayout"/>.</summary>
public static class DuelChainPromptWindow
{
    public const int YesOffset = 401662, NoOffset = 401098;
    public const float CentreX = 314, WrapWidth = 240, LinePitch = 11.72f, ButtonPitch = 80;
    /// <summary>Response faces: one was captured at x 304; several are spaced 24 apart around the centre (inferred).</summary>
    public const float ThumbPitch = 24;
    private static readonly Dictionary<int, (int, int)> Palette = new() { [0] = (0x000000, 64), [2] = (0x7f9900, 64), [3] = (0x6400c4, 64) };

    public static IEnumerable<DuelDraw> Expand(DuelChainPrompt prompt, DuelFieldProfile field, Func<string, float, float> measure,
        Func<string, int, string> message)
    {
        var layout = prompt.Layout;
        var text = new DuelRichText(prompt.Part, CardAlbumResources.CardInfoKey, layout.MessageOffset, prompt.CardName, 0, layout.Top + 9.42f,
            0.6f, 0.54f, Palette, WrapWidth, LinePitch);
        var lines = DuelRichTextLayout.Lines(text, message(text.Source, text.Offset), measure);
        float half = MathF.Ceiling(lines[0].Width / 2 + 15), x0 = CentreX - half;
        yield return new DuelFieldDraw(prompt.Part, "chain.clear", 0);
        yield return DuelMenuWindow.Frame(prompt.Part, field, "chain.window", x0, layout.Top, CentreX + half, layout.Bottom, null);
        foreach (var command in lines.SelectMany(l => l.Commands)) yield return command with { X = command.X + x0 + 9.8f };
        var thumb = field.Piece("chain.thumb")[0].Vertices![0];
        float first = CentreX - 10 - (prompt.Responses.Count - 1) * ThumbPitch / 2;
        for (int i = 0; i < prompt.Responses.Count; i++)
            yield return new DuelFieldDraw(prompt.Part, "chain.thumb", 0,
                new(Offset: new(first + i * ThumbPitch - thumb[0], 0), Texture: CardSpriteRenderer.FaceKey(prompt.Responses[i])));
        foreach (var draw in Button(prompt, field, 0)) yield return draw;
        foreach (var draw in Button(prompt, field, 1)) yield return draw;
    }

    /// <summary>The captured YES draws are the selected look (base, overlay, additive glow), the NO draw the plain look; each is moved to
    /// whichever button needs it.</summary>
    private static IEnumerable<DuelDraw> Button(DuelChainPrompt prompt, DuelFieldProfile field, int button)
    {
        bool selected = prompt.Selected == button;
        string piece = selected ? "chain.yes" : "chain.no";
        float offset = button * ButtonPitch - (selected ? 0 : ButtonPitch), dy = prompt.Layout.ButtonDy;
        for (int i = 0; i < field.Piece(piece).Length; i++) yield return new DuelFieldDraw(prompt.Part, piece, i, new(Offset: new(offset, dy)));
        yield return new DuelRichText(prompt.Part, DuelFieldResources.BasicKey, button == 0 ? YesOffset : NoOffset, null,
            button == 0 ? 259.5f : 342.5f, 122.42f + dy, 0.5f, 0.54f, new Dictionary<int, (int, int)> { [0] = (0xffffff, 128) });
    }
}
