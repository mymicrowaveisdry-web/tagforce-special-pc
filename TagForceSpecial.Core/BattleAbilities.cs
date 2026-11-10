namespace TagForceSpecial.Core;

/// <summary>Continuous battle permissions and requirements; attack counts from several sources use the highest value.</summary>
public sealed record BattleAbilities(bool Piercing = false, bool DirectAttack = false, int AttacksPerBattlePhase = 1,
    bool AttackAll = false, bool MustAttack = false);

/// <summary>A face-up source grants abilities to itself, its equipped monster, or matching monsters on a side.</summary>
public sealed record BattleAbilityProfile(string EffectId, uint CardId, BattleAbilities Abilities,
    LockScope Scope = LockScope.Self, TargetSide Side = TargetSide.Own, CardFilter? Cards = null);

internal sealed record BattleAttackUse(long Id, int TurnNumber, MonsterBinding Attacker, bool DeclaredDirect,
    bool BecameDirect, IReadOnlyList<MonsterBinding> Targets);

public sealed partial class DuelEngine
{
    private BattleAbilities GetBattleAbilities(DuelState state, PlayerId player, FieldMonster monster)
    {
        if (battleAbilityProfiles.Count == 0) return new();
        bool piercing = false, direct = false, all = false, must = false;
        int attacks = 1;
        void Add(BattleAbilityProfile profile, PlayerId controller, MonsterBinding? self, MonsterBinding? equipped)
        {
            if (profile.Side != TargetSide.Either && (profile.Side == TargetSide.Own) != (player == controller)) return;
            if (profile.Cards is { } filter && !MatchesFilter(monster.Card, filter)) return;
            var binding = profile.Scope == LockScope.Self ? self : equipped;
            if (profile.Scope != LockScope.Side && (binding is null || binding.Card != monster.Card || binding.PlacementId != monster.PlacementId)) return;
            var abilities = profile.Abilities;
            piercing |= abilities.Piercing; direct |= abilities.DirectAttack;
            all |= abilities.AttackAll; must |= abilities.MustAttack;
            attacks = Math.Max(attacks, abilities.AttacksPerBattlePhase);
        }
        var negated = CalculateNegatedMonsters(state, state.EventEffects.TimedNegations);
        foreach (var (binding, source) in FieldMonsters(state))
            if (source.Position != BattlePosition.FaceDownDefense && !source.BattleDestroyed &&
                !negated.Contains(binding) && battleAbilityProfiles.TryGetValue(source.Card.GameCardId, out var profiles))
                foreach (var profile in profiles) Add(profile, binding.Player, binding, null);
        bool spellsNegated = IsSpellsNegated(state);
        foreach (var (controller, _, field) in ActiveCards(state))
            if (!(spellsNegated && IsSpellCard(field)) && battleAbilityProfiles.TryGetValue(field.Card.GameCardId, out var profiles))
                foreach (var profile in profiles) Add(profile, controller, null, field.EquippedTo);
        return new(piercing, direct, attacks, all, must);
    }

    private BattleAttackUse[] AttacksThisTurn(DuelState state, FieldMonster attacker) =>
        state.Bookkeeping.BattleAttacks.Where(a => a.TurnNumber == state.TurnNumber &&
            a.Attacker.Card == attacker.Card && a.Attacker.PlacementId == attacker.PlacementId).ToArray();

    private DuelError AttackAllowanceError(DuelPlayerState player, FieldMonster attacker, int? targetZone, long? replayId = null)
    {
        var uses = AttacksThisTurn(State, attacker).Where(a => a.Id != replayId).ToArray();
        int count = uses.Length;
        // Retain the legacy marker for pre-existing snapshots that have no detailed history.
        if (count == 0 && replayId is null && attacker.LastAttackTurn == State.TurnNumber) count = 1;
        var abilities = GetBattleAbilities(State, player.Id, attacker);
        if (count < abilities.AttacksPerBattlePhase) return DuelError.None;
        if (!abilities.AttackAll || targetZone is null || uses.Any(a => a.DeclaredDirect || a.BecameDirect)) return DuelError.AlreadyAttacked;
        var opponent = State.GetPlayer(Other(player.Id));
        var defender = opponent.MonsterZones[targetZone.Value];
        if (defender is null) return DuelError.MonsterZoneEmpty;
        return uses.Any(a => a.Targets.Any(t => t.Card == defender.Card && t.PlacementId == defender.PlacementId))
            ? DuelError.AlreadyAttacked : DuelError.None;
    }

    private DuelState RecordBattleAttack(DuelState state, long id, MonsterBinding attacker, MonsterBinding? target, bool replay = false)
    {
        var uses = state.Bookkeeping.BattleAttacks.Where(a => a.TurnNumber == state.TurnNumber).ToList();
        if (replay)
        {
            int index = uses.FindIndex(a => a.Id == id);
            if (index < 0) throw new InvalidOperationException("An attack replay requires a recorded declaration.");
            var old = uses[index];
            uses[index] = old with { BecameDirect = old.BecameDirect || target is null,
                Targets = target is null ? old.Targets : Array.AsReadOnly(old.Targets.Append(target).Distinct().ToArray()) };
        }
        else uses.Add(new(id, state.TurnNumber, attacker, target is null, target is null,
            Array.AsReadOnly(target is null ? Array.Empty<MonsterBinding>() : [target])));
        return state.WithBookkeeping(state.Bookkeeping with { BattleAttacks = uses.AsReadOnly() });
    }

    private bool RequiredAttackPending()
    {
        if (State.TurnNumber == 1) return false;
        var player = State.GetPlayer(State.ActivePlayer);
        var opponent = State.GetPlayer(Other(player.Id));
        for (int z = 0; z < player.MonsterZones.Count; z++)
        {
            if (player.MonsterZones[z] is not { Position: BattlePosition.FaceUpAttack } monster ||
                monster.LastAttackTurn == State.TurnNumber || AttacksThisTurn(State, monster).Length != 0 ||
                !GetBattleAbilities(State, player.Id, monster).MustAttack || !rules[monster.Card.GameCardId].AllowsBasicBattle ||
                GetMonsterStatistics(State, player.Id, z)?.Attack is null) continue;
            bool Legal(int? target) => AttackAllowanceError(player, monster, target) == DuelError.None &&
                AttackRestrictionError(player, opponent, monster, target) == DuelError.None &&
                (target is not null || CanAttackDirectly(opponent, monster));
            if (Legal(null) || Enumerable.Range(0, 5).Any(i => opponent.MonsterZones[i] is { } defender &&
                    rules[defender.Card.GameCardId].AllowsBasicBattle &&
                    (defender.Position == BattlePosition.FaceUpAttack ? catalog[defender.Card.GameCardId].Statistics.Attack : catalog[defender.Card.GameCardId].Statistics.Defense) is not null && Legal(i))) return true;
        }
        return false;
    }
}
