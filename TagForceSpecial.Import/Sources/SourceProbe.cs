using TagForceSpecial.Assets.GameSource;

namespace TagForceSpecial.Import.Sources;

/// <param name="Container">Whether the ISO 9660 structure or the directory could be opened and listed.</param>
public sealed record ProbeResult(string Kind, bool Container, bool PspLayout, string? GameId, string? Title, string? DiscVersion,
    string? Category, IReadOnlyList<string> Notes)
{
    public bool Recognized => PspLayout && GameId is not null && SourceProbe.KnownGameIds.ContainsKey(GameId);
}

/// <summary>Fast, read-only first look at a source: container structure, PSP layout and PARAM.SFO identity. It never decides support.</summary>
public static class SourceProbe
{
    /// <summary>Disc IDs of Tag Force Special. A file name containing "Tag Force" is never evidence.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownGameIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NPJH00142"] = "Yu-Gi-Oh! ARC-V Tag Force Special"
    };
    private static readonly string[] RequiredLayout = ["PSP_GAME/PARAM.SFO", "PSP_GAME/SYSDIR/EBOOT.BIN", "PSP_GAME/USRDIR"];

    public static ProbeResult Probe(string path)
    {
        bool directory = Directory.Exists(path);
        string kind = directory ? "directory" : "iso";
        if (!directory && !File.Exists(path)) return new(kind, false, false, null, null, null, null, ["The selected path does not exist."]);
        try
        {
            using var files = directory ? GameFileSystem.OpenDirectory(path) : GameFileSystem.OpenIso(path);
            return Probe(files);
        }
        catch (InvalidDataException e) { return new(kind, false, false, null, null, null, null, [(directory ? "Directory" : "ISO 9660") + " structure is invalid: " + e.Message]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(kind, false, false, null, null, null, null, ["The source cannot be read: " + e.GetType().Name + "."]); }
    }

    public static ProbeResult Probe(GameFileSystem files)
    {
        var notes = new List<string>();
        var missing = RequiredLayout.Where(p => files.Find(p) is null).ToArray();
        if (missing.Length > 0) notes.Add("Missing PSP layout entries: " + string.Join(", ", missing) + ".");
        string? id = null, title = null, version = null, category = null;
        if (files.Find("PSP_GAME/PARAM.SFO") is { Directory: false } sfo)
        {
            try
            {
                var values = ParamSfo.Read(files.ReadAll(sfo, ParamSfo.MaximumBytes));
                id = values.GetValueOrDefault("DISC_ID"); title = values.GetValueOrDefault("TITLE");
                version = values.GetValueOrDefault("DISC_VERSION"); category = values.GetValueOrDefault("CATEGORY");
                if (id is not null && !KnownGameIds.ContainsKey(id)) notes.Add($"Disc ID {id} is not a known Tag Force Special build.");
            }
            catch (InvalidDataException e) { notes.Add("PARAM.SFO is unreadable: " + e.Message); }
        }
        return new(files.Kind, true, missing.Length == 0, id, title, version, category, notes);
    }
}
