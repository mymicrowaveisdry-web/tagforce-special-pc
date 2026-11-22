using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Visual.Ge;

/// <summary>Blend factors of the PSP GE as the MonoGame host mapped them; <see cref="Constant"/> is the draw's fixed colour.</summary>
public enum GeFactor { Zero, One, SrcColor, InvSrcColor, SrcAlpha, InvSrcAlpha, DstColor, InvDstColor, DstAlpha, InvDstAlpha, Constant }

/// <summary>Depth comparisons in the layer's 0 (near) .. 1 (far) depth, after each source's remap of GE depth.</summary>
public enum GeCompare { Never, Always, Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual }

/// <summary>Frame-buffer blending: colour and alpha factors (always added), the constant colour, and whether alpha is written.</summary>
public readonly record struct GeBlend(GeFactor ColorSource, GeFactor ColorDestination, GeFactor AlphaSource, GeFactor AlphaDestination,
    byte ConstantR = 0, byte ConstantG = 0, byte ConstantB = 0, bool WriteAlpha = true)
{
    /// <summary>Straight alpha over (MonoGame NonPremultiplied: alpha also scaled by source alpha).</summary>
    public static GeBlend NonPremultiplied => new(GeFactor.SrcAlpha, GeFactor.InvSrcAlpha, GeFactor.SrcAlpha, GeFactor.InvSrcAlpha);
}

/// <summary>Stencil use: write the reference (255) wherever drawn, or draw only where it was written. Cleared to 0.</summary>
public enum GeStencil { None, Write, Test }

/// <summary>Rasterizer state of one draw. A null <see cref="Blend"/> writes the source as is (opaque). <see cref="WriteColor"/> false draws
/// into the stencil only (a mask).</summary>
public sealed record GeState(GeBlend? Blend, bool DepthTest, GeCompare DepthCompare, bool DepthWrite, bool Linear, bool ClampU, bool ClampV,
    bool TextureAlpha = true, GeStencil Stencil = GeStencil.None, bool WriteColor = true);

/// <summary>A vertex in clip space (MonoGame convention: y up, depth 0 near .. 1 far after the divide) with a straight-alpha colour.</summary>
public readonly record struct GeVertex(float X, float Y, float Z, float W, byte R, byte G, byte B, byte A, float U, float V);

/// <summary>One step of a GE layer, drawn in list order.</summary>
public abstract record GeCommand;

/// <summary>Clears colour (to the given RGBA), depth (to <see cref="Depth"/>) and stencil, each when requested.</summary>
public sealed record GeClear(bool ClearColor, bool ClearDepth, bool ClearStencil, byte R, byte G, byte B, byte A, float Depth) : GeCommand;

/// <summary>A triangle list (back faces already removed), or with <see cref="Lines"/> a line list, sampling <see cref="Texture"/> (or vertex
/// colour only).</summary>
public sealed record GeDraw(FlowTexture? Texture, GeState State, GeVertex[] Triangles, bool Lines = false) : GeCommand;
