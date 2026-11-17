using System.Security.Cryptography;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Stages;

/// <summary>Writes the session manifest and, for a usable result, atomically points <c>import/active.json</c> at it. Older sessions are kept.</summary>
public static class PublishStage
{
    public const string Id = "publish";

    public static StageResult Run(ImportSession session, bool sourceUnchanged)
    {
        var status = session.OverallStatus();
        if (status is not (ImportStatus.Failed or ImportStatus.Cancelled) && !sourceUnchanged)
        {
            session.Log.Error(Id, "The original image changed during import; nothing was activated.");
            status = ImportStatus.Failed;
        }
        bool hasActive = ActiveExists(session.Storage);
        bool activate = status is ImportStatus.Succeeded or ImportStatus.SucceededWithWarnings || status == ImportStatus.Partial && !hasActive;
        string message = activate ? $"Import {status}; now active." :
            status == ImportStatus.Partial ? "Partial import recorded; the previous complete import stays active." :
            $"Import {status}; the previous import (if any) stays active.";
        var record = new StageResult(activate ? StageStatus.Succeeded : StageStatus.Skipped, message);
        var built = session.BuildManifest(status);
        var manifest = built with
        {
            Stages = built.Stages.Select(s => s.Id == Id ? s with { Status = record.Status, Message = message, FinishedUtc = DateTimeOffset.UtcNow } : s).ToArray()
        };
        byte[] bytes = ManifestJson.Serialize(manifest);
        session.Storage.WriteBytes(ImportLayout.SessionManifest(session.Id), bytes);
        session.Manifest = manifest;
        if (activate)
            session.Storage.Write(ImportLayout.Active, new ActiveImport(1, session.Id, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        return record;
    }

    private static bool ActiveExists(PrivateStorage storage)
    {
        try { return InstalledGameReader.ReadActiveManifest(storage) is not null; }
        catch (Exception e) when (PrivateStorage.Recoverable(e)) { return false; }
    }
}
