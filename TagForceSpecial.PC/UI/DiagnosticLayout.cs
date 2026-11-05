using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace TagForceSpecial.PC.UI;

/// <summary>Limited adapter for the diagnostic screen's absolute Gum layout anchors.
/// The existing pixel-font renderer remains in use; this is not a Gum Forms runtime.</summary>
public sealed class DiagnosticLayout
{
    private readonly Dictionary<string, string> variables;
    public DiagnosticLayout(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Missing diagnostic layout.");
        variables = root.Element("State")!.Elements("Variable").ToDictionary(
            v => (string)v.Attribute("Name")!, v => v.Element("Value")?.Value ?? "", StringComparer.Ordinal);
        foreach (string name in new[] { "Title", "ScreenLabel", "Summary", "Rows", "Source", "Footer" })
        {
            foreach (string unit in new[] { "XUnits", "YUnits", "WidthUnits", "HeightUnits", "XOrigin", "YOrigin" })
                if (variables.GetValueOrDefault(name + "." + unit) != "0")
                    throw new InvalidDataException("Diagnostic adapter supports absolute layout anchors only.");
            _ = Bounds(name);
        }
    }
    public Rectangle Bounds(string name)
    {
        int Value(string key) => checked((int)float.Parse(variables[name + "." + key], CultureInfo.InvariantCulture));
        var rect = new Rectangle(Value("X"), Value("Y"), Value("Width"), Value("Height"));
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > 480 || rect.Bottom > 272)
            throw new InvalidDataException("Diagnostic layout leaves the logical canvas.");
        return rect;
    }
    public string Text(string name) => variables[name + ".Text"];
}
