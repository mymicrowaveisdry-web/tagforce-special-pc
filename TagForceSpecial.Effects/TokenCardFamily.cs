using System.Collections.Frozen;
using TagForceSpecial.Core;
using static TagForceSpecial.Effects.MonsterAttributes;

namespace TagForceSpecial.Effects;

/// <summary>
/// Cards that Summon Tokens with rules of their own. They are kept apart from other families because the Tokens' rules and locks
/// travel with the family, so every engine loading these cards needs the Tokens in its catalog.
/// </summary>
public static class TokenCardFamily
{
    public const string FamilyId = "token-card-definition";
    public const string BatchId = "tfsp-family-batch-10d-03";

    private static readonly CardTable T = new("token-card");
    private const uint DoomsdayToken = 3940;

    /// <summary>The two Fluff Token entries Dandylion Summons.</summary>
    public static IReadOnlyList<uint> FluffTokens { get; } = Array.AsReadOnly(new uint[] { 3925, 3926 });

    /// <summary>Tokens this family Summons.</summary>
    public static IReadOnlyList<uint> TokenIds { get; } = Array.AsReadOnly(new uint[] { DoomsdayToken, 3925, 3926 });

    private static readonly RuleLock NotForSummonThisTurn = new(LockedAction.BeTributedForSummon, Scope: LockScope.Self, SummonedThisTurnOnly: true);

    public static IReadOnlyList<AuthoredCard> Cards { get; } = Array.AsReadOnly(new[]
    {
        T.WithLock(T.QuickPlay(7454, "Fires of Doomsday", [new SummonTokensStep([DoomsdayToken, DoomsdayToken], BattlePosition.FaceUpDefense)], locksOtherSummons: true),
            new RuleLock(LockedAction.BeTributedForSummon, Scope: LockScope.Self, ExceptSummoning: new MonsterAttributeFilter(Dark)), DoomsdayToken),
        T.WithLock(T.WithLock(T.Trigger(6654, "Dandylion", MonsterEventKind.SentToGraveyard, [new SummonTokensStep(FluffTokens, BattlePosition.FaceUpDefense)],
                location: TriggerSourceLocation.GraveyardSelf, timing: TriggerTiming.If),
            NotForSummonThisTurn, FluffTokens[0]), NotForSummonThisTurn, FluffTokens[1])
    });

    private static readonly CardFamily Base = CardTable.Family(FamilyId, BatchId, ["LockRefinementTests", "CardPoolTests"], Cards);

    public static CardFamily Family { get; } = Base with
    {
        Rules = Base.Rules.Concat(TokenIds.Select(id => new KeyValuePair<uint, DuelCardRules>(id, new(CardCategory.Monster, AllowsBasicBattle: true, AllowsTributeForSummon: true, IsToken: true))))
            .ToFrozenDictionary(),
        RequiredCardIds = TokenIds
    };
}
