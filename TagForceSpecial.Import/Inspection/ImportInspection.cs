using System.Security.Cryptography;
using TagForceSpecial.Assets;
using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.Import.Diagnostics;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.Import.Manifest;
using TagForceSpecial.Import.Pipeline;
using TagForceSpecial.Import.Stages;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Import.Inspection;

public sealed record AssetInspection(string Details, byte[] Header, EhpEntry[] Members, GimTexture? Texture);

/// <summary>Read-only data access for importer front ends, retaining original source and normalized-output provenance.</summary>
public static class ImportInspection
{
    public static InventoryDocument Inventory(PrivateStorage storage, ImportManifest manifest)
    {
        var output = manifest.Outputs.Inventory ?? throw new InvalidOperationException("This session has no inventory. Repeat the import with inventory enabled.");
        Verify(storage, output);
        return InventoryStage.Open(storage.PathFor(output.Directory));
    }
    public static IReadOnlyList<CardDefinition> Cards(PrivateStorage storage, ImportManifest manifest)
    {
        var output = manifest.Outputs.Catalog ?? throw new InvalidOperationException("No catalog was generated.");
        Verify(storage, output);
        var profile = GameBuildProfile.English21;
        if (manifest.Source.ProfileFingerprint != profile.Fingerprint) throw new InvalidDataException("No trusted catalog profile matches this import.");
        return CatalogStage.Open(storage.PathFor(output.Directory), profile).Catalog.Cards;
    }
    public static (IReadOnlyList<LogEntry> Entries, int Unreadable) Logs(PrivateStorage storage, ImportManifest manifest) =>
        DiagnosticLog.Read(storage.PathFor(ImportLayout.SessionLog(manifest.SessionId)));

    public static AssetInspection Inspect(ImportManifest manifest, InventoryRecord record, string? memberName = null)
    {
        using var files = GameFileSystem.Open(manifest.Source.Path);
        var file = files.Find(record.Path) ?? throw new FileNotFoundException("The original source file is missing.");
        string hash = files.WithStream(file, s => Convert.ToHexStringLower(SHA256.HashData(s)));
        if (file.Length != record.Length || hash != record.Sha256) throw new InvalidDataException("The original file has changed since this inventory was recorded. Re-import it before previewing.");
        byte[] header = files.Read(file, 0, 1024);
        EhpEntry[] members = [];
        GimTexture? texture = null;
        string details = $"Original resource: {record.Path}\nSource: {manifest.Source.DisplayName}\nBytes: {record.Length:N0}\nOffset: {record.Offset?.ToString() ?? "directory file"}\nSHA-256: {hash} (matches inventory)\nFormat: {record.Format}\nClassification: {record.Reason}\nParser: {record.Parse ?? "Not measured"} {record.ParseDetail}";
        if (record.Format == "ehp")
        {
            var index = files.WithStream(file, s => new EhpReader().ReadIndex(s, EhpReadMode.InspectStoredRanges));
            members = index.Entries.ToArray(); details += "\n" + string.Join("\n", index.Diagnostics);
            if (memberName is not null)
            {
                var member = members.Single(m => m.Name == memberName);
                header = files.Read(file, member.Offset, (int)Math.Min(member.Length, 1024));
                details += $"\nMember: {member.Name}; stored offset {member.Offset}; stored bytes {member.Length}.";
                if (member.Name.EndsWith(".gim", StringComparison.OrdinalIgnoreCase))
                {
                    if (member.Length > GimTextureDecoder.MaximumFileBytes) throw new InvalidDataException("GIM member exceeds the preview limit.");
                    texture = new GimTextureDecoder().DecodeFrame(files.Read(file, member.Offset, (int)member.Length), 0);
                }
            }
        }
        else if (record.Format == "gim") texture = new GimTextureDecoder().DecodeFrame(files.ReadAll(file, GimTextureDecoder.MaximumFileBytes), 0);
        if (texture is not null) details += $"\nGIM frame 0: {texture.Width} × {texture.Height}; format {texture.Format}; swizzled {texture.Swizzled}; frames {texture.Frames}\n" + string.Join("\n", texture.Diagnostics);
        return new(details, header, members, texture);
    }
    public static void Verify(PrivateStorage storage, StageOutput output)
    {
        if (!StageCache.Matches(storage.PathFor(output.Directory), output.Artifacts))
            throw new InvalidDataException("Stage artifacts no longer match their recorded hashes: " + output.Stage);
    }
}
