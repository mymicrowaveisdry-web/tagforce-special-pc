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
    PendingBattleAttack? Attack = null);

public sealed record AdvanceBattle(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record PassBattleResponse(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record ChooseAttackReplay(PlayerId Player, long DecisionId, int? TargetZone = null,
    bool Cancel = false) : DuelCommand(Player);
/// <summary>Explicit choices for an opted-in set response; hand summoning requires a card and empty zone.</summary>
public sealed record ActivateBattleResponse(PlayerId Player, long DecisionId, int SpellTrapZone,
    int? SummonedCardInstanceId = null, int? MonsterZone = null) : DuelCommand(Player);
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

public enum BattleResponseOperation { NegateAttack, DestroyAttacker, SpecialSummonFromHand, ModifyCombatantStatistics }
public enum BattleCombatant { Attacker, Defender }

/// <summary>Trusted, default-off response legality; explicit timing is never inferred from card text.</summary>
public sealed class BattleResponseProfile
{
    public string EffectId { get; }
    public uint CardId { get; }
    public BattleResponseOperation Operation { get; }
    public BattleTiming Windows { get; }
    public ChainSpellSpeed SpellSpeed { get; }
    public IReadOnlyList<uint> SummonableCardIds { get; }
    public BattlePosition SummonPosition { get; }
    public BattleCombatant Combatant { get; }
    public int AttackDelta { get; }
    public int DefenseDelta { get; }

    public BattleResponseProfile(string effectId, uint cardId, BattleResponseOperation operation,
        BattleTiming windows, ChainSpellSpeed spellSpeed = ChainSpellSpeed.Two,
        IEnumerable<uint>? summonableCardIds = null, BattlePosition summonPosition = BattlePosition.FaceUpDefense,
        BattleCombatant combatant = BattleCombatant.Attacker, int attackDelta = 0, int defenseDelta = 0,
        bool damageCalculationException = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        if (cardId == 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        if (!Enum.IsDefined(operation) || !Enum.IsDefined(combatant) || windows == BattleTiming.None || (windows & ~BattleTiming.All) != 0 ||
            spellSpeed is not (ChainSpellSpeed.Two or ChainSpellSpeed.Three) ||
            summonPosition is not (BattlePosition.FaceUpAttack or BattlePosition.FaceUpDefense))
            throw new ArgumentException("Invalid battle response metadata.");
        if (windows.HasFlag(BattleTiming.Calculation) && !damageCalculationException)
            throw new ArgumentException("During-calculation activation requires an explicit exception.");
        if (operation == BattleResponseOperation.NegateAttack && (windows & ~(BattleTiming.AttackDeclaration | BattleTiming.BattleStep)) != 0)
            throw new ArgumentException("This attack-negation primitive only supports the Battle Step.");
        if (operation == BattleResponseOperation.ModifyCombatantStatistics &&
            ((attackDelta == 0 && defenseDelta == 0) || (windows & ~(BattleTiming.DamageStart | BattleTiming.BeforeCalculation | BattleTiming.Calculation)) != 0 ||
                windows.HasFlag(BattleTiming.Calculation) && !damageCalculationException) ||
            operation != BattleResponseOperation.ModifyCombatantStatistics && (attackDelta != 0 || defenseDelta != 0))
            throw new ArgumentException("Stat modifiers require an explicit supported damage timing/exception.");
        var ids = (summonableCardIds ?? []).ToArray();
        if (operation == BattleResponseOperation.SpecialSummonFromHand
            ? ids.Length == 0 || ids.Any(id => id == 0) || ids.Distinct().Count() != ids.Length : ids.Length != 0)
            throw new ArgumentException("Only the hand-summon operation accepts a nonempty catalog allowlist.");
        EffectId = effectId; CardId = cardId; Operation = operation; Windows = windows; SpellSpeed = spellSpeed;
        SummonableCardIds = Array.AsReadOnly(ids); SummonPosition = summonPosition; Combatant = combatant;
        AttackDelta = attackDelta; DefenseDelta = defenseDelta;
    }
}
