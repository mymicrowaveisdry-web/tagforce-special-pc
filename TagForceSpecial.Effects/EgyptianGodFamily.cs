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

    /// <summary>
    /// Slifer the Sky Dragon: 1000 ATK and DEF for each card in its controller's hand; an opponent's monster Normal or Special Summoned in
    /// face-up Attack Position loses 2000 ATK, then is destroyed if that made its ATK 0.
    /// </summary>
    private static AuthoredCard Slifer(uint id, uint getterKind) => T.WithTrigger(
        T.EgyptianGod(id, "Slifer the Sky Dragon", getterKind) with
        {
            Stats = new MonsterStatProfile($"egyptian-god-{id}-stats-v1", id) { UnknownIsZero = true, PerHandCard = 1000 }
        },
        "weaken", [MonsterEventKind.NormalSummoned, MonsterEventKind.SpecialSummoned], EffectRelation.Opponent, [new WeakenEventMonsterStep(2000, DestroyAtZero: true)],
        subject: new FaceUpAttackFilter());

    /// <summary>
    /// The Winged Dragon of Ra: cannot be Special Summoned; when Normal Summoned it can pay LP down to 100 to gain that much ATK and DEF, and it can pay
    /// 1000 LP to destroy a target monster.
    /// </summary>
    private static AuthoredCard Ra(uint id, uint getterKind) => T.WithTrigger(
        T.EgyptianGod(id, "The Winged Dragon of Ra", getterKind, new([new DestroyTargetsStep()], new PayLifePointsCost(1000), [new FieldTarget(FieldCardKind.Monster)]),
            exceptions: "The LP for its ATK are paid as that effect resolves, not as its cost.", sendIfSpecialSummoned: false, cannotBeSpecialSummoned: true) with
        {
            Stats = new MonsterStatProfile($"egyptian-god-{id}-stats-v1", id) { UnknownIsZero = true }
        },
        "summoned", [MonsterEventKind.NormalSummoned], EffectRelation.Self, [new PayDownToGainStep(100)], TriggerRequirement.Optional);

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly(new[]
    {
        Obelisk(4998, 11),
        Obelisk(3858, 1),
        Slifer(4999, 11),
        Slifer(3861, 1),
        Ra(5000, 11),
        Ra(3859, 1)
    });

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["PhaseControlTests", "CardPoolTests"], Cards);
}
