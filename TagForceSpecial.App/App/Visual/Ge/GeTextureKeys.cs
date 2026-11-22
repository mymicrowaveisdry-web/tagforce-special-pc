using System.Globalization;

namespace TagForceSpecial.PC.App.Visual.Ge;

/// <summary>Texture keys a host resolves itself rather than from the import: decoded card faces and textures decoded from the stored EBOOT.</summary>
public static class GeTextureKeys
{
    public const string FacePrefix = "card.face:", EbootPrefix = "title.eboot.";

    public static string Face(uint cardId) => FacePrefix + cardId.ToString(CultureInfo.InvariantCulture);

    public static bool TryFace(string key, out uint cardId)
    {
        cardId = 0;
        return key.StartsWith(FacePrefix, StringComparison.Ordinal) &&
            uint.TryParse(key.AsSpan(FacePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out cardId);
    }

    /// <summary>Card faces and EBOOT textures are always drawn with their own alpha; only imported textures have an alpha-less variant.</summary>
    public static bool AlwaysOwnAlpha(string key) => key.StartsWith(FacePrefix, StringComparison.Ordinal) || key.StartsWith(EbootPrefix, StringComparison.Ordinal);
}
