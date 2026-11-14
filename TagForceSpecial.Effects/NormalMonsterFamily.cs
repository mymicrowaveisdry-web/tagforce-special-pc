using System.Collections.Frozen;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>
/// Every effect-free Normal Monster: Normal or Tribute Summon/Set by Level, being Tributed, flip, position change and
/// basic battle.
/// </summary>
public static class NormalMonsterFamily
{
    public const string FamilyId = "ordinary-normal-monster";
    public const string BatchId = "tfsp-family-batch-10c-01";
    public const string Contract =
        "Normal Summon/Set at Level 1–4; Tribute Summon/Set at Level 5–6 (one Tribute) or 7–8 (two Tributes); " +
        "may be Tributed; flip, position change, basic battle and owner Graveyard movement; no card effect.";

    public static DuelCardRules Rule { get; } = new(CardCategory.Monster, AllowsNormalSummon: true,
        AllowsBasicBattle: true, AllowsTributeSummon: true, AllowsTributeForSummon: true);

    public static IReadOnlyList<uint> CardIds => TfspNormalMonsters.Ids;

    public static IReadOnlyDictionary<uint, DuelCardRules> Rules { get; } =
        TfspNormalMonsters.Ids.ToFrozenDictionary(id => id, _ => Rule);

    /// <summary>Printed archetype membership with no effect of its own; returns null for ordinary members.</summary>
    public static string? Exception(uint cardId) =>
        TfspNormalMonsters.ArchfiendMembers.Contains(cardId)
            ? "Always treated as an \"Archfiend\" card (archetype membership only; no effect in this family)."
            : null;

    /// <summary>Fails closed for an unreviewed build, a missing ID or metadata/statistics outside the family contract.</summary>
    public static void ValidateCatalog(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 is { } hash && hash != TfspCardMetadata.CardInfoSha256)
            throw new InvalidDataException("Normal Monster family has not been reviewed against this source build.");
        var byId = catalog.Cards.ToDictionary(c => c.GameCardId);
        foreach (uint id in TfspNormalMonsters.Ids)
        {
            if (!byId.TryGetValue(id, out var card))
                throw new InvalidDataException($"Normal Monster {id} is missing from the catalog.");
            if (card.Kind != CardKind.Monster || card.Effects is not { State: CardEffectState.None } ||
                card.Codes is { } codes && codes.GetterKind != 0 ||
                card.Statistics is not { Attack: not null, Defense: not null, Level: >= 1 and <= 8 } ||
                card.Source?.ContainerSha256 is { } origin && origin != TfspCardMetadata.CardInfoSha256)
                throw new InvalidDataException($"Normal Monster {id} metadata or statistics differ from the reviewed family.");
        }
    }

    /// <summary>
    /// Family rules combined with other explicit profiles (e.g. the preview Spells). A family ID's family rule
    /// replaces a narrower Monster rule; any non-Monster rule for a family ID is a conflict.
    /// </summary>
    public static IReadOnlyDictionary<uint, DuelCardRules> RulesWith(IReadOnlyDictionary<uint, DuelCardRules> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.FirstOrDefault(e => Rules.ContainsKey(e.Key) && e.Value.Category != CardCategory.Monster) is { Value: not null } conflict)
            throw new ArgumentException($"Card {conflict.Key} already has non-Monster rules.", nameof(other));
        return other.Where(e => !Rules.ContainsKey(e.Key)).Concat(Rules).ToFrozenDictionary();
    }
}
