namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>An original message read from a stored archive at <paramref name="Offset"/>, with "%s" replaced by
/// <paramref name="Argument"/>, drawn as colour runs split at its "@n" codes. <paramref name="Palette"/> maps each code to a 0xBBGGRR
/// colour and its shadow alpha; text before the first code uses code 0. A positive <paramref name="WrapWidth"/> wraps by words onto lines
/// <paramref name="LinePitch"/> apart.</summary>
public sealed record DuelRichText(DuelPart Part, string Source, int Offset, string? Argument, float X, float Y, float ScaleX, float ScaleY,
    IReadOnlyDictionary<int, (int Color, int Shadow)> Palette, float WrapWidth = 0, float LinePitch = 0) : DuelDraw(Part);

public static class DuelRichTextLayout
{
    /// <summary>Splits a message into (code, text) runs at "@digit" markers.</summary>
    public static IReadOnlyList<(int Code, string Text)> Runs(string message)
    {
        var runs = new List<(int, string)>();
        int code = 0, start = 0;
        for (int i = 0; i + 1 < message.Length; i++)
        {
            if (message[i] != '@' || !char.IsAsciiDigit(message[i + 1])) continue;
            if (i > start) runs.Add((code, message[start..i]));
            code = message[i + 1] - '0';
            start = i + 2;
            i++;
        }
        if (start < message.Length) runs.Add((code, message[start..]));
        return runs;
    }

    /// <summary>Words with their trailing spaces, keeping each word's colour code.</summary>
    private static IEnumerable<(int Code, string Text)> Words(IEnumerable<(int Code, string Text)> runs)
    {
        foreach (var (code, text) in runs)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ' && (i + 1 == text.Length || text[i + 1] != ' '))
                {
                    yield return (code, text[start..(i + 1)]);
                    start = i + 1;
                }
            if (start < text.Length) yield return (code, text[start..]);
        }
    }

    /// <summary>The message after "%s" (or a count's "%d") substitution.</summary>
    public static string Resolve(DuelRichText text, string message) => text.Argument is { } argument
        ? message.Replace("%s", argument, StringComparison.Ordinal).Replace("%d", argument, StringComparison.Ordinal) : message;

    /// <summary>Text commands in reading order. Without wrapping, one command per run; with wrapping, a word moves to the next line when its
    /// visible part would pass <see cref="DuelRichText.WrapWidth"/> (its trailing space stays on the line it ends).</summary>
    public static IEnumerable<DuelFieldText> Expand(DuelRichText text, string message, Func<string, float, float> measure) =>
        Lines(text, message, measure).SelectMany(line => line.Commands);

    public static IReadOnlyList<(float Width, IReadOnlyList<DuelFieldText> Commands)> Lines(DuelRichText text, string message,
        Func<string, float, float> measure)
    {
        var runs = Runs(Resolve(text, message));
        var lines = new List<(float, IReadOnlyList<DuelFieldText>)>();
        var current = new List<DuelFieldText>();
        float pen = 0, width = 0, y = text.Y;
        DuelFieldText Command(int code, string run, float x)
        {
            var (color, shadow) = text.Palette.TryGetValue(code, out var style) ? style : text.Palette[0];
            return new DuelFieldText(text.Part, run, x, y, text.ScaleX, text.ScaleY, color, shadow);
        }
        foreach (var (code, word) in text.WrapWidth > 0 ? Words(runs) : runs)
        {
            if (text.WrapWidth > 0 && pen > 0 && pen + measure(word.TrimEnd(), text.ScaleX) > text.WrapWidth)
            {
                lines.Add((width, current));
                current = [];
                pen = width = 0;
                y += text.LinePitch;
            }
            current.Add(Command(code, word, text.X + pen));
            width = pen + measure(word.TrimEnd(), text.ScaleX);
            pen += measure(word, text.ScaleX);
        }
        if (current.Count > 0) lines.Add((width, current));
        return lines;
    }
}
