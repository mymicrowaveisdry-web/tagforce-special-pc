using System.Security.Cryptography;
using System.Text;
using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Import.Sources;

public sealed record ScannedFile(GameFileEntry Entry, string Sha256, byte[] Header);

/// <summary>Hashes every file of a source once, keeping each file's leading bytes for classification.</summary>
public static class SourceScan
{
    public static IReadOnlyList<ScannedFile> Run(GameFileSystem files, IProgress<(long Done, long Total, string Item)>? progress, CancellationToken cancellation)
    {
        var entries = files.List().Where(e => !e.Directory).ToArray();
        long total = entries.Sum(e => e.Length), done = 0;
        var result = new List<ScannedFile>(entries.Length);
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report((done, total, entry.Path));
            result.Add(files.WithStream(entry, stream =>
            {
                var header = new byte[(int)Math.Min(FileClassifier.HeaderBytes, entry.Length)];
                stream.ReadExactly(header);
                stream.Position = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[256 * 1024];
                for (int read; (read = stream.Read(buffer)) > 0;)
                {
                    cancellation.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                }
                return new ScannedFile(entry, Convert.ToHexStringLower(hash.GetHashAndReset()), header);
            }));
            done += entry.Length;
        }
        progress?.Report((total, total, ""));
        return result;
    }

    /// <summary>Identity of the game files under <c>PSP_GAME/</c>: paths (case-folded), lengths and hashes. Container layout is excluded,
    /// so an extracted directory and its ISO share one digest.</summary>
    public static string TreeDigest(IEnumerable<ScannedFile> files)
    {
        var text = new StringBuilder();
        foreach (var f in files.Where(f => f.Entry.Path.StartsWith("PSP_GAME/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.Entry.Path.ToUpperInvariant(), StringComparer.Ordinal))
            text.Append(f.Entry.Path.ToUpperInvariant()).Append('\t').Append(f.Entry.Length).Append('\t').Append(f.Sha256).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
