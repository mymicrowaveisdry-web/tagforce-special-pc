using System.Security.Cryptography;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Resources;

/// <summary>Installs only the two hash-pinned PSP fonts used by the game's text renderer; never bundles or downloads firmware.</summary>
public static class GameFonts
{
    public const string MenuSha256 = "250babd8a242d7eaaa84284c78d5e2273b29e61d0d8c01b65414e3248d7c1164";
    public const string CardSha256 = "1184f16bc89bad1fea49093806784470281af54f279b57ef0378ce33e6603870";
    public const string MenuFile = "fonts/psp-latin.pgf", CardFile = "fonts/psp-latin-card.pgf";
    private const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly (string Target, string Hash)[] Required = [(MenuFile, MenuSha256), (CardFile, CardSha256)];

    public static IReadOnlyList<string> Ensure(PrivateStorage storage, string? source = null, string? fontDirectory = null, CancellationToken cancellation = default)
    {
        var missing = new List<string>();
        IReadOnlyList<string>? roots = null;
        foreach (var (target, hash) in Required)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Valid(storage.PathFor(target), hash)) continue;
            roots ??= (fontDirectory is null ? Array.Empty<string>() : new[] { Path.GetFullPath(fontDirectory) })
                .Concat(ResourceDiscovery.Roots(storage, source)).ToArray();
            string? found = Candidates(roots).FirstOrDefault(path => Valid(path, hash));
            if (found is null) { missing.Add(target); continue; }
            cancellation.ThrowIfCancellationRequested();
            byte[] bytes = File.ReadAllBytes(found);
            if (new PgfFont(bytes).Sha256 != hash) throw new InvalidDataException("PSP font changed during import.");
            storage.WriteBytes(target, bytes);
        }
        return missing;
    }
    public static bool Ready(PrivateStorage storage) => Required.All(f => Valid(storage.PathFor(f.Target), f.Hash));
    private static IEnumerable<string> Candidates(IEnumerable<string> roots)
    {
        foreach (string root in roots)
            foreach (string subfolder in new[] { "", "fonts", "flash0/font", "assets/flash0/font", "PSP/flash0/font" })
                foreach (string name in new[] { "psp-latin.pgf", "psp-latin-card.pgf", "ltn4.pgf", "ltn0.pgf" })
                    yield return Path.Combine(root, subfolder, name);
    }
    private static bool Valid(string path, string hash)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return false;
            PrivateStorage.Check(path);
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == hash;
        }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return false; }
    }
}
