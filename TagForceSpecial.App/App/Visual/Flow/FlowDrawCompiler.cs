using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Visual.Flow;

/// <summary>Turns measured GE draws and quads into host-neutral triangle batches, with the material rules the MonoGame host applied.</summary>
public static class FlowDrawCompiler
{
    /// <summary>The batch for one measured draw, a stencil clear, or null when the draw has nothing to rasterize.</summary>
    public static FlowCommand? Compile(FlowDraw draw)
    {
        if (draw.ClearStencil) return new FlowClearStencil();
        bool lines = draw.Primitive == 2;
        if (lines ? draw.Points.Length < 2 : draw.Points.Length < 3 || draw.Primitive is not (3 or 4 or 5)) return null;
        var blend = draw.WriteMask ? FlowBlend.Mask : draw.Additive ? FlowBlend.Additive : FlowBlend.Alpha;
        var stencil = draw.WriteMask ? FlowStencil.Write : draw.Stencil ? FlowStencil.Test : FlowStencil.None;
        FlowTexture? texture = draw.AssetKey is not { } key ? null
            : draw.Add is { } add ? new(key, new(draw.Color[0], draw.Color[1], draw.Color[2], add[0], add[1], add[2])) : new(key);
        // A baked specular texture already holds tex x colour + specular; only alpha still modulates.
        var (r, g, b, a) = draw.Add is not null && draw.AssetKey is not null
            ? (255, 255, 255, draw.Color[3]) : (draw.Color[0], draw.Color[1], draw.Color[2], draw.Color[3]);
        var input = new FlowVertex[draw.Points.Length];
        for (int i = 0; i < input.Length; i++)
        {
            var (vr, vg, vb, va) = (r, g, b, a);
            if (draw.VertexColors is { } colors) { uint c = colors[i]; (vr, vg, vb, va) = ((int)(c & 255), (int)(c >> 8 & 255), (int)(c >> 16 & 255), (int)(c >> 24 & 255)); }
            float u = draw.Uv.Length == 0 ? 0 : draw.Uv[i][0], v = draw.Uv.Length == 0 ? 0 : draw.Uv[i][1];
            input[i] = new(draw.Points[i][0], draw.Points[i][1], Byte(vr), Byte(vg), Byte(vb), Byte(va), u, v);
        }
        if (lines) return new FlowBatch(texture, blend, stencil, draw.LinearSampling, Segments(input), Lines: true);
        return new FlowBatch(texture, blend, stencil, draw.LinearSampling, draw.Primitive == 3 ? input : Expand(input, draw.Primitive));
    }

    /// <summary>A line strip (2) as a line list: each vertex joined to the next.</summary>
    public static FlowVertex[] Segments(FlowVertex[] strip)
    {
        var output = new FlowVertex[(strip.Length - 1) * 2];
        for (int i = 0; i + 1 < strip.Length; i++) { output[i * 2] = strip[i]; output[i * 2 + 1] = strip[i + 1]; }
        return output;
    }

    /// <summary>A textured quad offset by (<paramref name="dx"/>, <paramref name="dy"/>), tinted by <paramref name="tint"/> or the quad's own colour.</summary>
    public static FlowBatch Quad(string textureKey, FlowQuad q, float dx, float dy, bool linear, (int R, int G, int B, int A)? tint = null)
    {
        var (r, g, b, a) = tint ?? (q.Color[0], q.Color[1], q.Color[2], q.Color[3]);
        float x0 = q.X0 + dx, x1 = q.X1 + dx, y0 = q.Y0 + dy, y1 = q.Y1 + dy;
        FlowVertex V(float x, float y, float u, float v) => new(x, y, Byte(r), Byte(g), Byte(b), Byte(a), u, v);
        return new(new(textureKey), q.Additive ? FlowBlend.Additive : FlowBlend.Alpha, FlowStencil.None, linear,
            [V(x0, y0, q.U0, q.V0), V(x1, y0, q.U1, q.V0), V(x0, y1, q.U0, q.V1), V(x0, y1, q.U0, q.V1), V(x1, y0, q.U1, q.V0), V(x1, y1, q.U1, q.V1)]);
    }

    /// <summary>Strips (4) and fans (5) as triangle lists, in the GE's vertex order.</summary>
    public static FlowVertex[] Expand(FlowVertex[] input, int primitive)
    {
        var output = new List<FlowVertex>((input.Length - 2) * 3);
        for (int i = 2; i < input.Length; i++)
        {
            output.Add(primitive == 5 ? input[0] : input[i - 2]);
            output.Add(input[i - 1]); output.Add(input[i]);
        }
        return output.ToArray();
    }

    private static byte Byte(int value) => (byte)Math.Clamp(value, 0, 255);
}
