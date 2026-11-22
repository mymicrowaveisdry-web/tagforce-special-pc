using System.Text;
using System.Text.RegularExpressions;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record FlowQuad(float X0, float Y0, float X1, float Y1, float U0, float V0, float U1, float V1, int[] Color, bool Additive);
public sealed record FlowTextSource(string Source, int Offset, int Start, int Chars);
public sealed record FlowText(string Source, int Offset, int Start, int Chars, float[] X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record FlowName(float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record FlowDraw(string? AssetKey, int Primitive, float[][] Points, float[][] Uv, int[] Color, uint[]? VertexColors, int[]? Add,
    bool Additive, bool LinearSampling, bool ClearStencil, bool WriteMask, bool Stencil);
public sealed record FlowButton(float[] Rect, FlowText Label);
public sealed record FlowMenuRow(float Y, FlowText Label);
public sealed record FlowMenu(FlowQuad[] Plain, FlowQuad[] Focus, FlowMenuRow[] Rows);
public sealed record FlowFrame(string Id, string DumpSha256, FlowDraw[] Draws, FlowText[] Text, FlowButton[]? Buttons, FlowMenu? Menu, FlowName? Name);
public sealed record FlowEbootTexture(string Key, int Offset, int Length, int ClutOffset, int ClutBytes, int Width, int Height, int Bpp, int Stride, string RgbaSha256);
public sealed record FlowButtonStyle(FlowQuad[] Focus, FlowQuad Plain);
public sealed record FlowProfile(int SchemaVersion, string BuildId, string Scope, FlowButtonStyle Button, FlowTextSource[] LevelNames,
    FlowEbootTexture[] EbootTextures, FlowFrame[] Frames);

/// <summary>Reads prompt text from pinned source bytes. Original strings stay in the private cache, never in Git.</summary>
public static partial class TitleFlowText
{
    [GeneratedRegex(@"\$C\d", RegexOptions.CultureInvariant)] private static partial Regex ColorCode();
    /// <summary>Same transform the exporter measured: colour codes and line breaks removed; %c is the trademark sign.</summary>
    public static string Normalize(string message) => ColorCode().Replace(message, "").Replace("\n", "").Replace("%c", "™");
    public static string Read(byte[] source, int offset, int start, int chars)
    {
        if (offset < 0 || offset >= source.Length || start < 0 || chars <= 0) throw new InvalidDataException("Invalid text reference.");
        int end = offset;
        while (end + 1 < source.Length && (source[end] != 0 || source[end + 1] != 0)) end += 2;
        string message = Normalize(Encoding.Unicode.GetString(source, offset, end - offset));
        if (start + chars > message.Length) throw new InvalidDataException("Text reference exceeds source message.");
        return message.Substring(start, chars);
    }
}
