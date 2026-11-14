using TagForceSpecial.Core;

namespace TagForceSpecial.Effects;

/// <summary>
/// The Apoqliphort monsters. Like the Egyptian Gods they are kept apart from other families because their Normal Summon needs a registered
/// Tribute procedure, which every engine loading them must also receive.
/// </summary>
public static class ApoqliphortFamily
{
    public const string FamilyId = "apoqliphort-definition";
    public const string BatchId = "tfsp-family-batch-10d-04";

    private static readonly CardTable T = new("apoqliphort");

    /// <summary>The "Qli" monsters, which can be Tributed for an Apoqliphort.</summary>
    public static IReadOnlyList<uint> QliMonsters { get; } = Array.AsReadOnly(new uint[] { 11353, 11354, 11355, 11356, 11357, 11358, 11540, 11541, 11542, 11543 });

    /// <summary>
    /// Apoqliphort Skybase: 3 "Qli" Tributes, no Special Summon; once Normal Summoned or Set it is unaffected by Spell and Trap effects and by
    /// activated effects of lower-Level monsters; once per turn it takes control of a target monster until the End Phase.
    /// </summary>
    private static AuthoredCard Skybase() =>
        T.Ignition(11543, "Apoqliphort Skybase", [new TakeControlStep(ControlDuration.UntilEndPhase)], targets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)],
            oncePerTurn: true, rules: new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true, AllowsTributeForSummon: true,
                RequiresRegisteredTributeProcedure: true, CannotBeSpecialSummoned: true)) with
        {
            TributeProcedures = [new TributeProcedureProfile("apoqliphort-11543-three-qli-v1", 11543, 3, QliMonsters)],
            Stats = new MonsterStatProfile("apoqliphort-11543-stats-v1", 11543) { UnaffectedByLowerMonsters = true, UnaffectedBySpellTraps = true }
        };

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly(new[] { Skybase() });

    public static CardFamily Family { get; } = CardTable.Family(FamilyId, BatchId, ["SpecialMonsterTests", "CardPoolTests"], Cards);
}
