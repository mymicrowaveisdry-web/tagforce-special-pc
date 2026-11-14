using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Face-down monsters as new cards, control while a monster stays face-up, per-target steps and declarations before targets.</summary>
public sealed class RulesAndControlTests
{
    private const uint W = 1071, Ping = 1072, Sun = 1073, Flame = 1074, Bury = 1075, BookOfMoon = 5432, MysticBox = 5168, Crevice = 8853, CloakAndDagger = 7015, PsiBlocker = 9283, MindOnAir = 6119, LegendarySword = 4307, Lyna = 8800, Hiita = 6337;
    private static readonly FieldTarget FaceDownMonster = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceDown);

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        CardCatalog catalog = new(new(null, "synthetic-rules-and-control"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Ping, "Ping", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Sun, "Sun", "", new(0, 0, 1)))
                .Append(new CardDefinition(Bury, "Bury", "", new(0, 0, 1)))
                .Append(new CardDefinition(Flame, "Flame", "", new(1200, 1000, 4), new(MonsterAttributes.Fire, MonsterTypes.Pyro, 1, 1, 0, 0, 0))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = rules[Ping] = rules[Flame] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Sun] = rules[Bury] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        EffectDefinition bury = new("test-bury", Bury, EffectKind.NormalSpell,
            [new SendFromDeckToGraveyardStep(new CardIdFilter([Flame])), new SendFromDeckToGraveyardStep(new CardIdFilter([W])), new SendFromDeckToGraveyardStep(new CardIdFilter([Ping]))]);
        EffectDefinition ping = new("test-ping", Ping, EffectKind.MonsterIgnition, [new GainLifePointsStep(100)], oncePerTurn: true);
        EffectDefinition sun = new("test-sun", Sun, EffectKind.NormalSpell, [new FlipTargetsFaceUpStep()], fieldTargets: [FaceDownMonster]);
        return new(catalog, rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers, experimentalBattleFlow: true,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, ping, sun, bury], shuffleSeed: 9, ruleLocks: TemplateCardFamily.Family.RuleLocks,
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;

    /// <summary>Answers decisions with the first option and resolves every trigger and chain.</summary>
    private static List<DuelEvent> Drive(DuelEngine e)
    {
        List<DuelEvent> events = [];
        for (int i = 0; i < 100 && e.State.Outcome is null; i++)
        {
            DuelCommand? command = e.State.EventEffects.PendingCommitment is { ZoneOptions: [var zone, ..] } c
                    ? new ChooseTriggerZoneTarget(c.Player, c.DecisionId, zone.Player, zone.Card.InstanceId)
                : e.State.EventEffects.PendingTriggers is { } t ? new ChooseTriggerOrder(t.Player, t.DecisionId, t.Options.Select(o => o.Id).ToArray())
                : e.State.PendingDecision is { FieldTargetOptions: [var target, ..] } d
                    ? new ChooseDecisionFieldTarget(d.Player, d.DecisionId, target.Player, target.Location, target.Zone, target.Card.InstanceId)
                : e.State.PendingDecision is { } card ? new ChooseDecisionCard(card.Player, card.DecisionId, card.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.Declaration is { } declare ? new DeclareValue(declare.Player, declare.DecisionId, declare.Options[0])
                : chain.DefinitionCardChoice is { } choice ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, choice.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Player One Sets <paramref name="charmer"/>, Player Two Normal Summons <paramref name="theirs"/>, and on turn 3 Player One Flip Summons.</summary>
    private static DuelEngine FlipCharmer(uint charmer, uint theirs, params uint[] more)
    {
        var e = Engine(Deck([charmer, .. more]), Deck(theirs));
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, PlayerId.One, charmer), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, theirs), 0)); Next(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        Drive(e);
        return e;
    }

    [Fact]
    public void ACharmerKeepsAChosenMonsterOnlyWhileItStaysFaceUp()
    {
        var e = FlipCharmer(Lyna, W, BookOfMoon);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == W);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, BookOfMoon), 0));
        Drive(e);
        Assert.Equal(W, P(e, PlayerId.Two).MonsterZones.Single(m => m is not null)!.Card.GameCardId);
        Assert.Equal(BattlePosition.FaceDownDefense, P(e, PlayerId.One).MonsterZones[0]!.Position);
    }

    [Fact]
    public void ACharmerTakesOnlyAMonsterOfItsAttribute()
    {
        var none = FlipCharmer(Hiita, W);
        Assert.NotNull(P(none, PlayerId.Two).MonsterZones[0]);
        var e = FlipCharmer(Hiita, Flame);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Flame);
        Next(e); Next(e);
        Assert.Contains(P(e, PlayerId.One).MonsterZones, m => m?.Card.GameCardId == Flame);
    }

    [Fact]
    public void AMonsterWhoseControlEndsWithNoFreeZoneTakesItsEquipWithIt()
    {
        var e = Engine(Deck(Lyna, BookOfMoon), Deck(W, LegendarySword, W, W, W, W, W, W));
        EnterMain(e); Ok(e, new SetMonster(PlayerId.One, Hand(e, PlayerId.One, Lyna), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, LegendarySword), 0)); Drive(e);
        for (int zone = 1; zone < 5; zone++) { Next(e); Next(e); Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), zone)); }
        Next(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack)); Drive(e);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, BookOfMoon), 0)); Drive(e);
        Assert.Equal(1, P(e, PlayerId.Two).Graveyard.Count(c => c.GameCardId == W));
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == LegendarySword);
        Assert.All(P(e, PlayerId.Two).SpellTrapFields, Assert.Null);
    }

    [Fact]
    public void AStepOnOneTargetDestroysTheFirstAndThenGivesTheSecondAway()
    {
        var e = Engine(Deck(MysticBox, Ping), Deck(Flame));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Ping), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Flame), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, MysticBox), 0));
        Drive(e);
        Assert.Contains(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == Flame);
        Assert.All(P(e, PlayerId.One).MonsterZones, Assert.Null);
        Assert.Equal(Ping, P(e, PlayerId.Two).MonsterZones.Single(m => m is not null)!.Card.GameCardId);
    }

    [Fact]
    public void TargetsChosenForADeclaredAttributeMustShareItAndFixTheDeclaration()
    {
        var e = Engine(Deck(Bury, Crevice, W, W, W, Flame, W, Ping), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Bury), 0)); Drive(e);
        Assert.Equal(3, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId is Flame or W or Ping));
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Crevice), 1)); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 1));
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, t => t.Card.GameCardId == Flame);
        var events = Drive(e);
        Assert.Contains(events, ev => ev is DeclarationMade { Kind: DeclarationKind.Attribute, Value: (int)MonsterAttributes.Light });
        Assert.Equal(2, P(e, PlayerId.One).Banished.Count(c => c.GameCardId is W or Ping));
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Flame);
    }

    [Fact]
    public void ACardDeclaresANameAsItResolvesAndBanishesTheOpponentsMonsterOfThatName()
    {
        var e = Engine(Deck(CloakAndDagger), Deck(Flame, W));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, CloakAndDagger), 0)); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Assert.Null(e.State.PendingChain!.Declaration);
        Ok(e, new PassChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId));
        Ok(e, new PassChainResponse(PlayerId.One, e.State.PendingChain!.DecisionId));
        Ok(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId));
        var ask = e.State.PendingChain!.Declaration!;
        Assert.Equal(DeclarationKind.MonsterCardName, ask.Kind);
        Ok(e, new DeclareValue(PlayerId.One, ask.DecisionId, (int)W));
        Drive(e);
        Assert.Equal((int)W, P(e, PlayerId.One).SpellTrapFields[0]!.Declared);
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Flame), 0));
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Assert.Null(e.State.PendingChain);
        Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 1));
        Drive(e);
        Assert.Contains(P(e, PlayerId.Two).Banished, c => c.GameCardId == W);
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == CloakAndDagger);
        Assert.Equal(Flame, P(e, PlayerId.Two).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void ADeclaredNameStopsTheEffectsOfThatFaceUpCardUntilTheLockEnds()
    {
        var e = Engine(Deck(PsiBlocker), Deck(MindOnAir));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, PsiBlocker), 0)); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, MindOnAir), 0)); Next(e);
        Assert.True(e.HandRevealed(PlayerId.One));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Ok(e, new DeclareValue(PlayerId.One, e.State.PendingChain!.Declaration!.DecisionId, (int)MindOnAir));
        Drive(e);
        Assert.False(e.HandRevealed(PlayerId.One));
        Next(e);
        Assert.False(e.HandRevealed(PlayerId.One));
        Next(e);
        Assert.True(e.HandRevealed(PlayerId.One));
    }

    [Fact]
    public void AMonsterFlippedFaceDownIsANewCardForItsOncePerTurnEffect()
    {
        var e = Engine(Deck(Ping, BookOfMoon, Sun), Deck());
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Ping), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Drive(e);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, BookOfMoon), 0)); Drive(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Sun), 1)); Drive(e);
        Assert.Equal(BattlePosition.FaceUpAttack, P(e, PlayerId.One).MonsterZones[0]!.Position);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0)); Drive(e);
        Assert.Equal(8200, P(e, PlayerId.One).LifePoints);
    }
}
