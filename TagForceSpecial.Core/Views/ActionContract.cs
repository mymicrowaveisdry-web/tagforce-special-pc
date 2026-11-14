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
public sealed record LegalAction(int Token, string Key, ActionKind Kind, string Label,
    int? CardInstanceId = null, int? Zone = null, int? TargetZone = null,
    BattlePosition? Position = null, DuelPhase? Phase = null);
public sealed record AuthorizedDecision(long Id, string Kind, IReadOnlyList<CardView> Cards);
public sealed record DuelObservation(long Revision, PlayerId Player, PlayerId Actor, PlayerId ActivePlayer,
    int Turn, DuelPhase Phase, PlayerView Self, PlayerView Opponent, AuthorizedDecision? Decision,
    ActionAvailability Availability, string Diagnostic, IReadOnlyList<LegalAction> Actions, DuelOutcome? Outcome);
public sealed record ActionExecution(bool Accepted, DuelError Error, long Revision, ActionKind? Kind);
