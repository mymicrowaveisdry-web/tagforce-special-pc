using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>
/// The Egyptian Gods. They are kept apart from other families because their Normal Summon needs a registered Tribute procedure,
/// which every engine loading them must also receive.
/// </summary>
public static class EgyptianGodFamily
{
    public const string FamilyId = "egyptian-god-definition";
    public const string BatchId = "tfsp-family-batch-10d-02";

    private static readonly CardTable T = new("egyptian-god");

    /// <summary>Obelisk the Tormentor: Tribute 2 monsters (it may Tribute itself) to destroy every monster the opponent controls; it then cannot attack this turn.</summary>
    private static AuthoredCard Obelisk(uint id, uint getterKind) => T.EgyptianGod(id, "Obelisk the Tormentor", getterKind,
        new([new DestroyAllStep(FieldCardKind.Monster, TargetSide.Opponent),
            new ApplyRuleLockStep(new RuleLock(LockedAction.Attack, TargetSide.Own, new CardIdFilter([id])))],
            new TributeCost(Count: 2)),
        new RuleLock(LockedAction.BeTargeted, Scope: LockScope.Self));

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly(new[]
    {
        Obelisk(4998, 11),
        Obelisk(3858, 1)
    });

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["PhaseControlTests", "CardPoolTests"], Cards);
}
