using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Taking, giving and switching control of monsters, and how long it lasts.</summary>
public sealed class ControlChangeTests
{
    private const uint W = 1031, Rock = 1032, ChangeOfHeart = 4860, SnatchSteal = 4898, CreatureSwap = 5162, Goblin = 5714;

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        CardCatalog catalog = new(new(null, "synthetic-control"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Rock, "Rock", "", new(1200, 1000, 4), new(5, MonsterTypes.Rock, 1, 1, 0, 0, 0))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = rules[Rock] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: TemplateCardFamily.Family.Definitions, shuffleSeed: 7, ruleLocks: TemplateCardFamily.Family.RuleLocks,
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    /// <summary>Answers decisions and choices with the first option (or the given card) and resolves every chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e, Func<IReadOnlyList<CardInstance>, CardInstance>? pick = null)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { FieldTargetOptions: [var target, ..] } d
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)
                : e.State.PendingDecision is { } card ? new ChooseDecisionCard(card.Player, card.DecisionId, card.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.DefinitionCardChoice is { } choice ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, (pick?.Invoke(choice.Options) ?? choice.Options[0]).InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Player Two Normal Summons <paramref name="opponent"/>; then it is Player One's Main Phase 1 of turn 3.</summary>
    private static DuelEngine Ready(uint[] first, uint opponent, uint[]? second = null)
    {
        var e = Engine(first, second ?? Deck(opponent));
        EnterMain(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, opponent), 0)); Next(e);
        return e;
    }

    [Fact]
    public void ControlTakenUntilTheEndPhaseGoesBackToThePlayerItWasTakenFrom()
    {
        var e = Ready(Deck(ChangeOfHeart), W);
        var taken = P(e, PlayerId.Two).MonsterZones[0]!;
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, ChangeOfHeart), 0));
        var moved = Assert.Single(Drive(e).OfType<ControlChanged>());
        Assert.Equal((PlayerId.Two, PlayerId.One, taken.Card), (moved.From, moved.To, moved.Card));
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        var held = P(e, PlayerId.One).MonsterZones.Single(m => m is not null)!;
        Assert.Equal((taken.Card, taken.PlacementId), (held.Card, held.PlacementId));
        Assert.Equal(PlayerId.Two, held.Card.Owner);
        Phase(e, DuelPhase.End);
        Assert.All(e.State.GetPlayer(PlayerId.One).MonsterZones, Assert.Null);
        Assert.Equal(taken.Card, P(e, PlayerId.Two).MonsterZones.Single(m => m is not null)!.Card);
    }

    [Fact]
    public void AMonsterWhoseControlCannotSwitchCannotBeTargetedToTakeIt()
    {
        var e = Ready(Deck(ChangeOfHeart), Goblin);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, ChangeOfHeart), 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void AnEquipKeepsControlAcrossTurnsAndStaysAttachedAfterTheMove()
    {
        var e = Ready(Deck(SnatchSteal), W);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, SnatchSteal), 0));
        Drive(e);
        var equip = P(e, PlayerId.One).SpellTrapFields.Single(f => f is not null)!;
        var held = P(e, PlayerId.One).MonsterZones.Select((m, z) => (m, z)).Single(x => x.m is not null);
        Assert.Equal(new MonsterBinding(PlayerId.One, held.z, held.m!.Card, held.m.PlacementId), equip.EquippedTo);
        Phase(e, DuelPhase.End); Ok(e, new EndTurn(PlayerId.One));
        Ok(e, new ResolveDrawStep(PlayerId.Two));
        Drive(e);
        Assert.Equal(9000, P(e, PlayerId.Two).LifePoints);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[held.z]);
        Assert.NotNull(P(e, PlayerId.One).SpellTrapFields.Single(f => f is not null));
    }

    [Fact]
    public void EachPlayerChoosesAMonsterToSwapAndNeitherCanChangePositionThisTurn()
    {
        var e = Ready(Deck(CreatureSwap, Rock), W);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Rock), 0));
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, CreatureSwap), 1));
        var events = Drive(e);
        Assert.Equal(2, events.OfType<ControlChanged>().Count());
        Assert.Equal(W, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(Rock, P(e, PlayerId.Two).MonsterZones[0]!.Card.GameCardId);
        Reject(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense), DuelError.PositionChangeNotAllowed);
    }

    [Fact]
    public void ASwapNeedsAMonsterOnBothSides()
    {
        var e = Engine(Deck(CreatureSwap, Rock), Deck());
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Rock), 0));
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, CreatureSwap), 1), DuelError.DecisionUnavailable);
    }
}
