using System.Security.Cryptography;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Import;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.Tests;

public class Phase2LocalReferenceTests
{
    [Fact]
    [Trait("Category", "LocalReference")]
    public void AcceptedSourcePixelsRequireExactIsoAndMatchRetainedOriginalRuntimeOutput()
    {
        string root = Environment.GetEnvironmentVariable("TFSP_PRIVATE_ROOT")
            ?? throw new InvalidOperationException("Set TFSP_PRIVATE_ROOT to the private evidence root. Never copy fixtures into Git/CI.");
        using var source = GameSourceValidator.Open(Path.Combine(root, "source", "original.iso")).Source;
        Assert.NotNull(source);
        var storage = new PrivateStorage(Path.Combine(root, "phase2-acceptance-20261104", "accepted-app-data"));
        var registry = new PresentationCacheStore(storage).OpenActive();
        Assert.NotNull(registry);
        Assert.Equal(6, registry.Entries.Count);
        var phase2References = PresentationReferenceCases.Cases.Where(c => c.EvidenceId.StartsWith("phase2.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, phase2References.Length);
        foreach (var reference in phase2References)
        {
            var resolved = registry.Resolve(reference.AssetKey);
            Assert.Equal(reference.PixelSha256, Convert.ToHexStringLower(SHA256.HashData(resolved.Content)));
            Assert.Equal(PresentationReferenceCases.Scope, registry.VerifiedReferenceScope(reference.AssetKey));
            Assert.Equal("mapped-unverified", resolved.Manifest.Status);
            Assert.Null(resolved.Manifest.Output.SourceRectangle); // Sprite geometry was not invented.
        }
        var binding = storage.Read<PresentationCacheBinding>("presentation/active.json");
        var package = PresentationPackage.Open(storage.PathFor("presentation/generations/" + binding.Generation + "/package-cache"),
            binding.PackageId, GameBuildProfile.English21, binding.Scope, binding.ResourceKeys);
        var art = new CardArtworkProvider(package).ReadFullArt(9863);
        Assert.Equal((256, 256, "opaque"), (art.Width, art.Height, art.AlphaUsage));
        Assert.True(Enumerable.Range(0, art.Width * art.Height).All(i => art.Rgba[i * 4 + 3] == 0));
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "phase2-acceptance-20261104", "jpeg-runtime-trace-uncached", "runtime-output.rgba")), art.Rgba);
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "phase2-acceptance-20261104", "jpeg-runtime-trace-repeat", "runtime-output.rgba")), art.Rgba);
        Assert.Throws<KeyNotFoundException>(() => new CardArtworkProvider(package).ReadFullArt(3459));
        var diagnostics = AssetDiagnostics.Inspect(registry, phase2References.Select(c => new AssetRequirement(c.Screen, c.AssetKey)));
        Assert.All(diagnostics, row => Assert.Equal("verified", row.Status));
        Assert.True(source.IsUnchanged());
    }
}
