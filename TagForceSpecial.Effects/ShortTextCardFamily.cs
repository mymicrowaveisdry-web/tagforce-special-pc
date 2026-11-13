using TagForceSpecial.Effects.Definitions;

namespace TagForceSpecial.Effects;

/// <summary>
/// Registers the short-text cards, whose definitions live in behaviour modules under <c>Definitions/</c>. The family and batch IDs and
/// the table prefix keep the cards' effect IDs stable; the family is a registration unit, not an archetype.
/// </summary>
public static class ShortTextCardFamily
{
    public const string FamilyId = "short-text-card-definition";
    public const string BatchId = "tfsp-family-batch-10d-05";

    private static readonly CardTable T = new("short-text");

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly<AuthoredCard>(
    [
        .. EquipAndFieldDefinitions.Cards(T),
        .. GraveyardTriggerDefinitions.Cards(T),
        .. FlipEffectDefinitions.Cards(T),
        .. SummonAndBattleTriggerDefinitions.Cards(T),
        .. MonsterActivatedEffectDefinitions.Cards(T),
        .. TargetedRemovalDefinitions.Cards(T),
        .. BoardRemovalDefinitions.Cards(T),
        .. AttackResponseDefinitions.Cards(T),
        .. EffectSummonDefinitions.Cards(T),
        .. ResourceSpellDefinitions.Cards(T),
        .. StatChangeDefinitions.Cards(T),
        .. GraveyardAndBanishedDefinitions.Cards(T),
        .. ArchetypeInteractionDefinitions.Cards(T),
        .. GenexDefinitions.Cards(T),
        .. GladiatorBeastDefinitions.Cards(T),
        .. GravekeeperDefinitions.Cards(T),
        .. NaturiaDefinitions.Cards(T),
        .. PhaseTriggerDefinitions.Cards(T),
        .. CountedStatDefinitions.Cards(T),
        .. HandSummonDefinitions.Cards(T),
        .. NoTributeSummonDefinitions.Cards(T)
    ]);

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["ShortTextCardFamilyTests", "CardPoolTests"], Cards);
}
