namespace TagForceSpecial.PC.App.Visual.Flow;

/// <summary>Frame-buffer blending of a measured draw. <see cref="Alpha"/>: colour SrcAlpha/InvSrcAlpha, alpha One/InvSrcAlpha (the layer
/// ends up premultiplied). <see cref="Additive"/>: colour SrcAlpha/One, destination alpha kept. <see cref="Mask"/>: writes no colour.</summary>
public enum FlowBlend { Alpha, Additive, Mask }

/// <summary>Stencil use of a measured draw: write the reference where drawn, or draw only where the reference was written.</summary>
public enum FlowStencil { None, Write, Test }

/// <summary>GE separate specular baked into a texture: texture × primary + specular per channel, clamped, texture alpha kept.</summary>
public readonly record struct FlowSpecular(int R, int G, int B, int AddR, int AddG, int AddB);

/// <summary>A texture a batch samples, by asset key, optionally baked with <see cref="FlowSpecular"/>.</summary>
public readonly record struct FlowTexture(string Key, FlowSpecular? Specular = null)
{
    /// <summary>One identity per distinct pixel content, so a host bakes and uploads each combination once.</summary>
    public string Identity => Specular is { } s ? $"{Key}|{s.R},{s.G},{s.B}|{s.AddR},{s.AddG},{s.AddB}" : Key;
}

/// <summary>A vertex in canvas pixels with a straight-alpha colour and normalized texture coordinates.</summary>
public readonly record struct FlowVertex(float X, float Y, byte R, byte G, byte B, byte A, float U, float V);

/// <summary>One step of a flow layer, drawn in list order.</summary>
public abstract record FlowCommand;

/// <summary>Clears the layer's stencil to zero.</summary>
public sealed record FlowClearStencil : FlowCommand;

/// <summary>A triangle list (or, with <see cref="Lines"/>, a line list) with one texture (or none: vertex colour only), blend, stencil use
/// and sampling (point unless linear).</summary>
public sealed record FlowBatch(FlowTexture? Texture, FlowBlend Blend, FlowStencil Stencil, bool Linear, FlowVertex[] Triangles, bool Lines = false) : FlowCommand;
