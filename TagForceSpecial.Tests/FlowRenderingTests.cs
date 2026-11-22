using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.Tests;

/// <summary>Host-neutral rendering and timing rules every game host shares: measured draws to triangle batches, baked specular,
/// fixed update steps and input priming.</summary>
public sealed class FlowRenderingTests
{
    private static FlowDraw Draw(int primitive, int points, string? key = "tex", int[]? add = null, uint[]? vertexColors = null) =>
        new(key, primitive, Enumerable.Range(0, points).Select(i => new[] { (float)i, i * 2f }).ToArray(),
            Enumerable.Range(0, points).Select(i => new[] { i / 10f, i / 20f }).ToArray(), [10, 20, 30, 40], vertexColors, add,
            Additive: false, LinearSampling: true, ClearStencil: false, WriteMask: false, Stencil: false);

    [Fact]
    public void StripsAndFansExpandInGeVertexOrder()
    {
        var strip = (FlowBatch)FlowDrawCompiler.Compile(Draw(4, 5))!;
        Assert.Equal([0f, 1, 2, 1, 2, 3, 2, 3, 4], strip.Triangles.Select(v => v.X));
        var fan = (FlowBatch)FlowDrawCompiler.Compile(Draw(5, 5))!;
        Assert.Equal([0f, 1, 2, 0, 2, 3, 0, 3, 4], fan.Triangles.Select(v => v.X));
        Assert.Null(FlowDrawCompiler.Compile(Draw(6, 5)));
        Assert.Null(FlowDrawCompiler.Compile(Draw(3, 2)));
        Assert.IsType<FlowClearStencil>(FlowDrawCompiler.Compile(Draw(3, 3) with { ClearStencil = true }));
    }

    [Fact]
    public void SpecularBakesIntoTheTextureAndLeavesOnlyAlphaInTheMaterial()
    {
        var plain = (FlowBatch)FlowDrawCompiler.Compile(Draw(3, 3))!;
        Assert.Equal((10, 20, 30, 40), (plain.Triangles[0].R, plain.Triangles[0].G, plain.Triangles[0].B, plain.Triangles[0].A));
        Assert.Null(plain.Texture!.Value.Specular);
        var lit = (FlowBatch)FlowDrawCompiler.Compile(Draw(3, 3, add: [5, 6, 7]))!;
        Assert.Equal((255, 255, 255, 40), (lit.Triangles[0].R, lit.Triangles[0].G, lit.Triangles[0].B, lit.Triangles[0].A));
        Assert.Equal(new FlowSpecular(10, 20, 30, 5, 6, 7), lit.Texture!.Value.Specular);
        Assert.NotEqual(plain.Texture.Value.Identity, lit.Texture.Value.Identity);
        // Without a texture the specular has nothing to bake into: the material keeps its colour.
        var untextured = (FlowBatch)FlowDrawCompiler.Compile(Draw(3, 3, key: null, add: [5, 6, 7]))!;
        Assert.Null(untextured.Texture);
        Assert.Equal(10, untextured.Triangles[0].R);
        var colored = (FlowBatch)FlowDrawCompiler.Compile(Draw(3, 3, vertexColors: [0x80402010, 0, 0]))!;
        Assert.Equal((0x10, 0x20, 0x40, 0x80), (colored.Triangles[0].R, colored.Triangles[0].G, colored.Triangles[0].B, colored.Triangles[0].A));
    }

    [Fact]
    public void BakedSpecularClampsPerChannelAndKeepsTextureAlpha()
    {
        byte[] pixels = [200, 100, 0, 77];
        Assert.Equal([255, 60, 9, 77], FlowPixels.Specular(pixels, new(255, 153, 255, 100, 0, 9)));
    }

    [Fact]
    public void FixedStepClockRunsWholeSixtiethsAndDropsLongStalls()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0, clock.Advance(0.010));
        Assert.Equal(1, clock.Advance(0.010));
        Assert.Equal(2, clock.Advance(2 / 60.0));
        Assert.Equal(FixedStepClock.MaxStepsPerAdvance, clock.Advance(5));
        Assert.Equal(0, clock.Advance(0.001));
        Assert.Equal(3 + FixedStepClock.MaxStepsPerAdvance, clock.Steps);
    }

    [Fact]
    public void InputEdgesPrimeAfterFocusAndReportPressesOnce()
    {
        var edges = new InputEdges();
        Assert.Equal(InputAction.None, edges.Next(InputAction.Confirm, false, true).Pressed); // first active frame primes
        Assert.Equal(InputAction.None, edges.Next(InputAction.Confirm, false, true).Pressed); // still held
        Assert.Equal(InputAction.Down, edges.Next(InputAction.Confirm | InputAction.Down, false, true).Pressed);
        Assert.Equal(InputAction.None, edges.Next(InputAction.Down, false, false).Held); // focus lost
        Assert.Equal(InputAction.None, edges.Next(InputAction.Down, true, true).Pressed); // refocus primes again
        Assert.True(edges.Next(InputAction.None, true, true) is { UtilityRequested: false });
        Assert.True(edges.Next(InputAction.None, false, true) is { UtilityRequested: false });
        Assert.True(edges.Next(InputAction.None, true, true).UtilityRequested);
    }

    [Fact]
    public void MainMenuBackgroundFollowsTheEntrySequence()
    {
        var layers = new MainMenuLayers(FlowProfile.LoadEmbedded(), MainMenuAnimation.LoadEmbedded());
        var entered = layers.Background(0);
        var early = layers.Background(0, entryFrame: 0);
        Assert.True(entered.Count > early.Count); // header and title are hidden before their entry frames
        Assert.Contains(MainMenuLayers.PanelKey, layers.TextureKeys);
        Assert.Equal((0, 3 * 2 * MainMenuLayers.LabelHeight + MainMenuLayers.LabelHeight, MainMenuLayers.LabelWidth, MainMenuLayers.LabelHeight),
            MainMenuLayers.LabelSource(3, focused: true));
    }
}
