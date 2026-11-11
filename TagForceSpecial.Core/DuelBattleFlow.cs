namespace TagForceSpecial.Core;

public enum BattleStep { Start, Battle, Damage, End }
public enum DamageStep { Start, BeforeCalculation, Calculation, AfterCalculation, End }
[Flags]
public enum BattleTiming
{
    None = 0, Start = 1, BattleStep = 2, AttackDeclaration = 4, DamageStart = 8,
    BeforeCalculation = 16, Calculation = 32, AfterCalculation = 64, DamageEnd = 128,
    End = 256, All = 511
}
public enum BattleAttackCancellation { Negated, AttackerUnavailable, ReplayDeclined }
public sealed record BattleResponseWindow(long DecisionId, BattleTiming Timing, PlayerId Responder,
    int ConsecutivePasses = 0)
{
    public bool Closed => ConsecutivePasses == 2;
}
public sealed record BattleDamageCalculation(int Damage, PlayerId DamagedPlayer, bool DestroyAttacker, bool DestroyDefender);

public sealed record PendingBattleAttack
{
    public long Id { get; internal init; }
    public MonsterBinding Attacker { get; internal init; }
    public MonsterBinding? Defender { get; internal init; }
    public IReadOnlyList<MonsterBinding> DeclaredDefenders { get; internal init; }
    public bool ReplayRequired { get; internal init; }
    public BattleAttackCancellation? Cancellation { get; internal init; }
    public MonsterBinding? RevealedDefender { get; internal init; }
    public BattleDamageCalculation? Calculation { get; internal init; }

    internal PendingBattleAttack(long id, MonsterBinding attacker, MonsterBinding? defender,
        IEnumerable<MonsterBinding> defenders)
    {
        Id = id; Attacker = attacker; Defender = defender;
        DeclaredDefenders = Array.AsReadOnly(defenders.ToArray());
    }
}
public sealed record BattleState(BattleStep Step, DamageStep? DamageStep, BattleResponseWindow Window,
    PendingBattleAttack? Attack = null, PendingReplacement? Replacement = null);

public sealed record AdvanceBattle(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record PassBattleResponse(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record ChooseAttackReplay(PlayerId Player, long DecisionId, int? TargetZone = null,
    bool Cancel = false) : DuelCommand(Player);
/// <summary>A set card activated in an open battle window.</summary>
public sealed record ActivateBattleResponse(PlayerId Player, long DecisionId, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0) : DuelCommand(Player);
/// <summary>The turn player's Quick-Play Spell activated from the hand in an open battle window.</summary>
public sealed record ActivateHandBattleResponse(PlayerId Player, long DecisionId, int CardInstanceId, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0) : DuelCommand(Player);
public sealed record BattleStepChanged(BattleStep? From, BattleStep To) : DuelEvent;
public sealed record DamageStepChanged(DamageStep? From, DamageStep To) : DuelEvent;
public sealed record BattleResponseRequested(BattleResponseWindow Window) : DuelEvent;
public sealed record BattleResponsePassed(PlayerId Player) : DuelEvent;
public sealed record BattleWindowClosed(BattleTiming Timing) : DuelEvent;
public sealed record AttackReplayRequested(long AttackId) : DuelEvent;
public sealed record AttackReplayChosen(long AttackId, int? TargetZone) : DuelEvent;
public sealed record AttackCancelled(long AttackId, BattleAttackCancellation Reason) : DuelEvent;
public sealed record BattleDamageCalculated(long AttackId, BattleDamageCalculation Calculation) : DuelEvent;
public sealed record BattleDamageCalculationSkipped(long AttackId) : DuelEvent;
public sealed record BattleAttackFinished(long AttackId) : DuelEvent;
public sealed record BattleFlipEffectWindowOpened(MonsterBinding Monster) : DuelEvent;
