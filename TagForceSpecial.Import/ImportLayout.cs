namespace TagForceSpecial.Import;

/// <summary>Relative paths of importer-owned files inside a data root. Nothing outside <see cref="Root"/> belongs to the importer.</summary>
public static class ImportLayout
{
    public const string Root = "import";
    public const string Active = "import/active.json";
    public const string Lock = "import/import.lock";
    public static string Session(string id) => "import/sessions/" + id;
    public static string SessionManifest(string id) => Session(id) + "/manifest.json";
    public static string SessionValidation(string id) => Session(id) + "/validation.json";
    public static string SessionReport(string id) => Session(id) + "/report.md";
    public static string SessionLog(string id) => Session(id) + "/log.jsonl";
    public static string SessionInventory(string id) => Session(id) + "/inventory.json";
    public static string Stages(string stage) => "import/stages/" + stage;
    public static string StageOutput(string stage, string key) => Stages(stage) + "/" + key;

    public static bool ValidSessionId(string? id) => Guid.TryParseExact(id, "N", out _);
    public static bool ValidKey(string? key) => key is { Length: 64 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
