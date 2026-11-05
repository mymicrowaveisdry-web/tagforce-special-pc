using Microsoft.Xna.Framework;

namespace TagForceSpecial.PC.App.Visual;

public sealed record OriginalLayout(string ScreenId, Rectangle Bounds, string? EvidenceId, NineSliceCaps? Caps = null)
{
    public bool Pending => string.IsNullOrWhiteSpace(EvidenceId);
}

/// <summary>Pending entries are editor scaffolds, never reference measurements.</summary>
public sealed class OriginalLayoutRegistry
{
    private readonly Dictionary<string, OriginalLayout> layouts = new(StringComparer.Ordinal);
    public void Register(string key, OriginalLayout layout)
    {
        if (layout.Bounds.X < 0 || layout.Bounds.Y < 0 || layout.Bounds.Width <= 0 || layout.Bounds.Height <= 0 ||
            layout.Bounds.Right > 480 || layout.Bounds.Bottom > 272)
            throw new InvalidDataException("Layout leaves the reference canvas.");
        layouts.Add(key, layout);
    }
    public OriginalLayout Get(string key, bool requireMeasured = true)
    {
        var layout = layouts[key];
        if (requireMeasured && layout.Pending) throw new InvalidDataException("Original layout measurement pending: " + key);
        return layout;
    }
}
