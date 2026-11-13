using System.Collections.Frozen;

namespace TagForceSpecial.Core;

/// <summary>
/// A monster's own rules for its statistics: "?" ATK and DEF counting as 0, ATK and DEF gained per card in its controller's hand, and a
/// Level and original ATK that change when it is Normal Summoned without Tributing or Special Summoned.
/// </summary>
public sealed record MonsterStatProfile(string EffectId, uint CardId)
{
    public bool UnknownIsZero { get; init; }
    /// <summary>ATK and DEF gained for each card in its controller's hand while it is face-up and its effects apply.</summary>
    public int PerHandCard { get; init; }
    /// <summary>The Level it has after a Normal Summon without Tributing or a Special Summon.</summary>
    public uint? ReducedLevel { get; init; }
    /// <summary>The original ATK it has after a Normal Summon without Tributing or a Special Summon.</summary>
    public int? ReducedAttack { get; init; }
    /// <summary>Once Normal Summoned or Set, it is unaffected by activated effects of monsters whose original Level or Rank is lower than its current Level.</summary>
    public bool UnaffectedByLowerMonsters { get; init; }
    /// <summary>Once Normal Summoned or Set, it is unaffected by Spell and Trap effects.</summary>
    public bool UnaffectedBySpellTraps { get; init; }
    /// <summary>ATK and DEF gained for each card matching <see cref="CountFilter"/> in <see cref="CountZone"/>, while it is face-up and its effects apply.</summary>
    public int AttackPerCount { get; init; }
    public int DefensePerCount { get; init; }
    public CardFilter? CountFilter { get; init; }
    public StatCountZone CountZone { get; init; }
}

/// <summary>Where a monster's per-card stat gain counts: its controller's face-up monsters or Graveyard, or banished monsters of both players.</summary>
public enum StatCountZone { OwnFaceUpMonsters, OwnGraveyard, BanishedMonsters }

/// <summary>The activating player adds 1 matching card from their Graveyard to their hand, without targeting it.</summary>
public sealed record AddFromGraveyardToHandStep(CardFilter Filter) : EffectStep;

/// <summary>A monster in face-up Attack Position on the field.</summary>
public sealed record FaceUpAttackFilter : CardFilter;

/// <summary>Changes the ATK and DEF of each bound target for as long as it stays face-up on the field.</summary>
public sealed record ModifyTargetsStep(int Attack, int Defense = 0) : EffectStep;

/// <summary>
/// The monster a trigger's event was about loses <paramref name="Attack"/> ATK for as long as it stays on the field; with
/// <paramref name="DestroyAtZero"/>, it is then destroyed if that made its ATK 0.
/// </summary>
public sealed record WeakenEventMonsterStep(int Attack, bool DestroyAtZero) : EffectStep;

/// <summary>The activating player pays LP down to <paramref name="Remaining"/>; the source monster gains that much ATK and DEF.</summary>
public sealed record PayDownToGainStep(int Remaining) : EffectStep;

/// <summary>
/// Every face-up monster the activating player controls that matches <paramref name="Filter"/> becomes the declared Level, which must be from
/// <paramref name="MinLevel"/> to <paramref name="MaxLevel"/>.
/// </summary>
public sealed record SetDeclaredLevelStep(CardFilter Filter, int MinLevel = 1, int MaxLevel = 12) : EffectStep;

/// <summary>The scale whose Pendulum Effect resolves lowers its Pendulum Scale by <paramref name="By"/>, not below <paramref name="Minimum"/>, while it stays.</summary>
public sealed record ReduceSourceScaleStep(int By, int Minimum = 1) : EffectStep;

/// <summary>Destroys every monster the activating player controls whose Level is higher than the resolving scale's Pendulum Scale, except matching ones.</summary>
public sealed record DestroyAboveSourceScaleStep(CardFilter Except) : EffectStep;

/// <summary>A face-up card in the Extra Deck was added to its owner's hand.</summary>
public sealed record CardAddedFromExtraDeck(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;

/// <summary>The activating player adds 1 matching face-up monster from their Extra Deck to their hand.</summary>
public sealed record AddFromFaceUpExtraDeckStep(CardFilter Filter) : EffectStep;

/// <summary>The activating player adds 1 matching card from their Deck or Graveyard to their hand; a card from the Deck shuffles it.</summary>
public sealed record AddFromDeckOrGraveyardStep(CardFilter Filter) : EffectStep;

public sealed partial class DuelEngine
{
    private FrozenDictionary<uint, MonsterStatProfile> statProfiles = FrozenDictionary<uint, MonsterStatProfile>.Empty;

    /// <summary>Whether this monster was Normal Summoned without Tributing or Special Summoned, for a reduced Level and ATK.</summary>
    /// <summary>Negating its effects gives it back its printed Level and ATK (Qliphort Carrier ruling).</summary>
    private static bool ReducedBySummon(DuelState state, MonsterBinding binding, FieldMonster monster) =>
        !state.EventEffects.NegatedMonsters.Contains(binding) && ReducedSummon(state, binding, monster);

    private static bool ReducedSummon(DuelState state, MonsterBinding binding, FieldMonster monster) =>
        monster.SummonTributes == 0 && monster.Position != BattlePosition.FaceDownDefense && state.SummonHistory.Any(h => h.Card == binding.Card &&
            h.PlacementId == binding.PlacementId && h.Method == SummonMethod.Normal) || WasSpecialSummoned(state, binding);

    /// <summary>The current Pendulum Scale of the card in a Pendulum Zone (0 left, 1 right); null for an empty zone.</summary>
    public uint? GetPendulumScale(PlayerId player, int zone)
    {
        if (player is not (PlayerId.One or PlayerId.Two)) throw new ArgumentOutOfRangeException(nameof(player));
        if (zone is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(zone));
        return State.GetPlayer(player).PendulumZones[zone] is { } scale ? CurrentScale(State, scale, left: zone == 0) : null;
    }

    /// <summary>The current Level of the monster in a zone, after effects that change it; null for an empty zone.</summary>
    public uint? GetMonsterLevel(PlayerId player, int zone)
    {
        if (player is not (PlayerId.One or PlayerId.Two)) throw new ArgumentOutOfRangeException(nameof(player));
        if (zone is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(zone));
        return State.GetPlayer(player).MonsterZones[zone] is { } monster ? CurrentLevel(monster.Card) : null;
    }

    /// <summary>The Level a monster on the field has now; elsewhere, its printed Level.</summary>
    private uint CurrentLevel(CardInstance card)
    {
        uint printed = catalog[card.GameCardId].Statistics.Level;
        var state = IdentityState;
        var (binding, monster) = FieldMonsters(state).FirstOrDefault(e => e.Monster.Card == card);
        if (monster is null) return printed;
        if (monster.Level is uint changed) return changed;
        return statProfiles.TryGetValue(card.GameCardId, out var profile) && profile.ReducedLevel is uint reduced && ReducedBySummon(state, binding, monster)
            ? reduced : printed;
    }

    /// <summary>The ATK and DEF a monster starts from before modifiers, after its own stat rules.</summary>
    private (int? Attack, int? Defense) BaseStatistics(DuelState state, MonsterBinding binding)
    {
        var printed = catalog[binding.Card.GameCardId].Statistics;
        if (!statProfiles.TryGetValue(binding.Card.GameCardId, out var profile)) return (printed.Attack, printed.Defense);
        int? attack = printed.Attack ?? (profile.UnknownIsZero ? 0 : null), defense = printed.Defense ?? (profile.UnknownIsZero ? 0 : null);
        var monster = binding.IsStillPresent(state) ? state.GetPlayer(binding.Player).MonsterZones[binding.Zone] : null;
        if (monster is null) return (attack, defense);
        if (profile.ReducedAttack is int reduced && ReducedBySummon(state, binding, monster)) attack = reduced;
        if (profile.PerHandCard != 0 && monster.Position != BattlePosition.FaceDownDefense && !state.EventEffects.NegatedMonsters.Contains(binding))
        {
            int gain = profile.PerHandCard * state.GetPlayer(binding.Player).Hand.Count;
            attack += gain; defense += gain;
        }
        if ((profile.AttackPerCount != 0 || profile.DefensePerCount != 0) && monster.Position != BattlePosition.FaceDownDefense &&
            !state.EventEffects.NegatedMonsters.Contains(binding))
        {
            int count = StatCount(state, binding.Player, profile);
            attack += profile.AttackPerCount * count; defense += profile.DefensePerCount * count;
        }
        return (attack, defense);
    }

    private int StatCount(DuelState state, PlayerId controller, MonsterStatProfile profile)
    {
        var filter = profile.CountFilter ?? new AnyMonsterFilter();
        return profile.CountZone switch
        {
            StatCountZone.OwnFaceUpMonsters => state.GetPlayer(controller).MonsterZones.Count(m => m is { Position: not BattlePosition.FaceDownDefense } &&
                MatchesFilter(m.Card, filter)),
            StatCountZone.OwnGraveyard => state.GetPlayer(controller).Graveyard.Count(c => MatchesFilter(c, filter, inGraveyard: true)),
            _ => state.Players.Sum(p => p.Banished.Count(c => MatchesFilter(c, filter)))
        };
    }

    private DuelState AddFromGraveyard(DuelState state, PlayerId controller, CardInstance card, string effectId, List<DuelEvent> events) =>
        MoveZoneCardToHand(state, new(controller, FieldLocation.Graveyard, -1, card, 0), effectId, events);

    /// <summary>
    /// Whether the effect of <paramref name="link"/> applies to <paramref name="target"/>: a face-up monster Normal Summoned or Set with an
    /// "unaffected" rule ignores Spell and Trap effects (Pendulum Effects included) or activated effects of monsters with a lower original
    /// Level or Rank. Costs and targeting are not effects, so only resolution checks this.
    /// </summary>
    private bool Affects(DuelState state, ChainLink link, FieldCardTarget target)
    {
        if (statProfiles.Count == 0 || target.Location != FieldLocation.MonsterZone || !statProfiles.TryGetValue(target.Card.GameCardId, out var profile) ||
            !(profile.UnaffectedByLowerMonsters || profile.UnaffectedBySpellTraps)) return true;
        var binding = new MonsterBinding(target.Player, target.Zone, target.Card, target.PlacementId);
        if (!binding.IsStillPresent(state) || state.GetPlayer(target.Player).MonsterZones[target.Zone] is not { Position: not BattlePosition.FaceDownDefense, SummonTributes: not null } ||
            state.EventEffects.NegatedMonsters.Contains(binding)) return true;
        var source = link.Activation.Spell;
        bool spellEffect = link.IsPendulumScale || link.Trigger?.Profile.SourceLocation is TriggerSourceLocation.PendulumZone or TriggerSourceLocation.FaceUpSpellTrap ||
            link.Trigger is null && LinkDefinition(link).Origin == EffectOrigin.PendulumZone || rules[source.GameCardId].Category != CardCategory.Monster;
        if (spellEffect) return !profile.UnaffectedBySpellTraps;
        return !profile.UnaffectedByLowerMonsters || source == target.Card || catalog[source.GameCardId].Statistics.Level >= CurrentLevel(target.Card);
    }

    /// <summary>Adds a stat change that lasts while the monster stays face-up on the field.</summary>
    private DuelState ModifyLasting(DuelState state, MonsterBinding target, int attack, int defense, string effectId, List<DuelEvent> events)
    {
        var effects = state.EventEffects;
        var modifier = new StatModifier(effectId, null, target, attack, defense, checked(State.Revision + 1));
        events.Add(new MonsterModifierCreated(modifier));
        return state.WithEventEffects(effects.WithModifiers(effects.TimedModifiers.Append(modifier), effects.AppliedModifiers, effects.Statistics));
    }

    private DuelState WeakenEventMonster(DuelState state, ChainLink link, WeakenEventMonsterStep step, string effectId, List<DuelEvent> events)
    {
        if (link.Trigger?.Occurrence.Subject is not { } subject || !subject.IsStillPresent(state) ||
            state.GetPlayer(subject.Player).MonsterZones[subject.Zone]!.Position == BattlePosition.FaceDownDefense) return state;
        int before = GetMonsterStatistics(state, subject.Player, subject.Zone)?.Attack ?? 0;
        state = ModifyLasting(state, subject, -step.Attack, 0, effectId, events);
        // "Then, if its ATK has been reduced to 0 as a result": it had ATK to lose, and none is left.
        if (step.DestroyAtZero && before > 0 && (GetMonsterStatistics(state, subject.Player, subject.Zone)?.Attack ?? 0) == 0)
            state = DestroyFieldCard(state, new(subject.Player, FieldLocation.MonsterZone, subject.Zone, subject.Card, subject.PlacementId), events);
        return state;
    }

    private DuelState PayDownToGain(DuelState state, PlayerId controller, ChainLink link, PayDownToGainStep step, string effectId, List<DuelEvent> events)
    {
        var player = state.GetPlayer(controller);
        int paid = player.LifePoints - step.Remaining;
        if (paid <= 0) return state;
        events.Add(new LifePointsPaid(controller, paid, player.LifePoints, step.Remaining, effectId));
        state = state.With(player: player.With(lifePoints: step.Remaining));
        return link.Trigger?.Source is { } source && source.IsStillPresent(state) ? ModifyLasting(state, source, paid, paid, effectId, events) : state;
    }

    /// <summary>The scale a Pendulum trigger resolves for, if it is still in its zone.</summary>
    private static FieldSpellTrap? SourceScale(DuelState state, ChainLink link) =>
        link.Trigger?.Source is { } source && source.Zone is 0 or 1 && state.GetPlayer(source.Player).PendulumZones[source.Zone] is { } scale &&
        scale.Card == source.Card && scale.PlacementId == source.PlacementId ? scale : null;

    private DuelState ReduceSourceScale(DuelState state, ChainLink link, ReduceSourceScaleStep step, string effectId, List<DuelEvent> events)
    {
        if (SourceScale(state, link) is not { } scale) return state;
        int value = Math.Max(step.Minimum, (int)CurrentScale(state, scale, left: link.Trigger!.Source.Zone == 0) - step.By);
        events.Add(new ScaleChanged(scale.Card, (uint)value, effectId));
        return state.WithBookkeeping(state.Bookkeeping with
        {
            ScaleChanges = Array.AsReadOnly(state.Bookkeeping.ScaleChanges.Where(c => c.Turn == state.TurnNumber || c.Turn == 0)
                .Append(new ScaleChange(scale.Card, scale.PlacementId, (uint)value, 0)).ToArray())
        });
    }

    private DuelState DestroyAboveSourceScale(DuelState state, PlayerId controller, ChainLink link, DestroyAboveSourceScaleStep step, List<DuelEvent> events)
    {
        if (SourceScale(state, link) is not { } scale) return state;
        uint limit = CurrentScale(state, scale, left: link.Trigger!.Source.Zone == 0);
        return FieldMatches(state, controller, FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp, null)
            .Where(t => !IsXyzCard(t.Card) && CurrentLevel(t.Card) > limit && !MatchesFilter(t.Card, step.Except) && Affects(state, link, t)).ToArray()
            .Aggregate(state, (s, t) => DestroyFieldCard(s, t, events));
    }

    private DuelState SetDeclaredLevel(DuelState state, PlayerId controller, ChainLink link, SetDeclaredLevelStep step)
    {
        if (link.Declared is not int level) return state;
        var player = state.GetPlayer(controller);
        var zones = player.MonsterZones.ToArray();
        for (int zone = 0; zone < zones.Length; zone++)
            if (zones[zone] is { Position: not BattlePosition.FaceDownDefense } monster && !IsXyzCard(monster.Card) && MatchesFilter(monster.Card, step.Filter))
                zones[zone] = monster with { Level = (uint)level };
        return state.With(player: player.With(monsters: zones));
    }
}
