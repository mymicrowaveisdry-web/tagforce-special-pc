namespace TagForceSpecial.Core;

/// <summary>Explicit research operations, not inferred from card text or category codes.</summary>
public enum ChainOperation
{
    DestroyOpposingFaceUpMonsters, DestroyBoundSpellTrap, SummonFourTokens,
    NegateActivation, NegateEffect
}
public enum ChainSpellSpeed { One = 1, Two = 2, Three = 3 }
public enum ChainNegation { None, Effect, Activation }
public enum ChainSkipReason { ActivationNegated, EffectNegated, TargetUnavailable }

public sealed class ChainEffectProfile
{
    // Profiles reference catalog identities; PSP packed-ID limits belong to import validation.
    public string EffectId { get; }
    public uint CardId { get; }
    public ChainOperation Operation { get; }
    public IReadOnlyList<uint> TokenCardIds { get; }

    public ChainEffectProfile(string effectId, uint cardId, ChainOperation operation,
        IEnumerable<uint>? tokenCardIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (operation is not (ChainOperation.DestroyOpposingFaceUpMonsters or
            ChainOperation.DestroyBoundSpellTrap or ChainOperation.SummonFourTokens))
            throw new ArgumentOutOfRangeException(nameof(operation));
        var tokens = (tokenCardIds ?? []).ToArray();
        if (operation == ChainOperation.SummonFourTokens
            ? tokens.Length != 4 || tokens.Any(id => id == 0) || tokens.Distinct().Count() != 4
            : tokens.Length != 0)
            throw new ArgumentException("Only the four-token response accepts exactly four distinct token IDs.", nameof(tokenCardIds));
        EffectId = effectId;
        CardId = cardId;
        Operation = operation;
        TokenCardIds = Array.AsReadOnly(tokens);
    }
}

public sealed record ChainLink(int Number, CommittedActivation Activation, long SourcePlacementId,
    ChainOperation Operation, ChainSpellSpeed SpellSpeed = ChainSpellSpeed.One,
    int? TargetLinkNumber = null, ChainNegation Negation = ChainNegation.None,
    bool DestroyNegatedSource = false)
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
    internal PendingChain(long decisionId, IEnumerable<ChainLink> links, PlayerId responder,
        int consecutivePasses = 0, int nextLink = 0)
    {
        DecisionId = decisionId;
        Links = Array.AsReadOnly(links.ToArray());
        Responder = responder;
        ConsecutivePasses = consecutivePasses;
        NextLink = nextLink;
    }
}

public sealed record PassChainResponse(PlayerId Player, long DecisionId) : DuelCommand(Player);
/// <summary>Activate a previously set card through an explicitly registered response profile.</summary>
public sealed record ActivateChainResponse(PlayerId Player, long DecisionId, int SpellTrapZone) : DuelCommand(Player);
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
public sealed record SpellTrapDestroyed(PlayerId Player, int Zone, CardInstance Card) : DuelEvent;
public sealed record TokenSpecialSummoned(PlayerId Player, int Zone, FieldMonster Monster) : DuelEvent;
