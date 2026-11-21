namespace TagForceSpecial.Core.Views;

public enum ActionKind
{
    Draw, Summon, ActivateSpell, ActivateSetSpell, SetMonster, SetSpellTrap, Position, Attack,
    Discard, Phase, EndTurn, ChooseCard, ChoosePosition, ChooseZone, ChooseTarget,
    ChainResponse, ChainPass, ResolveChain, BattleResponse, BattlePass, AdvanceBattle, ReplayAttack, CancelReplay, FinishChoice,
    OrderTriggers, DeclineTriggers, MonsterEffect, Unequip, Upkeep, CardEffect
}
public enum ActionAvailability { Ready, Waiting, Finished, Unsupported }

// Handles that address commands kept inside DuelActionSource.
public sealed record AuthorizedFieldTarget(PlayerId Player, FieldLocation Location, int Zone);
public sealed record LegalAction(int Token, string Key, ActionKind Kind, string Label,
    int? CardInstanceId = null, int? Zone = null, int? TargetZone = null,
    BattlePosition? Position = null, DuelPhase? Phase = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AuthorizedFieldTarget? FieldTarget { get; init; }

    /// <summary>The card a decision option is about, for its chooser only: a card to choose (from any of the chooser's zones, the Deck
    /// included), or the card being positioned (picked earlier in the same sequence).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardView? Subject { get; init; }

    /// <summary>Where a choose-card option's card is (the card list's header icon).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DecisionCardSource? SubjectSource { get; init; }
}
public sealed record AuthorizedDecision(long Id, string Kind, IReadOnlyList<CardView> Cards);
public sealed record DuelObservation(long Revision, PlayerId Player, PlayerId Actor, PlayerId ActivePlayer,
    int Turn, DuelPhase Phase, PlayerView Self, PlayerView Opponent, AuthorizedDecision? Decision,
    ActionAvailability Availability, string Diagnostic, IReadOnlyList<LegalAction> Actions, DuelOutcome? Outcome);
public sealed record ActionExecution(bool Accepted, DuelError Error, long Revision, ActionKind? Kind);
