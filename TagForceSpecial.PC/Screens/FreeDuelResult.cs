using Microsoft.Xna.Framework;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.Screens;

public enum FreeDuelResultAction { None, Rematch, Return, SaveAndReturn }

public sealed class FreeDuelResultSession
{
    private static readonly IReadOnlyList<string> ResultRows = Array.AsReadOnly(new[] { "Play again", "Return to main menu" });
    private static readonly IReadOnlyList<string> SaveRows = Array.AsReadOnly(new[] { "Return without saving", "Save profile and return" });
    private bool savePrompt;

    public int Focus { get; private set; }
    public string? Notice { get; set; }
    public IReadOnlyList<string> Rows => savePrompt ? SaveRows : ResultRows;

    public void PointAt(int row)
    {
        if (row >= 0 && row < Rows.Count) Focus = row;
    }

    public FreeDuelResultAction Handle(GuiButtons buttons)
    {
        if (buttons.Back)
        {
            SetSavePrompt(!savePrompt);
            return FreeDuelResultAction.None;
        }
        if (buttons.Up || buttons.Down) Focus = 1 - Focus;
        if (!buttons.Confirm) return FreeDuelResultAction.None;
        if (savePrompt) return Focus == 0 ? FreeDuelResultAction.Return : FreeDuelResultAction.SaveAndReturn;
        if (Focus == 0) return FreeDuelResultAction.Rematch;

        SetSavePrompt(true);
        return FreeDuelResultAction.None;
    }

    private void SetSavePrompt(bool open)
    {
        savePrompt = open;
        Focus = open ? 0 : 1;
        Notice = null;
    }
}

internal static class FreeDuelResultScreen
{
    private const int RowX = 54, RowY = 156, RowPitch = 27;

    public static int? HitRow(CanvasPoint? position)
    {
        if (position is not { X: >= 48 and < 432 } point) return null;
        for (int row = 0; row < 2; row++)
        {
            int top = RowY + RowPitch * row - 4;
            if (point.Y >= top && point.Y < top + 17) return row;
        }
        return null;
    }

    public static void Draw(SpriteRenderer renderer, DuelView view, FreeDuelResultSession session)
    {
        var accent = new Color(104, 218, 214);
        var muted = new Color(143, 162, 187);
        renderer.Fill(new(38, 29, 404, 216), new Color(18, 27, 43), SceneLayer.Background);
        renderer.Text(view.Outcome is null ? "Duel ended early" : view.Prompt, 54, 53, accent, 2);
        renderer.Text($"Turn {view.Turn}   You {view.Self.LifePoints} LP   Opponent {view.Opponent.LifePoints} LP", 54, 92, muted);
        for (int i = 0; i < session.Rows.Count; i++)
        {
            int y = RowY + i * RowPitch;
            if (i == session.Focus) renderer.Fill(new(48, y - 4, 384, 17), new Color(38, 61, 77));
            renderer.Text(session.Rows[i], RowX, y, i == session.Focus ? accent : Color.White);
        }
        if (session.Notice is { } notice) renderer.Text(notice, 54, 218, muted);
    }
}
