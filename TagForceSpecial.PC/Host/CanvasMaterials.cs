using Godot;

namespace TagForceSpecial.PC.Host;

/// <summary>Canvas materials for showing rendered layers.</summary>
internal static class CanvasMaterials
{
    /// <summary>For layers whose alpha blend wrote One/InvSrcAlpha into alpha (flow layers, the game canvas): their colour is premultiplied.</summary>
    public static CanvasItemMaterial Premultiplied { get; } = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
}
