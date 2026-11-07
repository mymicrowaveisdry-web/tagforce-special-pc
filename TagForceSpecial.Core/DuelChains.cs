namespace TagForceSpecial.Core;

/// <summary>How a chain link resolves. Card effects resolve as <see cref="Definition"/> steps.</summary>
public enum ChainOperation
{
    /// <summary>Negation responses (<see cref="ChainResponseProfile"/>).</summary>
    NegateActivation, NegateEffect,
    TriggerEffect, BattleResponse, SummonNegation, EffectSummon, ActivatePendulumScale,
    /// <summary>Resolves the ordered steps of the link's <see cref="EffectDefinition"/>.</summary>
    Definition
}
public enum ChainSpellSpeed { One = 1, Two = 2, Three = 3 }
public enum ChainNegation { None, Effect, Activation }
public enum ChainSkipReason { ActivationNegated, EffectNegated, TargetUnavailable, SourceEffectsNegated, SummonUnavailable, NoMatchingCard }

public sealed record ChainLink(int Number, CommittedActivation Activation, long SourcePlacementId,
    ChainOperation Operation, ChainSpellSpeed SpellSpeed = ChainSpellSpeed.One,
    int? TargetLinkNumber = null, ChainNegation Negation = ChainNegation.None,
    bool DestroyNegatedSource = false, TriggerActivation? Trigger = null,
    BattleResponseProfile? BattleEffect = null, long? BattleAttackId = null, MonsterBinding? BattleTarget = null,
    CardInstance? BattleSummonCard = null, int? BattleMonsterZone = null, long? SummonAttemptId = null,
    bool IsPendulumScale = false)
{
    public bool ActivationNegated => Negation == ChainNegation.Activation;
    public bool EffectNegated => Negation != ChainNegation.None;
}

/// <summary>Full-information immutable slice. IDs are accepted-state revisions.</summary>
public sealed class PendingChain
{
    public long DecisionId { get; }
    public IReadOnlyList<ChainLink> Links { get; }
    public PlayerId Responder { get; }
    public int ConsecutivePasses { get; }
    /// <summary>Zero means response window; otherwise the next link to resolve, in reverse order.</summary>
    public int NextLink { get; }
    public PendingEffectSummon? SummonDecision { get; }
    private readonly PendingDefinitionChoice? definitionChoice;
    /// <summary>A definition step paused for its resolution-time Special Summon choice.</summary>
    public PendingDefinitionSummon? DefinitionSummon => definitionChoice as PendingDefinitionSummon;
    /// <summary>A definition step paused for its resolution-time Deck card choice.</summary>
    public PendingDefinitionCardChoice? DefinitionCardChoice => definitionChoice as PendingDefinitionCardChoice;
    internal PendingDefinitionChoice? DefinitionChoice => definitionChoice;
    internal PendingChain(long decisionId, IEnumerable<ChainLink> links, PlayerId responder,
        int consecutivePasses = 0, int nextLink = 0, PendingEffectSummon? summonDecision = null,
        PendingDefinitionChoice? definitionChoice = null)
    {
        DecisionId = decisionId;
        Links = Array.AsReadOnly(links.ToArray());
        Responder = responder;
        ConsecutivePasses = consecutivePasses;
        NextLink = nextLink;
        SummonDecision = summonDecision;
        this.definitionChoice = definitionChoice;
    }
}

public sealed record PassChainResponse(PlayerId Player, long DecisionId) : DuelCommand(Player);
/// <summary>Activate a previously set card as the next chain link (or in an open window).</summary>
public sealed record ActivateChainResponse(PlayerId Player, long DecisionId, int SpellTrapZone,
    int? SummonedCardInstanceId = null, int? MonsterZone = null) : DuelCommand(Player);
/// <summary>Explicit PC stepping boundary; the active turn player advances resolution.</summary>
public sealed record ResolveNextChainLink(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record ChainLinkAdded(ChainLink Link) : DuelEvent;
public sealed record ChainResponseRequested(PlayerId Player, long DecisionId) : DuelEvent;
public sealed record ChainResponsePassed(PlayerId Player) : DuelEvent;
public sealed record ChainResolutionStarted(int LinkCount) : DuelEvent;
public sealed record ChainLinkResolved(int Number, string EffectId) : DuelEvent;
public sealed record ChainLinkNegated(int Number, int ByLinkNumber, ChainNegation Negation) : DuelEvent;
public sealed record ChainLinkSkipped(int Number, ChainSkipReason Reason) : DuelEvent;
public sealed record ChainResponseActivated(PlayerId Player, string EffectId, CardInstance Card, int Zone) : DuelEvent;
public sealed record ChainTrapSentToGraveyard(PlayerId Player, CardInstance Card, int Zone) : DuelEvent;
public sealed record ChainCompleted : DuelEvent;
public sealed record SpellTrapDestroyed(PlayerId Player, int Zone, CardInstance Card, bool IsPendulumZone = false) : DuelEvent;
public sealed record TokenSpecialSummoned(PlayerId Player, int Zone, FieldMonster Monster) : DuelEvent;
