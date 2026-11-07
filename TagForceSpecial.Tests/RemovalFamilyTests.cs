using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Removal family wiring through the production definitions.</summary>
public sealed class RemovalFamilyTests
{
    private const uint A = 901, B = 902, C = 903, DarkHole = 4342, Storm = 4891, Raigeki = 5544, ShieldCrush = 6003,
        Twister = 6999, SoulTaker = 5743, Fissure = 4835, Smashing = 5905, Hammer = 6130, Evacuation = 5914,
        Trunade = 4910, Block = 4874, NoEntry = 6746, TwoPronged = 4839;

    private static CardCatalog Catalog() => new(new(null, "synthetic-removal"),
        RemovalFamily.Cards.Select(c => c.CardId).Concat([A, B, C, ChainCardFamily.Scapegoat]).Concat(ChainCardFamily.SheepTokenIds)
            .Select(id => new CardDefinition(id, $"Probe {id}", "", id switch
            {
                A => new(1000, 1000, 4), B => new(1500, 2000, 4), C => new(1500, 500, 4), _ => new(0, 0, 1)
            })));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = RemovalFamily.Rules.ToDictionary();
        foreach (uint monster in new[] { A, B, C })
            rules[monster] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        foreach (uint id in ChainCardFamily.SheepTokenIds.Append(ChainCardFamily.Scapegoat)) rules[id] = ChainCardFamily.Rules[id];
        return new(Catalog(), rules, first, second, effectDefinitions: [.. RemovalFamily.Definitions,
            .. ChainCardFamily.Definitions.Where(d => d.CardId == ChainCardFamily.Scapegoat)]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(A, 40 - top.Length)];
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static void Summon(DuelEngine e, uint id, int zone, bool set = false) =>
        Ok(e, set ? new SetMonster(Turn(e), Hand(e, Turn(e), id).InstanceId, zone) : new NormalSummon(Turn(e), Hand(e, Turn(e), id).InstanceId, zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, Turn(e), id).InstanceId, zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, Turn(e), id).InstanceId, zone));
    private static long ChainId(DuelEngine e) => e.State.PendingChain!.DecisionId;

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.Single(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    private static void Discard(DuelEngine e)
    {
        var p = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId));
    }

    private static FieldMonster? Monster(DuelEngine e, PlayerId p, int zone) => e.State.GetPlayer(p).MonsterZones[zone];

    [Fact]
    public void DarkHoleDestroysEveryMonsterActivatingSideFirstAndNeedsOne()
    {
        var empty = Engine(Deck(DarkHole), Deck());
        EnterMain(empty);
        Reject(empty, new ActivateSpell(PlayerId.One, Hand(empty, PlayerId.One, DarkHole).InstanceId, 4), DuelError.DecisionUnavailable);

        var e = Engine(Deck(DarkHole), Deck(B));
        EnterMain(e); Summon(e, A, 0); Next(e); Summon(e, B, 0); Next(e);
        Activate(e, DarkHole);
        var destroyed = Resolve(e).OfType<MonsterDestroyed>().ToArray();
        Assert.Equal([PlayerId.One, PlayerId.Two], destroyed.Select(d => d.Player));
        Assert.All(e.State.Players, p => Assert.All(p.MonsterZones, Assert.Null));
    }

    [Fact]
    public void HeavyStormDestroysEverySetAndFaceUpSpellTrapButItself()
    {
        var e = Engine(Deck(Storm, ShieldCrush), Deck(Trunade));
        EnterMain(e); SetCard(e, ShieldCrush, 1); Next(e); SetCard(e, Trunade, 0); Next(e);
        Activate(e, Storm);
        var events = Resolve(e);
        Assert.Equal(2, events.OfType<SpellTrapDestroyed>().Count());
        Assert.Contains(events, ev => ev is SpellSentToGraveyard s && s.Card.GameCardId == Storm); // cleanup, not destruction
        Assert.All(e.State.Players, p => Assert.All(p.SpellTrapFields, Assert.Null));
    }

    [Fact]
    public void ShieldCrushOffersOnlyDefensePositionMonstersIncludingFaceDown()
    {
        var e = Engine(Deck(ShieldCrush), Deck(B, C));
        EnterMain(e); Next(e); Summon(e, B, 0, set: true); Next(e); Next(e); Summon(e, C, 1); Next(e);
        Activate(e, ShieldCrush);
        var option = Assert.Single(e.State.PendingDecision!.FieldTargetOptions!);
        Assert.Equal((PlayerId.Two, 0), (option.Player, option.Zone));
        var actions = new DuelActionSource(e, Catalog()).Observe(PlayerId.One).Actions;
        Assert.All(actions, a => { Assert.Equal(ActionKind.ChooseTarget, a.Kind); Assert.Null(a.CardInstanceId); }); // no face-down identity
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Null(Monster(e, PlayerId.Two, 0));
        Assert.NotNull(Monster(e, PlayerId.Two, 1));
    }

    [Fact]
    public void TwisterPaysLifePointsAtActivationAndAnswersFromTheHandOnlyFaceUpCards()
    {
        var e = Engine(Deck(Storm, Twister), Deck(Trunade));
        EnterMain(e); Next(e); SetCard(e, Trunade, 0); Next(e);
        Activate(e, Storm, 0);
        Ok(e, new PassChainResponse(PlayerId.Two, ChainId(e)));
        var paid = Ok(e, new ActivateHandChainResponse(PlayerId.One, ChainId(e), Hand(e, PlayerId.One, Twister).InstanceId, 1));
        Assert.Equal(new LifePointsPaid(PlayerId.One, 500, 8000, 7500, "removal-6999-v1"), Assert.Single(paid.Events.OfType<LifePointsPaid>()));
        var option = Assert.Single(e.State.PendingDecision!.FieldTargetOptions!); // Heavy Storm, not the set Trunade
        Assert.Equal(Storm, option.Card.GameCardId);
        Target(e, PlayerId.One, FieldLocation.SpellTrapZone, 0);
        var events = Resolve(e);
        Assert.Equal(Storm, events.OfType<SpellTrapDestroyed>().First().Card.GameCardId);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Null(e.State.GetPlayer(PlayerId.Two).SpellTrapFields[0]); // Heavy Storm still resolved
    }

    [Fact]
    public void SoulTakerWithItsTargetGoneDoesNotGiveTheLifePoints()
    {
        var e = Engine(Deck(SoulTaker), Deck(Raigeki, B));
        EnterMain(e); Next(e); SetCard(e, Raigeki, 0); Summon(e, B, 0); Next(e);
        Activate(e, SoulTaker);
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Ok(e, new ActivateChainResponse(PlayerId.Two, ChainId(e), 0));
        Discard(e);
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0); // Raigeki Break destroys the same monster first
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Number: 1, Reason: ChainSkipReason.TargetUnavailable });
        Assert.Empty(events.OfType<LifePointsGained>());
        Assert.Equal(8000, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void FissureAsksOnATieWhileSmashingGroundAndHammerShotPickTheExtreme()
    {
        var e = Engine(Deck(Fissure, Smashing, Hammer), Deck(B, C));
        EnterMain(e); Summon(e, A, 0); Next(e); Summon(e, B, 0); Next(e); Next(e); Summon(e, C, 1); Next(e);
        Activate(e, Hammer, 4); // B and C share the highest ATK (1500) among face-up Attack Position monsters
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, ChainId(e)));
        var paused = Ok(e, new ResolveNextChainLink(PlayerId.One, ChainId(e)));
        var choice = Assert.IsType<DefinitionCardChoiceRequested>(paused.Events[^1]).Decision;
        Assert.Equal([B, C], choice.Options.Select(c => c.GameCardId).Order());
        Ok(e, new ChooseDefinitionCard(PlayerId.One, choice.DecisionId, choice.Options.Single(c => c.GameCardId == C).InstanceId));
        Assert.Null(Monster(e, PlayerId.Two, 1));
        Activate(e, Smashing, 4); // only B remains on that side
        var smashed = Resolve(e);
        Assert.Equal(B, Assert.Single(smashed.OfType<MonsterDestroyed>()).Card.GameCardId);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Fissure).InstanceId, 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void CompulsoryEvacuationDeviceReturnsMonstersToTheHandAndTokensCeaseToExist()
    {
        var e = Engine(Deck(ChainCardFamily.Scapegoat), Deck(Evacuation, Evacuation));
        EnterMain(e); Next(e);
        SetCard(e, Evacuation, 0); SetCard(e, Evacuation, 1);
        Next(e);
        Activate(e, ChainCardFamily.Scapegoat);
        Resolve(e);
        Summon(e, A, 4, set: true); // Sets stay legal under Scapegoat's lock
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.OpenWindow!.DecisionId, 0));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        var token = Resolve(e);
        Assert.Single(token.OfType<TokenRemoved>());
        Assert.Contains(token, ev => ev is CardReturnedToHand { FieldDestination: FieldCardDestination.Removed });
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.OpenWindow!.DecisionId, 1));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 4);
        var returned = Resolve(e);
        var card = Assert.Single(returned.OfType<CardReturnedToHand>());
        Assert.Equal(FieldCardDestination.Hand, card.FieldDestination);
        Assert.Contains(card.Card, e.State.GetPlayer(PlayerId.One).Hand);
    }

    [Fact]
    public void GiantTrunadeReturnsEverySpellTrapToItsOwnerButItself()
    {
        var e = Engine(Deck(Trunade, ShieldCrush), Deck(Storm));
        EnterMain(e); SetCard(e, ShieldCrush, 1); Next(e); SetCard(e, Storm, 0); Next(e);
        Activate(e, Trunade);
        Assert.Equal(2, Resolve(e).OfType<CardReturnedToHand>().Count());
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Hand, c => c.GameCardId == ShieldCrush);
        Assert.Contains(e.State.GetPlayer(PlayerId.Two).Hand, c => c.GameCardId == Storm);
        Assert.Contains(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == Trunade);
    }

    [Fact]
    public void BlockAttackAndNoEntryChangeAttackPositionMonstersToDefense()
    {
        var e = Engine(Deck(Block, NoEntry), Deck(B, C));
        EnterMain(e); Summon(e, A, 0); SetCard(e, NoEntry, 0); Next(e); Summon(e, B, 0); Next(e);
        Activate(e, Block);
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Equal(BattlePosition.FaceUpDefense, Monster(e, PlayerId.Two, 0)!.Position);
        Next(e); Summon(e, C, 1); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Assert.Equal(2, Resolve(e).OfType<MonsterPositionChanged>().Count()); // A and C; B is already in Defense
        Assert.All(e.State.Players.SelectMany(p => p.MonsterZones).OfType<FieldMonster>(),
            m => Assert.Equal(BattlePosition.FaceUpDefense, m.Position));
    }

    [Fact]
    public void TwoProngedAttackNeedsAllThreeTargetsAndDestroysThoseStillPresent()
    {
        var e = Engine(Deck(TwoPronged, C), Deck(B));
        EnterMain(e); Summon(e, A, 0); SetCard(e, TwoPronged, 0); Next(e); Summon(e, B, 0); Next(e);
        Reject(e, new ActivateSetSpell(PlayerId.One, 0), DuelError.DecisionUnavailable); // only one own monster
        Summon(e, C, 1);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Assert.All(e.State.PendingDecision!.FieldTargetOptions!, o => Assert.Equal(PlayerId.One, o.Player));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, o => o.Zone == 0 && o.Player == PlayerId.One);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 1);
        Assert.All(e.State.PendingDecision!.FieldTargetOptions!, o => Assert.Equal(PlayerId.Two, o.Player));
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Assert.Equal(3, e.State.CommittedActivation!.FieldTargets!.Count);
        Assert.Equal(3, Resolve(e).OfType<MonsterDestroyed>().Count());
    }

    [Fact]
    public void FieldTargetsAndStepsValidate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldTarget(FieldCardKind.Monster, count: 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PayLifePointsCost(0));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DestroyTargetsStep()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new ChangeTargetsToDefenseStep()],
            fieldTargets: [new(FieldCardKind.SpellTrap)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DestroyTargetsStep()],
            fieldTargets: [new(FieldCardKind.Monster, count: 3), new(FieldCardKind.Monster, count: 3)]));
        Assert.Equal(15, RemovalFamily.Cards.Count);
    }
}
