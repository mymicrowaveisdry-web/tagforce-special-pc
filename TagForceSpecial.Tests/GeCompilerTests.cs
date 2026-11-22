using System.Numerics;
using TagForceSpecial.PC.App.Screens.Album;
using TagForceSpecial.PC.App.Screens.DuelField;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.Tests;

/// <summary>Host-neutral GE rules shared by every renderer: culling, flow and measured-draw conversion, and MonoGame's colour rounding.</summary>
public sealed class GeCompilerTests
{
    private static GeVertex V(float x, float y, float w = 1) => new(x, y, 0, w, 255, 255, 255, 255, 0, 0);

    [Fact]
    public void ClockwiseOnScreenIsCulledAndTrianglesBehindTheEyeAreKept()
    {
        GeVertex[] counterClockwise = [V(0, 0), V(1, 0), V(0, 1)]; // positive signed area in y-up clip space
        GeVertex[] clockwise = [V(0, 0), V(0, 1), V(1, 0)]; // negative: clockwise on screen, culled like MonoGame's CullClockwiseFace
        Assert.Equal(3, GeGeometry.CullClockwise(counterClockwise).Length);
        Assert.Empty(GeGeometry.CullClockwise(clockwise));
        Assert.Equal(3, GeGeometry.CullClockwise([V(0, 0), V(0, 1), V(1, 0, w: -1)]).Length);
    }

    [Fact]
    public void FlowDrawsMapCanvasPixelsToClipSpaceWithTheirBlend()
    {
        var quad = FlowDrawCompiler.Quad("tex", new FlowQuad(0, 0, 480, 272, 0, 0, 1, 1, [10, 20, 30, 40], Additive: true), 0, 0, linear: false);
        var draw = Assert.IsType<GeDraw>(GeGeometry.FromFlow(quad));
        Assert.Equal((-1f, 1f), (draw.Triangles[0].X, draw.Triangles[0].Y));
        Assert.Equal((1f, -1f), (draw.Triangles[5].X, draw.Triangles[5].Y));
        Assert.Equal(new GeBlend(GeFactor.SrcAlpha, GeFactor.One, GeFactor.Zero, GeFactor.One), draw.State.Blend);
        Assert.False(draw.State.DepthTest);
        Assert.IsType<GeClear>(GeGeometry.FromFlow(new FlowClearStencil()));
    }

    [Fact]
    public void FlowMasksWriteStencilOnlyAndTestedDrawsReadIt()
    {
        static FlowDraw Draw(bool mask, bool stencil) => new(null, 3, [[0, 0], [1, 0], [0, 1]], [], [255, 255, 255, 255], null, null,
            Additive: false, LinearSampling: false, ClearStencil: false, WriteMask: mask, Stencil: stencil);
        var mask = (GeDraw)GeGeometry.FromFlow(FlowDrawCompiler.Compile(Draw(mask: true, stencil: false))!);
        Assert.Equal((GeStencil.Write, false), (mask.State.Stencil, mask.State.WriteColor));
        var tested = (GeDraw)GeGeometry.FromFlow(FlowDrawCompiler.Compile(Draw(mask: false, stencil: true))!);
        Assert.Equal((GeStencil.Test, true), (tested.State.Stencil, tested.State.WriteColor));
        var clear = Assert.IsType<GeClear>(GeGeometry.FromFlow(new FlowClearStencil()));
        Assert.Equal((false, false, true), (clear.ClearColor, clear.ClearDepth, clear.ClearStencil));
    }

    [Fact]
    public void FlowLayersOfOtherSizesMapTheirOwnPixelsAndStartTransparent()
    {
        var quad = FlowDrawCompiler.Quad("tex", new FlowQuad(0, 0, 253, 384, 0, 0, 1, 1, [255, 255, 255, 255], false), 0, 0, linear: true);
        var commands = GeGeometry.FromFlows([quad], 253, 384);
        var first = Assert.IsType<GeClear>(commands[0]);
        Assert.Equal((true, 0), (first.ClearColor, (int)first.A));
        var draw = Assert.IsType<GeDraw>(commands[1]);
        Assert.Equal((1f, -1f), (draw.Triangles[5].X, draw.Triangles[5].Y));
    }

    [Fact]
    public void ColoursTruncateAsTheMonoGameHostConvertedThem()
    {
        Assert.Equal(254, GeGeometry.Byte(0.999f));
        Assert.Equal(255, GeGeometry.Byte(1.5f));
        var a = new CardAlbumVertex(Vector3.Zero, 0, 100, 255, 10, Vector2.Zero);
        var b = new CardAlbumVertex(Vector3.One, 3, 101, 0, 20, Vector2.One);
        var mid = CardAlbumBinder.Lerp(a, b, 0.5f);
        Assert.Equal((1, 100, 127, 15), (mid.R, mid.G, mid.B, mid.A)); // 1.5 → 1, 100.5 → 100, 127.5 → 127
        Assert.Equal(new Vector3(0.5f), mid.Position);
    }

    private static readonly DuelFieldProfile Field = new(1, "test", new Dictionary<string, PspTextureInfo>(), [], [], new Dictionary<string, PspDraw[]>());

    private static PspDraw Through(int prim, float[][] vertices, string? texture = "duel.board", PspBlend? blend = null, bool textureAlpha = true) =>
        new("test", prim, Through: true, null, null, 0, texture, vertices, vertices.Select(_ => new[] { 0f, 0f }).ToArray(),
            vertices.Select((_, i) => new[] { 1f, 1f, 1f, i == 1 ? 0.5f : 1f }).ToArray(), 0, 0, 0, 0, blend, DepthTest: false, 7, false, null,
            null, textureAlpha, Linear: false, Clamp: null);

    [Fact]
    public void GeSpritesBecomeTwoTrianglesColouredByTheirSecondVertex()
    {
        var draw = Assert.IsType<GeDraw>(PspGeCompiler.Compile(Through(6, [[0, 0, 0], [240, 136, 0]]), Field));
        Assert.Equal(6, draw.Triangles.Length);
        Assert.All(draw.Triangles, v => Assert.Equal(127, v.A)); // the second corner's alpha 0.5, truncated
        Assert.Equal((-1f, 1f), (draw.Triangles[0].X, draw.Triangles[0].Y));
        Assert.Equal((0f, 0f), (draw.Triangles[5].X, draw.Triangles[5].Y));
        Assert.Equal(1f, draw.Triangles[0].Z); // through-mode z 0 is the far end of the layer depth
    }

    [Fact]
    public void FixedColourBlendsUseConstantsAndNeverWriteAlpha()
    {
        var fixWhite = PspGeCompiler.Compile(Through(3, [[0, 0, 0], [1, 0, 0], [0, 1, 0]], blend: new("srcAlpha", "fix", 0, 0xffffff)), Field) as GeDraw;
        Assert.Equal(GeFactor.One, fixWhite!.State.Blend!.Value.ColorDestination);
        var fixGrey = PspGeCompiler.Compile(Through(3, [[0, 0, 0], [1, 0, 0], [0, 1, 0]], blend: new("srcAlpha", "fix", 0, 0x404040)), Field) as GeDraw;
        Assert.Equal(GeFactor.Constant, fixGrey!.State.Blend!.Value.ColorDestination);
        Assert.Equal((0x40, 0x40, 0x40), (fixGrey.State.Blend.Value.ConstantR, fixGrey.State.Blend.Value.ConstantG, fixGrey.State.Blend.Value.ConstantB));
        Assert.False(fixGrey.State.Blend.Value.WriteAlpha);
    }

    [Fact]
    public void OnlyImportedTexturesDropTheirAlphaForRgbOnlyDraws()
    {
        float[][] triangle = [[0, 0, 0], [1, 0, 0], [0, 1, 0]];
        Assert.False(((GeDraw)PspGeCompiler.Compile(Through(3, triangle, textureAlpha: false), Field)!).State.TextureAlpha);
        Assert.True(((GeDraw)PspGeCompiler.Compile(Through(3, triangle, texture: GeTextureKeys.Face(1234), textureAlpha: false), Field)!).State.TextureAlpha);
        Assert.True(((GeDraw)PspGeCompiler.Compile(Through(3, triangle, texture: "title.eboot.arrow", textureAlpha: false), Field)!).State.TextureAlpha);
        Assert.True(GeTextureKeys.TryFace(GeTextureKeys.Face(1234), out uint card) && card == 1234);
    }

    [Fact]
    public void ClearsKeepTheLayerOpaqueAndMapGeDepth()
    {
        var draw = Through(3, []) with { Clear = new(Color: true, Stencil: false, Depth: true), ClearRgba = [0.5f, 0, 1, 0], ClearDepth = 0 };
        var clear = Assert.IsType<GeClear>(PspGeCompiler.Compile(draw, Field));
        Assert.Equal((127, 0, 255, 255), (clear.R, clear.G, clear.B, clear.A));
        Assert.Equal(1f, clear.Depth);
        Assert.Null(PspGeCompiler.Compile(draw with { Clear = new(false, false, false) }, Field));
    }
}
