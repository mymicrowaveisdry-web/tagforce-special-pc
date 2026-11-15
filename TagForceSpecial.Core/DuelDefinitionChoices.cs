namespace TagForceSpecial.Core;

public sealed record ActivateSetSpell(PlayerId Player, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0) : DuelCommand(Player);
/// <summary>The turn player activates a Quick-Play Spell from the hand as the next chain link.</summary>
public sealed record ActivateHandChainResponse(PlayerId Player, long DecisionId, int CardInstanceId, int SpellTrapZone, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int EffectIndex = 0)
    : DuelCommand(Player);
public sealed record EffectDamageDealt(PlayerId Player, int Damage, int LifePointsBefore,
    int LifePointsAfter, string EffectId) : DuelEvent;
/// <summary>The player's Deck was shuffled; carries no card order.</summary>
public sealed record DeckShuffled(PlayerId Player, string EffectId) : DuelEvent;
/// <summary>A Token left the field and ceased to exist; follows the departure's cause event.</summary>
public sealed record TokenRemoved(PlayerId Owner, CardInstance Card) : DuelEvent;

/// <summary>A turn-scoped "cannot Summon other monsters" restriction from an activated definition.</summary>
public sealed record SummonLock(PlayerId Player, int TurnNumber, CardInstance Source, string EffectId);
public sealed record SummonLockApplied(SummonLock Lock) : DuelEvent;

/// <summary>A definition step paused at resolution for its controller's choice.</summary>
public abstract class PendingDefinitionChoice
{
    public long DecisionId { get; }
    public PlayerId Player { get; }
    public string EffectId { get; }
    public int LinkNumber { get; }
    public int StepIndex { get; }

    private protected PendingDefinitionChoice(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex)
    {
        DecisionId = decisionId; Player = player; EffectId = effectId; LinkNumber = linkNumber; StepIndex = stepIndex;
    }
}

/// <summary>Resolution-time Special Summon choice; deck options are visible only to the choosing player.</summary>
public sealed record DefinitionSummonOption(CardInstance Card, DecisionCardSource Source,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    PlayerId? From = null);
public sealed class PendingDefinitionSummon : PendingDefinitionChoice
{
    public IReadOnlyList<DefinitionSummonOption> Options { get; }
    public IReadOnlyList<int> MonsterZoneOptions { get; }
    public IReadOnlyList<BattlePosition> PositionOptions { get; }
    /// <summary>Monsters already chosen for an any-number Summon; all are placed together.</summary>
    public IReadOnlyList<ChosenDefinitionSummon> Picks { get; }
    public bool CanFinish { get; }

    internal PendingDefinitionSummon(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex,
        IEnumerable<DefinitionSummonOption> options, IEnumerable<int> zones, BattlePosition? onlyPosition = null,
        IEnumerable<ChosenDefinitionSummon>? picks = null, bool canFinish = false)
        : base(decisionId, player, effectId, linkNumber, stepIndex)
    {
        Picks = Array.AsReadOnly(picks?.ToArray() ?? []);
        CanFinish = canFinish;
        Options = Array.AsReadOnly(options.ToArray());
        MonsterZoneOptions = Array.AsReadOnly(zones.ToArray());
        PositionOptions = Array.AsReadOnly(onlyPosition is { } only ? [only] : new[] { BattlePosition.FaceUpAttack, BattlePosition.FaceUpDefense });
    }
}
public sealed record ChooseDefinitionSummon(PlayerId Player, long DecisionId, int CardInstanceId, int MonsterZone,
    BattlePosition Position) : DuelCommand(Player);
public sealed record ChosenDefinitionSummon(CardInstance Card, DecisionCardSource Source, int MonsterZone, BattlePosition Position);
/// <summary>Ends an any-number Summon with the monsters chosen so far.</summary>
public sealed record FinishDefinitionSummon(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record DefinitionSummonRequested(PendingDefinitionSummon Decision) : DuelEvent;

/// <summary>A resolution-time card choice; options are visible only to the choosing player.</summary>
public sealed class PendingDefinitionCardChoice : PendingDefinitionChoice
{
    public IReadOnlyList<CardInstance> Options { get; }
    /// <summary>Cards selected for one simultaneous discard; they stay in hand until every choice is complete.</summary>
    public IReadOnlyList<CardInstance> Picks { get; }
    public bool CanFinish { get; }

    internal PendingDefinitionCardChoice(long decisionId, PlayerId player, string effectId, int linkNumber, int stepIndex,
        IEnumerable<CardInstance> options, IEnumerable<CardInstance>? picks = null, bool canFinish = false) : base(decisionId, player, effectId, linkNumber, stepIndex)
    {
        Options = Array.AsReadOnly(options.ToArray());
        Picks = Array.AsReadOnly(picks?.ToArray() ?? []);
        CanFinish = canFinish;
    }
}
public sealed record ChooseDefinitionCard(PlayerId Player, long DecisionId, int CardInstanceId) : DuelCommand(Player);
public sealed record FinishDefinitionCardChoice(PlayerId Player, long DecisionId) : DuelCommand(Player);
public sealed record DefinitionCardChoiceRequested(PendingDefinitionCardChoice Decision) : DuelEvent;
/// <summary>A Deck card was revealed and added to its owner's hand by an effect.</summary>
public sealed record CardSearched(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
/// <summary>A Deck card went to its owner's Graveyard by an effect (a chosen card or the top of the Deck).</summary>
public sealed record CardSentFromDeckToGraveyard(PlayerId Player, CardInstance Card, string EffectId) : DuelEvent;
