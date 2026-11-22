using Godot;

namespace TagForceSpecial.PC.Host;

/// <summary>Text elements of converted Gum scenes: Gum drew them with the Liberation Sans BMFont page generated for each font size (tracked
/// under res://Fonts with the SIL OFL licence), and PC notices fill a converted Notice container.</summary>
internal static class GumText
{
    private static readonly Dictionary<int, FontFile> Fonts = [];
    private static ImageTexture? white;

    /// <summary>Gives every converted Gum label its BMFont page for its size.</summary>
    public static void ApplyFonts(Node root)
    {
        foreach (var label in root.FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>())
        {
            if (!label.HasMeta("gum_font")) continue;
            int size = label.GetThemeFontSize("font_size");
            if (!Fonts.TryGetValue(size, out var font))
            {
                Fonts[size] = font = GD.Load<FontFile>($"res://Fonts/Font{size}LiberationSans-Regular_ttf_ba33345d.fnt")
                    ?? throw new InvalidOperationException($"No Liberation Sans page for size {size}.");
                // Gum's BMFont text used advances only: no kerning pairs.
                font.ClearKerningMap(0, size);
            }
            label.AddThemeFontOverride("font", font);
            // Gum stacked wrapped lines at the BMFont line height, with no extra spacing.
            label.AddThemeConstantOverride("line_spacing", 0);
        }
    }

    /// <summary>Shows the screen's PC notice with a title and body (its hint stays as authored), or hides it when <paramref name="title"/> is null.
    /// The shade is a tinted white pixel stretched over the notice, as the Gum shade sprite was.</summary>
    public static void ShowNotice(Control screen, string? title, string? body)
    {
        var notice = screen.GetNode<Control>("Notice");
        notice.Visible = title is not null;
        if (title is null) return;
        var shade = notice.GetNode<TextureRect>("NoticeShade");
        shade.Texture ??= white ??= ImageTexture.CreateFromImage(Image.CreateFromData(1, 1, false, Image.Format.Rgba8, [255, 255, 255, 255]));
        notice.GetNode<Label>("NoticeTitle").Text = title;
        notice.GetNode<Label>("NoticeBody").Text = body ?? "";
    }
}
