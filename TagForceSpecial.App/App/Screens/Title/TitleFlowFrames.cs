using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The measured title-flow frames (storage, data, name entry, questions, start menu, quit question) as host-neutral flow commands:
/// the frame's draws (with their stencil masks), the menu rows, the dialog buttons, the text runs and the typed name. Port of the MonoGame
/// host's flow layer.</summary>
public sealed class TitleFlowFrames(FlowProfile profile, MainMenuLayers menu, FlowTextSources text, PgfGlyphAtlas font, string fontKey)
{
    public const string QuitFrame = "main-menu.quit", ButtonKey = "title.eboot.dialog-button";
    private const string LevelFrame = "entry.level";
    private const int BarLeft = 114;
    private readonly Dictionary<string, FlowFrame> frames = profile.Frames.ToDictionary(f => f.Id, StringComparer.Ordinal);

    public bool Has(string frameId) => frames.ContainsKey(frameId);

    /// <summary>Imported and EBOOT textures the frames sample.</summary>
    public IEnumerable<string> TextureKeys => profile.Frames.SelectMany(f => f.Draws).Select(d => d.AssetKey).OfType<string>()
        .Append(MainMenuLayers.PanelKey).Append(ButtonKey).Distinct(StringComparer.Ordinal);

    /// <summary>One frame. <paramref name="menuBackgroundFrame"/> keeps the Main Menu background animating behind the quit question; without it
    /// the question shows its dump's own instant (exact comparisons).</summary>
    public IReadOnlyList<FlowCommand> Render(string frameId, int buttonFocus = 0, int menuFocus = 0, string? name = null,
        DuelistLevelChoice level = DuelistLevelChoice.FirstTimer, int? menuBackgroundFrame = null)
    {
        var frame = frames[frameId];
        var commands = new List<FlowCommand>();
        void Draw(FlowDraw draw) { if (FlowDrawCompiler.Compile(draw) is { } c) commands.Add(c); }
        void Line(string value, float x, float y, float sx, float sy, int color, int shadow, float[]? measured = null) =>
            commands.AddRange(FlowTextLayout.Line(font, fontKey, value, x, y, sx, sy, color, shadow, measured));
        void Measured(string value, FlowText run) => Line(value, run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha,
            run.X.Length == value.Length ? run.X : null);

        bool overMenu = frameId == QuitFrame;
        if (overMenu) EndGameBehindDialog(frame, menuBackgroundFrame, commands, Draw, Measured);
        else foreach (var draw in frame.Draws) Draw(draw);
        if (frame.Menu is { } rows)
            for (int i = 0; i < rows.Rows.Length; i++)
            {
                var row = rows.Rows[i];
                foreach (var q in rows.Plain) commands.Add(FlowDrawCompiler.Quad(MainMenuLayers.PanelKey, q, 0, row.Y, false));
                bool focused = i == menuFocus;
                if (focused) foreach (var q in rows.Focus) commands.Add(FlowDrawCompiler.Quad(MainMenuLayers.PanelKey, q, 0, row.Y, false));
                // Measured: focused rows use black text with a light shadow; others white with a heavier one.
                Line(text.Read(row.Label), row.Label.X[0], row.Label.Y, row.Label.ScaleX, row.Label.ScaleY, focused ? 0 : 0xffffff,
                    focused ? 0x40 : 0x80, row.Label.X);
            }
        if (frame.Buttons is { } buttons)
            for (int i = 0; i < buttons.Length; i++)
            {
                float x = buttons[i].Rect[0], y = buttons[i].Rect[1];
                if (i == buttonFocus) foreach (var q in profile.Button.Focus) commands.Add(FlowDrawCompiler.Quad(ButtonKey, q, x, y, false));
                else commands.Add(FlowDrawCompiler.Quad(ButtonKey, profile.Button.Plain, x, y, false));
                Measured(text.Read(buttons[i].Label), buttons[i].Label);
            }
        foreach (var run in frame.Text)
        {
            if (overMenu && IsMenuLabel(run)) continue;
            string value = text.Read(run);
            // Measures the level sentence once; its variants differ only in the bracketed name.
            if (frameId == LevelFrame && value.Contains('<'))
            {
                int open = value.IndexOf('<');
                Line(value[..(open + 1)] + text.Read(profile.LevelNames[(int)level]) + ">", run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);
                continue;
            }
            Measured(value, run);
        }
        if (frame.Name is { } typed && name is not null) Line(name, typed.X, typed.Y, typed.ScaleX, typed.ScaleY, typed.Color, typed.ShadowAlpha);
        return commands;
    }

    /// <summary>Measured draw order for the end-game question: background, bars, their labels, then the dialog over them.</summary>
    private void EndGameBehindDialog(FlowFrame frame, int? menuBackgroundFrame, List<FlowCommand> commands, Action<FlowDraw> draw,
        Action<string, FlowText> measured)
    {
        int background = menu.BackgroundDrawCount;
        if (menuBackgroundFrame is { } gameFrame) commands.AddRange(menu.Background(gameFrame));
        else for (int i = 0; i < background; i++) draw(frame.Draws[i]);
        int dialog = Array.FindIndex(frame.Draws, background, d => d.Points.Length > 0 && d.Points.Min(p => p[0]) < BarLeft / 2f);
        if (dialog < 0) throw new InvalidDataException("End-game frame has no dialog panel.");
        for (int i = background; i < dialog; i++) draw(frame.Draws[i]);
        foreach (var label in frame.Text.Where(IsMenuLabel)) measured(text.Read(label), label);
        for (int i = dialog; i < frame.Draws.Length; i++) draw(frame.Draws[i]);
    }

    /// <summary>Menu rows start inside the bars; the dialog's own lines start at its left margin.</summary>
    private static bool IsMenuLabel(FlowText run) => run.X[0] > BarLeft;
}
