using Godot;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.Host;

/// <summary>The release renderer for every original layer. Rasterizes GE commands on Godot's RenderingDevice into an RGBA8 target with a
/// depth/stencil buffer (24-bit depth and 8-bit stencil, as the MonoGame host used, else 32-bit float depth), in submission order: one
/// pipeline per blend/depth/stencil/colour-write state, blend constants for fixed colours, perspective-correct interpolation, clears that
/// split the draw list. Blending happens in the 8-bit (gamma-space) target, as on the GE. Show <see cref="Texture"/> (premultiplied when the
/// commands came from flow layers); read with <see cref="Capture"/>. Device work runs on the render thread.</summary>
internal sealed class GeLayer : IDisposable
{
    private const int Stride = 28, StencilReference = 255;
    private const string VertexSource = """
        #version 450
        layout(location = 0) in vec4 clip;
        layout(location = 1) in vec4 color;
        layout(location = 2) in vec2 uv;
        layout(location = 0) out vec4 v_color;
        layout(location = 1) out vec2 v_uv;
        void main() { v_color = color; v_uv = uv; gl_Position = vec4(clip.x, -clip.y, clip.z, clip.w); }
        """;
    private const string FragmentSource = """
        #version 450
        layout(location = 0) in vec4 v_color;
        layout(location = 1) in vec2 v_uv;
        layout(location = 0) out vec4 frag;
        layout(set = 0, binding = 0) uniform sampler2D tex;
        layout(push_constant, std430) uniform Params { vec4 flags; } params;
        void main()
        {
            vec4 t = params.flags.x > 0.5 ? texture(tex, v_uv) : vec4(1.0);
            if (params.flags.y < 0.5) t.a = 1.0;
            frag = v_color * t;
        }
        """;

    /// <summary>Straight-alpha pixels for a texture, or null when the host cannot supply it (the draw is skipped, as a missing face was).</summary>
    public delegate (byte[] Rgba, int Width, int Height)? PixelSource(FlowTexture texture);

    private readonly PixelSource pixels;
    private readonly Dictionary<string, Rid?> textures = new(StringComparer.Ordinal);
    private readonly Dictionary<(bool, bool, bool), Rid> samplers = [];
    private readonly Dictionary<(Rid, Rid), Rid> uniformSets = [];
    private readonly Dictionary<(GeBlend?, bool, GeCompare, bool, GeStencil, bool), Rid> pipelines = [];
    private readonly List<Rid> transient = [];
    private RenderingDevice rd = null!;
    private Rid color, depth, framebuffer, shader, white;
    private long framebufferFormat, vertexFormat;

    public int Width { get; }
    public int Height { get; }
    public Texture2Drd Texture { get; } = new();

    public GeLayer(PixelSource pixels, int width = 480, int height = 272)
    {
        this.pixels = pixels;
        Width = width; Height = height;
        RenderingServer.CallOnRenderThread(Callable.From(Create));
    }

    /// <summary>Replaces the layer with <paramref name="commands"/>; drawn on the render thread before the next frame.</summary>
    public void Render(IReadOnlyList<GeCommand> commands) => RenderingServer.CallOnRenderThread(Callable.From(() => Draw(commands)));

    /// <summary>Forgets an uploaded texture whose pixels changed (a growing glyph atlas); it is uploaded again when next drawn.</summary>
    public void Invalidate(string identity) => RenderingServer.CallOnRenderThread(Callable.From(() =>
    {
        if (!textures.Remove(identity, out var rid) || rid is not { } texture) return;
        foreach (var key in uniformSets.Keys.Where(k => k.Item1 == texture).ToList()) { rd.FreeRid(uniformSets[key]); uniformSets.Remove(key); }
        rd.FreeRid(texture);
    }));

    /// <summary>The layer's RGBA pixels as rendered.</summary>
    public Image Capture()
    {
        Image? image = null;
        RenderingServer.CallOnRenderThread(Callable.From(() => image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, rd.TextureGetData(color, 0))));
        RenderingServer.ForceSync();
        return image ?? throw new InvalidOperationException("The GE layer could not be read back.");
    }

    private void Create()
    {
        rd = RenderingServer.GetRenderingDevice() ?? throw new NotSupportedException("The GE layer needs a RenderingDevice renderer (Forward+ or Mobile).");
        color = rd.TextureCreate(new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)Width, Height = (uint)Height,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.ColorAttachmentBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanUpdateBit
        }, new RDTextureView(), []);
        var depthFormat = rd.TextureIsFormatSupportedForUsage(RenderingDevice.DataFormat.D24UnormS8Uint, RenderingDevice.TextureUsageBits.DepthStencilAttachmentBit)
            ? RenderingDevice.DataFormat.D24UnormS8Uint : RenderingDevice.DataFormat.D32SfloatS8Uint;
        depth = rd.TextureCreate(new RDTextureFormat
        {
            Format = depthFormat, Width = (uint)Width, Height = (uint)Height, UsageBits = RenderingDevice.TextureUsageBits.DepthStencilAttachmentBit
        }, new RDTextureView(), []);
        framebuffer = rd.FramebufferCreate([color, depth]);
        framebufferFormat = rd.FramebufferGetFormat(framebuffer);
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { SourceVertex = VertexSource, SourceFragment = FragmentSource });
        if (!string.IsNullOrEmpty(spirv.CompileErrorVertex) || !string.IsNullOrEmpty(spirv.CompileErrorFragment))
            throw new InvalidOperationException("GE shader: " + spirv.CompileErrorVertex + spirv.CompileErrorFragment);
        shader = rd.ShaderCreateFromSpirV(spirv, "GeLayer");
        vertexFormat = rd.VertexFormatCreate([
            new RDVertexAttribute { Location = 0, Offset = 0, Stride = Stride, Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat },
            new RDVertexAttribute { Location = 1, Offset = 16, Stride = Stride, Format = RenderingDevice.DataFormat.R8G8B8A8Unorm },
            new RDVertexAttribute { Location = 2, Offset = 20, Stride = Stride, Format = RenderingDevice.DataFormat.R32G32Sfloat }]);
        white = Upload([255, 255, 255, 255], 1, 1);
        Texture.TextureRdRid = color;
    }

    private void Draw(IReadOnlyList<GeCommand> commands)
    {
        FreeTransient();
        // One vertex buffer for the frame; each draw reads its own range.
        var draws = commands.OfType<GeDraw>().Where(d => d.Triangles.Length > 0).ToList();
        var offsets = new Dictionary<GeDraw, long>(ReferenceEqualityComparer.Instance);
        int total = draws.Sum(d => d.Triangles.Length);
        Rid buffer = default;
        if (total > 0)
        {
            var data = new byte[total * Stride];
            int at = 0;
            foreach (var d in draws) { offsets[d] = (long)at * Stride; Write(data, at, d.Triangles); at += d.Triangles.Length; }
            buffer = rd.VertexBufferCreate((uint)data.Length, data);
            transient.Add(buffer);
        }
        long list = -1;
        foreach (var command in commands)
        {
            if (command is GeClear clear)
            {
                if (list >= 0) rd.DrawListEnd();
                var flags = (clear.ClearColor ? RenderingDevice.DrawFlags.ClearColor0 : 0) | (clear.ClearDepth ? RenderingDevice.DrawFlags.ClearDepth : 0) |
                    (clear.ClearStencil ? RenderingDevice.DrawFlags.ClearStencil : 0);
                list = rd.DrawListBegin(framebuffer, flags, [new Color(clear.R / 255f, clear.G / 255f, clear.B / 255f, clear.A / 255f)], clear.Depth, 0);
                continue;
            }
            var draw = (GeDraw)command;
            if (draw.Triangles.Length == 0) continue;
            Rid? texture = draw.Texture is { } t ? Texture2D(t) : white;
            if (texture is not { } textureRid) continue;
            if (list < 0) list = rd.DrawListBegin(framebuffer, RenderingDevice.DrawFlags.DefaultAll, [], 1, 0);
            var s = draw.State;
            rd.DrawListBindRenderPipeline(list, Pipeline(s));
            rd.DrawListBindUniformSet(list, UniformSet(textureRid, Sampler(s.Linear, s.ClampU, s.ClampV)), 0);
            var array = rd.VertexArrayCreate((uint)draw.Triangles.Length, vertexFormat, [buffer, buffer, buffer], [offsets[draw], offsets[draw], offsets[draw]]);
            transient.Add(array);
            rd.DrawListBindVertexArray(list, array);
            if (s.Blend is { } blend && Uses(blend, GeFactor.Constant))
                rd.DrawListSetBlendConstants(list, new Color(blend.ConstantR / 255f, blend.ConstantG / 255f, blend.ConstantB / 255f, 1));
            var push = new byte[16];
            BitConverter.TryWriteBytes(push.AsSpan(0, 4), draw.Texture is null ? 0f : 1f);
            BitConverter.TryWriteBytes(push.AsSpan(4, 4), s.TextureAlpha ? 1f : 0f);
            rd.DrawListSetPushConstant(list, push, (uint)push.Length);
            rd.DrawListDraw(list, false, 1);
        }
        if (list >= 0) rd.DrawListEnd();
    }

    /// <summary>Frees last frame's vertex arrays, then their buffer: freeing a buffer first would free the arrays with it.</summary>
    private void FreeTransient()
    {
        for (int i = transient.Count - 1; i >= 0; i--) rd.FreeRid(transient[i]);
        transient.Clear();
    }

    private static void Write(byte[] data, int first, GeVertex[] triangles)
    {
        for (int i = 0; i < triangles.Length; i++)
        {
            var v = triangles[i];
            var span = data.AsSpan((first + i) * Stride, Stride);
            BitConverter.TryWriteBytes(span[0..], v.X); BitConverter.TryWriteBytes(span[4..], v.Y);
            BitConverter.TryWriteBytes(span[8..], v.Z); BitConverter.TryWriteBytes(span[12..], v.W);
            span[16] = v.R; span[17] = v.G; span[18] = v.B; span[19] = v.A;
            BitConverter.TryWriteBytes(span[20..], v.U); BitConverter.TryWriteBytes(span[24..], v.V);
        }
    }

    private Rid? Texture2D(FlowTexture texture)
    {
        if (textures.TryGetValue(texture.Identity, out var known)) return known;
        Rid? created = pixels(texture) is { } p ? Upload(p.Rgba, p.Width, p.Height) : null;
        textures[texture.Identity] = created;
        return created;
    }

    private Rid Upload(byte[] rgba, int width, int height) => rd.TextureCreate(new RDTextureFormat
    {
        Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)width, Height = (uint)height,
        UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
    }, new RDTextureView(), [rgba]);

    private Rid Sampler(bool linear, bool clampU, bool clampV)
    {
        if (samplers.TryGetValue((linear, clampU, clampV), out var known)) return known;
        var filter = linear ? RenderingDevice.SamplerFilter.Linear : RenderingDevice.SamplerFilter.Nearest;
        var sampler = rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = filter, MagFilter = filter, MipFilter = RenderingDevice.SamplerFilter.Nearest,
            RepeatU = clampU ? RenderingDevice.SamplerRepeatMode.ClampToEdge : RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = clampV ? RenderingDevice.SamplerRepeatMode.ClampToEdge : RenderingDevice.SamplerRepeatMode.Repeat
        });
        samplers[(linear, clampU, clampV)] = sampler;
        return sampler;
    }

    private Rid UniformSet(Rid texture, Rid sampler)
    {
        if (uniformSets.TryGetValue((texture, sampler), out var known)) return known;
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        uniform.AddId(sampler); uniform.AddId(texture);
        var set = rd.UniformSetCreate([uniform], shader, 0);
        uniformSets[(texture, sampler)] = set;
        return set;
    }

    private Rid Pipeline(GeState s)
    {
        var key = (s.Blend, s.DepthTest, s.DepthCompare, s.DepthWrite, s.Stencil, s.WriteColor);
        if (pipelines.TryGetValue(key, out var known)) return known;
        var attachment = new RDPipelineColorBlendStateAttachment
        {
            WriteR = s.WriteColor, WriteG = s.WriteColor, WriteB = s.WriteColor, WriteA = s.WriteColor && (s.Blend?.WriteAlpha ?? true)
        };
        if (s.Blend is { } b)
        {
            attachment.EnableBlend = true;
            attachment.SrcColorBlendFactor = Factor(b.ColorSource); attachment.DstColorBlendFactor = Factor(b.ColorDestination);
            attachment.SrcAlphaBlendFactor = Factor(b.AlphaSource); attachment.DstAlphaBlendFactor = Factor(b.AlphaDestination);
            attachment.ColorBlendOp = RenderingDevice.BlendOperation.Add; attachment.AlphaBlendOp = RenderingDevice.BlendOperation.Add;
        }
        var blendState = new RDPipelineColorBlendState();
        blendState.Attachments.Add(attachment);
        var depthState = new RDPipelineDepthStencilState
        {
            EnableDepthTest = s.DepthTest, EnableDepthWrite = s.DepthTest && s.DepthWrite, DepthCompareOperator = Compare(s.DepthCompare),
            EnableStencil = s.Stencil != GeStencil.None
        };
        if (s.Stencil != GeStencil.None)
        {
            bool write = s.Stencil == GeStencil.Write;
            var compare = write ? RenderingDevice.CompareOperator.Always : RenderingDevice.CompareOperator.Equal;
            var pass = write ? RenderingDevice.StencilOperation.Replace : RenderingDevice.StencilOperation.Keep;
            depthState.FrontOpCompare = depthState.BackOpCompare = compare;
            depthState.FrontOpPass = depthState.BackOpPass = pass;
            depthState.FrontOpFail = depthState.BackOpFail = RenderingDevice.StencilOperation.Keep;
            depthState.FrontOpDepthFail = depthState.BackOpDepthFail = RenderingDevice.StencilOperation.Keep;
            depthState.FrontOpReference = depthState.BackOpReference = StencilReference;
            depthState.FrontOpCompareMask = depthState.BackOpCompareMask = 0xff;
            depthState.FrontOpWriteMask = depthState.BackOpWriteMask = 0xff;
        }
        var dynamic = s.Blend is { } used && Uses(used, GeFactor.Constant) ? RenderingDevice.PipelineDynamicStateFlags.BlendConstants : 0;
        var pipeline = rd.RenderPipelineCreate(shader, framebufferFormat, vertexFormat, RenderingDevice.RenderPrimitive.Triangles,
            new RDPipelineRasterizationState { CullMode = RenderingDevice.PolygonCullMode.Disabled }, new RDPipelineMultisampleState(),
            depthState, blendState, dynamic);
        pipelines[key] = pipeline;
        return pipeline;
    }

    private static bool Uses(GeBlend b, GeFactor f) => b.ColorSource == f || b.ColorDestination == f || b.AlphaSource == f || b.AlphaDestination == f;

    private static RenderingDevice.BlendFactor Factor(GeFactor f) => f switch
    {
        GeFactor.Zero => RenderingDevice.BlendFactor.Zero, GeFactor.One => RenderingDevice.BlendFactor.One,
        GeFactor.SrcColor => RenderingDevice.BlendFactor.SrcColor, GeFactor.InvSrcColor => RenderingDevice.BlendFactor.OneMinusSrcColor,
        GeFactor.SrcAlpha => RenderingDevice.BlendFactor.SrcAlpha, GeFactor.InvSrcAlpha => RenderingDevice.BlendFactor.OneMinusSrcAlpha,
        GeFactor.DstColor => RenderingDevice.BlendFactor.DstColor, GeFactor.InvDstColor => RenderingDevice.BlendFactor.OneMinusDstColor,
        GeFactor.DstAlpha => RenderingDevice.BlendFactor.DstAlpha, GeFactor.InvDstAlpha => RenderingDevice.BlendFactor.OneMinusDstAlpha,
        _ => RenderingDevice.BlendFactor.ConstantColor
    };

    private static RenderingDevice.CompareOperator Compare(GeCompare c) => c switch
    {
        GeCompare.Never => RenderingDevice.CompareOperator.Never, GeCompare.Always => RenderingDevice.CompareOperator.Always,
        GeCompare.Equal => RenderingDevice.CompareOperator.Equal, GeCompare.NotEqual => RenderingDevice.CompareOperator.NotEqual,
        GeCompare.Less => RenderingDevice.CompareOperator.Less, GeCompare.LessEqual => RenderingDevice.CompareOperator.LessOrEqual,
        GeCompare.Greater => RenderingDevice.CompareOperator.Greater, _ => RenderingDevice.CompareOperator.GreaterOrEqual
    };

    public void Dispose() => RenderingServer.CallOnRenderThread(Callable.From(() =>
    {
        Texture.TextureRdRid = default;
        FreeTransient();
        foreach (var rid in pipelines.Values.Concat(uniformSets.Values).Concat(samplers.Values)) rd.FreeRid(rid);
        foreach (var rid in textures.Values.OfType<Rid>()) rd.FreeRid(rid);
        foreach (var rid in new[] { framebuffer, depth, color, white, shader }) rd.FreeRid(rid);
    }));
}
