using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Skipped phases, ending the Battle Phase, and the Egyptian God Summon rules.</summary>
public sealed class PhaseControlTests
{
    private const uint W = 1051, Gift = 1052, RecklessGreed = 5448, Lawbook = 4990, FrozenSoul = 5793, NegateAttack = 4804, Obelisk = 4998;
    private const string ObeliskProcedure = "egyptian-god-4998-three-tributes-v1";

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        CardCatalog catalog = new(new(null, "synthetic-phase-control"),
            TemplateCardFamily.Family.Cards.Concat(EgyptianGodFamily.Family.Cards).Select(c => new CardDefinition(c.CardId, c.Name, "", c.CardId == Obelisk ? new(4000, 4000, 10) : new(1000, 1000, 4),
                    new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Gift, "Gift", "", new(0, 0, 1))));
        var rules = TemplateCardFamily.Family.Rules.Concat(EgyptianGodFamily.Family.Rules).ToDictionary();
        rules[W] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Gift] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        EffectDefinition gift = new("test-gift", Gift, EffectKind.NormalSpell, [new GainLifePointsStep(2000, Players: EffectPlayers.Opponent)]);
        return new(catalog, rules, first, second, experimentalTriggerEffects: [.. TemplateCardFamily.Family.Triggers, .. EgyptianGodFamily.Family.Triggers], experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, .. EgyptianGodFamily.Family.Definitions, gift], shuffleSeed: 11,
            ruleLocks: [.. TemplateCardFamily.Family.RuleLocks, .. EgyptianGodFamily.Family.RuleLocks],
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules,
            experimentalTributeProcedures: EgyptianGodFamily.Family.TributeProcedures);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    private static List<DuelEvent> Drive(DuelEngine e)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { FieldTargetOptions: [var target, ..] } d
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    [Fact]
    public void SkippedDrawPhasesDrawNothingAndThenDrawingResumes()
    {
        var e = Engine(Deck(RecklessGreed), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, RecklessGreed), 0)); Next(e); Next(e);
        int deck = P(e, PlayerId.One).Deck.Count;
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Assert.Equal(deck - 2, P(e, PlayerId.One).Deck.Count);
        Next(e); Next(e);
        Assert.Equal(deck - 2, P(e, PlayerId.One).Deck.Count);
        Next(e); Next(e);
        Assert.Equal(deck - 2, P(e, PlayerId.One).Deck.Count);
        Next(e); Next(e);
        Assert.Equal(deck - 3, P(e, PlayerId.One).Deck.Count);
    }

    [Fact]
    public void ASkippedStandbyPhaseGoesStraightToMainPhaseOne()
    {
        var e = Engine(Deck(Lawbook), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Lawbook), 0)); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Next(e);
        Phase(e, DuelPhase.End);
        while (P(e, PlayerId.Two).Hand.Count > 6) Ok(e, new DiscardForHandLimit(PlayerId.Two, P(e, PlayerId.Two).Hand[^1].InstanceId));
        Ok(e, new EndTurn(PlayerId.Two));
        var events = Ok(e, new ResolveDrawStep(PlayerId.One)).Events;
        Assert.Contains(events, ev => ev is PhaseSkipped { Phase: DuelPhase.Standby });
        Assert.Equal(DuelPhase.Main1, e.State.Phase);
    }

    [Fact]
    public void ASkippedBattlePhaseCannotBeEnteredThatTurnOnly()
    {
        var e = Engine(Deck(FrozenSoul, Gift), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, FrozenSoul), 0)); Next(e); Next(e);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.ActivationConditionNotMet);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Gift), 1)); Drive(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Next(e);
        Reject(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle), DuelError.PhaseSkipped);
        Next(e); Next(e);
        Phase(e, DuelPhase.Battle);
        Assert.Equal(DuelPhase.Battle, e.State.Phase);
    }

    [Fact]
    public void NegatingAnAttackAndEndingTheBattlePhaseStopsFurtherAttacks()
    {
        var e = Engine(Deck(NegateAttack), Deck());
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, NegateAttack), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        S.Respond(e, 0);
        Drive(e);
        Assert.Equal(BattleStep.End, e.State.Battle!.Step);
        Assert.Null(e.State.Battle.Attack);
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
        S.Close(e);
        Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Main2));
    }

    [Fact]
    public void AnEgyptianGodNeedsThreeTributesCannotBeSetAndItsSummonCannotBeAnswered()
    {
        var e = Engine(Deck(Obelisk, W, W, W), Deck(W));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 1)); Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 2));
        Next(e); Next(e);
        MonsterBinding[] tributes = [.. Enumerable.Range(0, 3).Select(z => new MonsterBinding(PlayerId.One, z, P(e, PlayerId.One).MonsterZones[z]!.Card, P(e, PlayerId.One).MonsterZones[z]!.PlacementId))];
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Obelisk), 0, tributes), DuelError.UnsupportedTributeProcedure);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Obelisk), 0, tributes, faceDown: true, procedureId: ObeliskProcedure),
            DuelError.UnsupportedTributeProcedure);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Obelisk), 0, tributes[..2], procedureId: ObeliskProcedure),
            DuelError.InvalidTributeSelection);
        var events = Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Obelisk), 0, tributes, procedureId: ObeliskProcedure)).Events;
        Assert.Contains(events, ev => ev is MonsterNormalSummoned);
        Assert.DoesNotContain(events, ev => ev is SummonAttempted or SummonResponseWindowRequested);
        Assert.Null(e.State.PendingSummon);
        Assert.Null(e.State.OpenWindow);
        Assert.Equal(Obelisk, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void SkipsOfTheSameDrawPhaseDoNotAddUp()
    {
        var e = Engine(Deck(RecklessGreed, RecklessGreed), Deck());
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, RecklessGreed), 0));
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, RecklessGreed), 1));
        Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 1)); Drive(e);
        int deck = P(e, PlayerId.One).Deck.Count;
        Next(e); Next(e); Next(e); Next(e);
        Assert.Equal(deck, P(e, PlayerId.One).Deck.Count);
        Next(e); Next(e);
        Assert.Equal(deck - 1, P(e, PlayerId.One).Deck.Count);
    }
}
