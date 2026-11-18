using System.Collections.Frozen;

namespace TagForceSpecial.Effects;

/// <summary>
/// The built-in Forbidden/Limited seed of the English v2.1 EBOOT, read as static source (not a save, DLC or story regulation).
/// A listed card allows 0 (Forbidden), 1 (Limited) or 2 (Semi-Limited) copies; an unlisted card allows 3.
/// </summary>
public static class BuiltInRestrictions
{
    public const string EbootSha256 = "8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975";
    public const string TableSha256 = "ea2aa9fa9c493a82374d0b64ed7628f7e5906f69178d3533c5e0c45afba09009";
    public const int FileOffset = 0xd27e0;

    /// <summary>Card ID and copies allowed, in table order (sorted by status).</summary>
    public static IReadOnlyList<(uint CardId, int Copies)> Entries { get; } = Array.AsReadOnly(new (uint, int)[]
    {
        (7601, 0), (5868, 0), (10581, 0), (5860, 0), (4481, 0), (4054, 0), (9095, 0), (4580, 0), (7736, 0), (5880, 0), (4913, 0), (4740, 0),
        (5285, 0), (10577, 0), (4434, 0), (9742, 0), (10576, 0), (7095, 0), (5660, 0), (7548, 0), (5371, 0), (8318, 0), (10582, 0), (5657, 0),
        (7714, 0), (5375, 0), (6262, 0), (5433, 0), (4901, 0), (5231, 0), (4900, 0), (4911, 0), (5181, 0), (4907, 0), (4898, 0), (4844, 0),
        (4860, 0), (4343, 0), (5906, 0), (5123, 0), (4803, 0), (5127, 0), (5724, 0), (4812, 0), (4678, 0), (4966, 0), (4910, 0), (5671, 0),
        (10544, 0), (6782, 0), (5539, 0), (4885, 0), (6078, 0), (5399, 0), (4960, 0), (5360, 0), (4667, 0), (5945, 0), (5446, 0), (4851, 0),
        (4984, 0), (5005, 0), (5406, 0), (11040, 1), (8709, 1), (9597, 1), (9894, 1), (9891, 1), (11258, 1), (11257, 1), (6784, 1), (10521, 1),
        (10219, 1), (5835, 1), (10519, 1), (7423, 1), (6400, 1), (9918, 1), (7421, 1), (6654, 1), (9554, 1), (6042, 1), (9861, 1), (6075, 1),
        (10520, 1), (8732, 1), (4027, 1), (4023, 1), (4025, 1), (4024, 1), (4026, 1), (9118, 1), (11294, 1), (10471, 1), (4597, 1), (10522, 1),
        (10445, 1), (7557, 1), (6941, 1), (9778, 1), (8823, 1), (4891, 1), (5236, 1), (5846, 1), (4842, 1), (4817, 1), (5328, 1), (11159, 1),
        (5346, 1), (5432, 1), (4821, 1), (6511, 1), (7868, 1), (6161, 1), (4342, 1), (9350, 1), (5980, 1), (7570, 1), (5129, 1), (9049, 1),
        (10590, 1), (8602, 1), (8197, 1), (11281, 1), (9008, 1), (4861, 1), (3650, 1), (4956, 1), (7010, 1), (5990, 1), (6444, 1), (10006, 2),
        (6393, 2), (10972, 2), (7574, 2), (6939, 2), (9332, 2), (8283, 2), (6949, 2), (9488, 2), (8089, 2), (7970, 2), (8011, 2), (7359, 2),
        (9699, 2), (7568, 2), (10079, 2), (6904, 2), (7187, 2), (5688, 2), (5738, 2), (5400, 2)
    });

    private static readonly FrozenDictionary<uint, int> Copies = Entries.ToFrozenDictionary(e => e.CardId, e => e.Copies);

    public static int CopiesAllowed(uint cardId) => Copies.TryGetValue(cardId, out int copies) ? copies : 3;
}
