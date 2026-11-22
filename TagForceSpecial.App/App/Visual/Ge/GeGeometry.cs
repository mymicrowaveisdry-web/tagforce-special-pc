using System.Numerics;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Visual.Ge;

/// <summary>Clip-space helpers shared by the GE compilers.</summary>
public static class GeGeometry
{
    public const float Width = 480, Height = 272;

    /// <summary>A layer pixel position (y down) as clip space with depth 0 and w 1; the layer is 480 × 272 unless given.</summary>
    public static (float X, float Y) Screen(float x, float y, float width = Width, float height = Height) => (x / (width / 2) - 1, 1 - y / (height / 2));

    /// <summary>Row-vector transform, as XNA and System.Numerics compose matrices.</summary>
    public static Vector4 Transform(Vector3 position, Matrix4x4 matrix) => Vector4.Transform(new Vector4(position, 1), matrix);

    /// <summary>Removes triangles wound clockwise on screen (MonoGame CullClockwiseFace): negative signed area in y-up clip space after
    /// the perspective divide. Triangles touching w ≤ 0 are kept for the rasterizer to clip.</summary>
    public static GeVertex[] CullClockwise(GeVertex[] triangles)
    {
        var kept = new List<GeVertex>(triangles.Length);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            GeVertex a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (a.W > 0 && b.W > 0 && c.W > 0)
            {
                float ax = a.X / a.W, ay = a.Y / a.W, bx = b.X / b.W, by = b.Y / b.W, cx = c.X / c.W, cy = c.Y / c.W;
                if ((bx - ax) * (cy - ay) - (cx - ax) * (by - ay) < 0) continue;
            }
            kept.Add(a); kept.Add(b); kept.Add(c);
        }
        return kept.ToArray();
    }

    /// <summary>A 2D flow step as a GE step on a <paramref name="width"/> × <paramref name="height"/> layer: pixels to clip space, the flow's
    /// blend as GE factors (a mask writes stencil only), its stencil use, clamped sampling, no depth.</summary>
    public static GeCommand FromFlow(FlowCommand command, float width = Width, float height = Height)
    {
        if (command is FlowClearStencil) return new GeClear(false, false, true, 0, 0, 0, 0, 1);
        var batch = (FlowBatch)command;
        var blend = batch.Blend == FlowBlend.Additive
            ? new GeBlend(GeFactor.SrcAlpha, GeFactor.One, GeFactor.Zero, GeFactor.One)
            : new GeBlend(GeFactor.SrcAlpha, GeFactor.InvSrcAlpha, GeFactor.One, GeFactor.InvSrcAlpha);
        var stencil = batch.Stencil switch { FlowStencil.Write => GeStencil.Write, FlowStencil.Test => GeStencil.Test, _ => GeStencil.None };
        var vertices = Array.ConvertAll(batch.Triangles, v =>
        {
            var (x, y) = Screen(v.X, v.Y, width, height);
            return new GeVertex(x, y, 0, 1, v.R, v.G, v.B, v.A, v.U, v.V);
        });
        return new GeDraw(batch.Texture, new GeState(blend, false, GeCompare.Always, false, batch.Linear, true, true,
            Stencil: stencil, WriteColor: batch.Blend != FlowBlend.Mask), vertices, batch.Lines);
    }

    /// <summary>A flow layer as GE commands: cleared to transparent first, as the flow layers were.</summary>
    public static IReadOnlyList<GeCommand> FromFlows(IEnumerable<FlowCommand> commands, float width = Width, float height = Height) =>
        [new GeClear(true, true, true, 0, 0, 0, 0, 1), .. commands.Select(c => FromFlow(c, width, height))];

    /// <summary>MonoGame's float-to-byte colour conversion (truncating), so every host receives the colours the MonoGame host drew.</summary>
    public static byte Byte(float unit) => (byte)Math.Clamp(unit * 255, 0, 255);
}
