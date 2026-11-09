using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Field, Equip, revival, negation, Toon, Union, activated and counter cards through the production definitions.</summary>
public sealed class LastingCardFamilyTests
{
    private const uint W = 901, Fairy = 902, Fiendish = 903, Storm = 904, Negate = 905, EffectNegate = 906, Hole = 907, Banisher = 908, ResponseHole = 909,
        Burn = 910, BanishCost = 911,
        Sogen = 4339, Yami = 4341, United = 5212, Mage = 5213, Call = 4989, Premature = 4966, Drain = 5740, Imperial = 4960, ToonWorld = 3834,
        Mermaid = 4734, ToonSkull = 4735, YDragon = 5552, ZTank = 5555, XCannon = 5504, Cannon = 4518, Exiled = 5323, Breaker = 5655;
    private const int FieldZone = DuelPlayerState.FieldZoneIndex;

    private static readonly EffectDefinition[] Extras =
    [
        new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.SpellTrap)]),
        ResponseProbe.Negate("negate", Negate, ResponseNegation.Activation, ChainSpellSpeed.Three),
        ResponseProbe.Negate("effect-negate", EffectNegate, ResponseNegation.Effect, ChainSpellSpeed.Three),
        new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
        new("banisher", Banisher, EffectKind.NormalSpell, [new BanishAllStep()]),
        new("response-hole", ResponseHole, EffectKind.NormalTrap, [new DestroyAllStep(FieldCardKind.Monster)], [new RespondsToActivationCondition()]),
        new("burn", Burn, EffectKind.NormalSpell, [new DealDamageStep(100)]),
        new("banish-cost", BanishCost, EffectKind.NormalSpell, [new DealDamageStep(100)], cost: new BanishFromGraveyardCost(new AnyMonsterFilter()))
    ];

    private static CardNumericCodes Type(uint code) => new(1, code, 1, 1, 0, 0, 0);

    private static CardCatalog Catalog(params uint[] more) => new(new(null, "synthetic-lasting-family"),
        LastingCardFamily.Cards.Select(c => c.CardId).Concat(Extras.Select(d => d.CardId)).Concat([W, Fairy, Fiendish, XCannon]).Concat(more).Distinct().Select(id => id switch
        {
            W => new CardDefinition(id, "Warrior", "", new(1000, 1000, 4), Type(15)),
            Fairy => new CardDefinition(id, "Fairy", "", new(1500, 1000, 4), Type(17)),
            Fiendish => new CardDefinition(id, "Fiend", "", new(1000, 1000, 4), Type(3)),
            Mermaid => new CardDefinition(id, "Mermaid", "", new(1400, 1500, 4), Type(14)),
            ToonSkull => new CardDefinition(id, "Toon Skull", "", new(2500, 1200, 6), Type(3)),
            _ => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, 4), Type(7))
        }));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second) => EngineWith(first, second, []);

    /// <summary>The family plus extra synthetic definitions and triggers; Spells are told apart by kind.</summary>
    private static DuelEngine EngineWith(IEnumerable<uint> first, IEnumerable<uint> second, EffectDefinition[] more, TriggerEffectProfile[]? triggers = null)
    {
        var rules = LastingCardFamily.CardRules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        foreach (uint id in new[] { W, Fairy, Fiendish, XCannon }) rules[id] = monster;
        foreach (var definition in Extras.Concat(more))
            rules[definition.CardId] = definition.IsMonsterEffect ? monster
                : new(definition.Kind is EffectKind.NormalSpell or EffectKind.ContinuousSpell ? CardCategory.Spell : CardCategory.Trap, AllowsSpellTrapSet: true);
        foreach (var trigger in triggers ?? []) rules[trigger.CardId] = monster;
        return new(Catalog([.. more.Select(d => d.CardId), .. (triggers ?? []).Select(t => t.CardId)]), rules, first, second,
            experimentalTriggerEffects: [.. LastingCardFamily.Triggers, .. triggers ?? []],
            experimentalSpecialSummonCards: LastingCardFamily.SpecialSummonCards, effectDefinitions: [.. LastingCardFamily.Definitions, .. Extras, .. more]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static CardInstance Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id);
    private static void Summon(DuelEngine e, uint id, int zone, bool set = false) =>
        Ok(e, set ? new SetMonster(Turn(e), Hand(e, id).InstanceId, zone) : new NormalSummon(Turn(e), Hand(e, id).InstanceId, zone));
    private static void SetCard(DuelEngine e, uint id, int zone) => Ok(e, new SetSpellTrap(Turn(e), Hand(e, id).InstanceId, zone));
    private static DuelActionResult Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id).InstanceId, zone));
    private static void Respond(DuelEngine e, PlayerId player, int zone) => Ok(e, new ActivateChainResponse(player, e.State.PendingChain!.DecisionId, zone));
    private static int? Atk(DuelEngine e, PlayerId player, int zone) => e.GetMonsterStatistics(player, zone)!.Attack;
    private static int? Def(DuelEngine e, PlayerId player, int zone) => e.GetMonsterStatistics(player, zone)!.Defense;
    private static DuelPlayerState P(DuelEngine e, PlayerId player) => e.State.GetPlayer(player);
    private static bool InGraveyard(DuelEngine e, PlayerId player, uint id) => P(e, player).Graveyard.Any(c => c.GameCardId == id);

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    /// <summary>Passes the chain up to the revival choice and places the monster in that zone, face-up in Attack Position.</summary>
    private static void ResolveRevival(DuelEngine e, int zone)
    {
        while (e.State.PendingChain is { DefinitionSummon: null } chain)
            Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId));
        var pending = e.State.PendingChain!.DefinitionSummon!;
        Ok(e, new ChooseDefinitionSummon(pending.Player, pending.DecisionId, pending.Options[0].Card.InstanceId, zone, BattlePosition.FaceUpAttack));
        Resolve(e);
    }

    /// <summary>Ends the current turn and stops in the next player's Standby Phase.</summary>
    private static void ToStandby(DuelEngine e)
    {
        Phase(e, DuelPhase.End);
        var actor = Turn(e);
        while (P(e, actor).Hand.Count > 6) Ok(e, new DiscardForHandLimit(actor, P(e, actor).Hand[^1].InstanceId));
        Ok(e, new EndTurn(actor));
        Ok(e, new ResolveDrawStep(Turn(e)));
    }

    // Field Spells

    [Fact]
    public void YamiChangesStatsByTypeOnBothSides()
    {
        var e = Engine(Deck(Yami, Fiendish), Deck(Fairy));
        EnterMain(e); Summon(e, Fiendish, 0);
        Activate(e, Yami, FieldZone); Resolve(e);
        Next(e); Summon(e, Fairy, 0);
        Assert.Equal(1200, Atk(e, PlayerId.One, 0));
        Assert.Equal(1200, Def(e, PlayerId.One, 0));
        Assert.Equal(1300, Atk(e, PlayerId.Two, 0));
        Assert.Equal(Yami, P(e, PlayerId.One).FieldZone!.GameCardId);
    }

    [Fact]
    public void FieldSpellsUseOnlyTheFieldZone()
    {
        var e = Engine(Deck(Yami, Mage, W), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Yami).InstanceId, 0), DuelError.InvalidSpellTrapZone);
        Reject(e, new SetSpellTrap(PlayerId.One, Hand(e, Yami).InstanceId, 0), DuelError.InvalidSpellTrapZone);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Mage).InstanceId, FieldZone), DuelError.InvalidSpellTrapZone);
        Reject(e, new SetSpellTrap(PlayerId.One, Hand(e, Mage).InstanceId, FieldZone), DuelError.InvalidSpellTrapZone);
    }

    [Fact]
    public void NewFieldSpellSendsTheOldOneToTheGraveyardWithoutDestroyingIt()
    {
        var e = Engine(Deck(Yami, Sogen), Deck());
        EnterMain(e);
        Activate(e, Yami, FieldZone); Resolve(e);
        var events = Activate(e, Sogen, FieldZone).Events;
        Assert.Contains(events, ev => ev is FieldSpellReplaced r && r.Card.GameCardId == Yami);
        Assert.DoesNotContain(events, ev => ev is SpellTrapDestroyed);
        Assert.True(InGraveyard(e, PlayerId.One, Yami));
        Resolve(e);
        Assert.Equal(Sogen, P(e, PlayerId.One).FieldZone!.GameCardId);
    }

    [Fact]
    public void SettingAFieldSpellAlsoReplacesTheOldOne()
    {
        var e = Engine(Deck(Yami, Sogen), Deck());
        EnterMain(e);
        Activate(e, Yami, FieldZone); Resolve(e);
        SetCard(e, Sogen, FieldZone);
        Assert.True(InGraveyard(e, PlayerId.One, Yami));
        Assert.False(P(e, PlayerId.One).FieldSpell!.IsFaceUp);
    }

    [Fact]
    public void OpponentsFieldSpellLeavesYoursInPlace()
    {
        var e = Engine(Deck(Yami), Deck(Sogen));
        EnterMain(e); Activate(e, Yami, FieldZone); Resolve(e);
        Next(e); Activate(e, Sogen, FieldZone); Resolve(e);
        Assert.Equal(Yami, P(e, PlayerId.One).FieldZone!.GameCardId);
        Assert.Equal(Sogen, P(e, PlayerId.Two).FieldZone!.GameCardId);
    }

    [Fact]
    public void FieldSpellCanBeDestroyedAndItsEffectEnds()
    {
        var e = Engine(Deck(Yami, Fiendish), Deck(Storm));
        EnterMain(e); Summon(e, Fiendish, 0);
        Activate(e, Yami, FieldZone); Resolve(e);
        Next(e); Activate(e, Storm);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SpellTrapDestroyed { Zone: FieldZone });
        Assert.Null(P(e, PlayerId.One).FieldZone);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void SetFieldSpellWithNegatedActivationGoesToTheGraveyard()
    {
        var e = Engine(Deck(Yami), Deck(Negate));
        EnterMain(e); SetCard(e, Yami, FieldZone); Next(e);
        SetCard(e, Negate, 0); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, FieldZone));
        Respond(e, PlayerId.Two, 0);
        Resolve(e);
        Assert.Null(P(e, PlayerId.One).FieldZone);
        Assert.True(InGraveyard(e, PlayerId.One, Yami));
    }

    // Equip Spells

    private static DuelEngine EquippedUnited()
    {
        var e = Engine(Deck(United), Deck(Hole, Storm));
        EnterMain(e); Summon(e, W, 0);
        Activate(e, United);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        return e;
    }

    [Fact]
    public void UnitedWeStandScalesWithFaceUpMonsters()
    {
        var e = EquippedUnited();
        Assert.Equal(1800, Atk(e, PlayerId.One, 0));
        Assert.Equal(1800, Def(e, PlayerId.One, 0));
        Next(e); Next(e);
        Summon(e, W, 1);
        Assert.Equal(2600, Atk(e, PlayerId.One, 0));
        Assert.Equal(1000, Atk(e, PlayerId.One, 1));
    }

    [Fact]
    public void MagePowerCountsSpellsAndTrapsYouControl()
    {
        var e = Engine(Deck(Mage, Storm, Yami), Deck());
        EnterMain(e); Summon(e, W, 0);
        SetCard(e, Storm, 1);
        Activate(e, Yami, FieldZone); Resolve(e);
        Activate(e, Mage);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Equal(2500, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void EquipIsDestroyedWhenItsMonsterLeaves()
    {
        var e = EquippedUnited();
        Next(e); Activate(e, Hole);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SpellTrapDestroyed d && d.Card.GameCardId == United);
        Assert.True(InGraveyard(e, PlayerId.One, United));
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[4]);
    }

    [Fact]
    public void DestroyingTheEquipEndsTheBonusAndKeepsTheMonster()
    {
        var e = EquippedUnited();
        Next(e); Activate(e, Storm); Resolve(e);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void EquipNeedsAFaceUpMonster()
    {
        var e = Engine(Deck(United, W), Deck());
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, United).InstanceId, 4), DuelError.DecisionUnavailable);
        Summon(e, W, 0, set: true);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, United).InstanceId, 4), DuelError.DecisionUnavailable);
    }

    private static DuelEngine UnitedOnChainAgainstSetCard(uint setCard)
    {
        var e = Engine(Deck(United), Deck(setCard));
        EnterMain(e); Next(e);
        SetCard(e, setCard, 0); Next(e);
        Summon(e, W, 0);
        Activate(e, United);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Respond(e, PlayerId.Two, 0);
        return e;
    }

    [Fact]
    public void NegatedEquipActivationIsNeverEquipped()
    {
        var e = UnitedOnChainAgainstSetCard(Negate);
        Resolve(e);
        Assert.True(InGraveyard(e, PlayerId.One, United));
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void NegatedEquipEffectStaysEquippedWithoutApplying()
    {
        var e = UnitedOnChainAgainstSetCard(EffectNegate);
        Resolve(e);
        Assert.NotNull(P(e, PlayerId.One).SpellTrapFields[4]!.EquippedTo);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void EquipWhoseTargetLeftGoesToTheGraveyard()
    {
        var e = UnitedOnChainAgainstSetCard(ResponseHole);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SpellSentToGraveyard s && s.Card.GameCardId == United);
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[4]);
    }

    // Revival links

    private static DuelEngine RevivedWithCall()
    {
        var e = Engine(Deck(Call), Deck(Hole, Storm, Banisher, Hole));
        EnterMain(e); Summon(e, W, 0); SetCard(e, Call, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        ResolveRevival(e, 2);
        return e;
    }

    [Fact]
    public void CallOfTheHauntedRevivesInAttackPositionOnly()
    {
        var e = Engine(Deck(Call), Deck(Hole));
        EnterMain(e); Summon(e, W, 0); SetCard(e, Call, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        var chain = e.State.PendingChain!;
        Ok(e, new PassChainResponse(chain.Responder, chain.DecisionId));
        chain = e.State.PendingChain!;
        Ok(e, new PassChainResponse(chain.Responder, chain.DecisionId));
        Ok(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId));
        var pending = e.State.PendingChain!.DefinitionSummon!;
        Reject(e, new ChooseDefinitionSummon(pending.Player, pending.DecisionId, pending.Options[0].Card.InstanceId, 2, BattlePosition.FaceUpDefense), DuelError.InvalidBattlePosition);
    }

    [Fact]
    public void CallLeavingTheFieldDestroysTheMonster()
    {
        var e = RevivedWithCall();
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[2]);
        Next(e); Activate(e, Storm);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is MonsterDestroyed { Zone: 2 });
        Assert.Null(P(e, PlayerId.One).MonsterZones[2]);
    }

    [Fact]
    public void MonsterDestroyedDestroysCall()
    {
        var e = RevivedWithCall();
        Next(e); Activate(e, Hole);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is SpellTrapDestroyed d && d.Card.GameCardId == Call);
        Assert.True(InGraveyard(e, PlayerId.One, Call));
    }

    [Fact]
    public void MonsterBanishedLeavesCallInPlace()
    {
        var e = RevivedWithCall();
        Next(e); Activate(e, Banisher); Resolve(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[2]);
        Assert.NotNull(P(e, PlayerId.One).SpellTrapFields[0]);
    }

    private static DuelEngine RevivedWithPremature()
    {
        var e = Engine(Deck(Premature), Deck(Hole, Storm, Hole));
        EnterMain(e); Summon(e, W, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Activate(e, Premature);
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        ResolveRevival(e, 3);
        return e;
    }

    [Fact]
    public void PrematureBurialPaysAndEquipsTheRevivedMonster()
    {
        var e = RevivedWithPremature();
        Assert.Equal(7200, P(e, PlayerId.One).LifePoints);
        Assert.Equal(3, P(e, PlayerId.One).SpellTrapFields[4]!.EquippedTo!.Zone);
    }

    [Fact]
    public void DestroyingPrematureBurialDestroysTheMonster()
    {
        var e = RevivedWithPremature();
        Next(e); Activate(e, Storm); Resolve(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[3]);
    }

    [Fact]
    public void PrematureBurialIsDestroyedWithItsMonster()
    {
        var e = RevivedWithPremature();
        Next(e); Activate(e, Hole); Resolve(e);
        Assert.True(InGraveyard(e, PlayerId.One, Premature));
        Assert.Equal(1, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId == W));
    }

    // Activated monster effects

    [Fact]
    public void CannonSoldierCanTributeItself()
    {
        var e = Engine(Deck(Cannon), Deck());
        EnterMain(e); Summon(e, Cannon, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Equal(7500, P(e, PlayerId.Two).LifePoints);
        Assert.True(InGraveyard(e, PlayerId.One, Cannon));
    }

    [Fact]
    public void IgnitionEffectsRespectTimingAndPosition()
    {
        var e = Engine(Deck(Cannon), Deck(Burn));
        EnterMain(e);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.MonsterZoneEmpty);
        Summon(e, Cannon, 0, set: true);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.UnsupportedMonsterEffect);

        var faceUp = Engine(Deck(Cannon), Deck(Burn));
        EnterMain(faceUp); Summon(faceUp, Cannon, 0); Next(faceUp);
        Activate(faceUp, Burn);
        Reject(faceUp, new ActivateMonsterChainResponse(PlayerId.One, faceUp.State.PendingChain!.DecisionId, 0), DuelError.ActivationTimingNotAllowed);
    }

    [Fact]
    public void ExiledForcePaysAtActivationAndResolvesAfterLeaving()
    {
        var e = Engine(Deck(W), Deck(Exiled));
        EnterMain(e); Summon(e, W, 0); Next(e);
        Summon(e, Exiled, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.Two, 0));
        Assert.True(InGraveyard(e, PlayerId.Two, Exiled));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void NegatedMonsterEffectDoesNotResolveButCostStaysPaid()
    {
        var e = Engine(Deck(W, Negate), Deck(Exiled));
        EnterMain(e); Summon(e, W, 0); SetCard(e, Negate, 0); Next(e);
        Summon(e, Exiled, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.Two, 0));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Respond(e, PlayerId.One, 0);
        Resolve(e);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        Assert.True(InGraveyard(e, PlayerId.Two, Exiled));
    }

    // Union

    private static DuelEngine UnionOnCannon(uint[]? second = null)
    {
        var e = Engine(Deck(XCannon, YDragon, ZTank), second ?? Deck());
        EnterMain(e); Summon(e, XCannon, 0); Next(e); Next(e);
        Summon(e, YDragon, 1);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 1));
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 0);
        Resolve(e);
        return e;
    }

    [Fact]
    public void UnionEquipsToItsListedTargetAndAddsItsBonus()
    {
        var e = UnionOnCannon();
        Assert.Null(P(e, PlayerId.One).MonsterZones[1]);
        Assert.True(P(e, PlayerId.One).SpellTrapFields[0] is { IsUnion: true, EffectActive: true });
        Assert.Equal(1400, Atk(e, PlayerId.One, 0));
        Assert.Equal(1400, Def(e, PlayerId.One, 0));
    }

    [Fact]
    public void UnionNeedsAListedTarget()
    {
        var e = Engine(Deck(YDragon), Deck());
        EnterMain(e); Summon(e, YDragon, 0);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void UnionUsesOneActionPerTurnAcrossEquipAndUnequip()
    {
        var e = UnionOnCannon();
        Reject(e, new UnequipUnion(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Next(e); Next(e);
        Ok(e, new UnequipUnion(PlayerId.One, 0));
        Resolve(e);
        Assert.Equal(BattlePosition.FaceUpAttack, P(e, PlayerId.One).MonsterZones[1]!.Position);
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[0]);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void MonsterCanHoldOnlyOneUnion()
    {
        var e = UnionOnCannon();
        Next(e); Next(e);
        Summon(e, ZTank, 1);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 1), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void UnionIsDestroyedInsteadOfTheMonsterInBattle()
    {
        var e = UnionOnCannon(Deck(Fairy));
        Next(e); Summon(e, Fairy, 0);
        Phase(e, DuelPhase.Battle);
        var events = Ok(e, new DeclareAttack(PlayerId.Two, 0, 0)).Events;
        Assert.Contains(events, ev => ev is DestructionReplaced);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
        Assert.True(InGraveyard(e, PlayerId.One, YDragon));
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void UnionIsDestroyedWhenItsMonsterLeaves()
    {
        var e = UnionOnCannon(Deck(Hole));
        Next(e); Activate(e, Hole); Resolve(e);
        Assert.True(InGraveyard(e, PlayerId.One, YDragon));
    }

    // Toon

    private static void ToonSummon(DuelEngine e, uint id, int zone, params int[] tributes) =>
        Ok(e, new ToonSummon(Turn(e), Hand(e, id).InstanceId, zone, BattlePosition.FaceUpAttack, tributes, e.State.Revision));

    private static DuelEngine MermaidReady(uint[]? second = null)
    {
        var e = Engine(Deck(ToonWorld, Mermaid), second ?? Deck());
        EnterMain(e);
        Activate(e, ToonWorld); Resolve(e);
        ToonSummon(e, Mermaid, 0);
        return e;
    }

    [Fact]
    public void ToonSummonNeedsToonWorld()
    {
        var e = Engine(Deck(Mermaid), Deck());
        EnterMain(e);
        Reject(e, new ToonSummon(PlayerId.One, Hand(e, Mermaid).InstanceId, 0, BattlePosition.FaceUpAttack, [], e.State.Revision), DuelError.ToonSummonUnavailable);
        Reject(e, new NormalSummon(PlayerId.One, Hand(e, Mermaid).InstanceId, 0), DuelError.NormalSummonNotAllowed);
    }

    [Fact]
    public void ToonWorldPaysAndToonSummonUsesLevelTributes()
    {
        var e = Engine(Deck(ToonWorld, ToonSkull), Deck());
        EnterMain(e);
        Activate(e, ToonWorld); Resolve(e);
        Assert.Equal(7000, P(e, PlayerId.One).LifePoints);
        Reject(e, new ToonSummon(PlayerId.One, Hand(e, ToonSkull).InstanceId, 1, BattlePosition.FaceUpAttack, [], e.State.Revision), DuelError.InvalidTributeSelection);
        Summon(e, W, 0);
        ToonSummon(e, ToonSkull, 1, 0);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[1]);
    }

    [Fact]
    public void ToonCannotAttackTheTurnItIsSummoned()
    {
        var e = Engine(Deck(ToonWorld, Mermaid), Deck());
        EnterMain(e); Activate(e, ToonWorld); Resolve(e); Next(e); Next(e);
        ToonSummon(e, Mermaid, 0);
        Phase(e, DuelPhase.Battle);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.ToonCannotAttack);
    }

    [Fact]
    public void ToonPaysToAttackDirectlyPastNonToons()
    {
        var e = MermaidReady(Deck(W));
        Next(e); Summon(e, W, 0); Next(e);
        Phase(e, DuelPhase.Battle);
        Ok(e, new DeclareAttack(PlayerId.One, 0));
        Assert.Equal(6500, P(e, PlayerId.One).LifePoints);
        Assert.Equal(6600, P(e, PlayerId.Two).LifePoints);
    }

    [Fact]
    public void ToonMustAttackAnOpposingToon()
    {
        var e = MermaidReady(Deck(ToonWorld, Mermaid));
        Next(e); Activate(e, ToonWorld); Resolve(e); ToonSummon(e, Mermaid, 0); Next(e);
        Phase(e, DuelPhase.Battle);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.DirectAttackBlocked);
        Ok(e, new DeclareAttack(PlayerId.One, 0, 0));
    }

    [Fact]
    public void DestroyingToonWorldDestroysToons()
    {
        var e = MermaidReady(Deck(Storm));
        Next(e); Activate(e, Storm);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is MonsterDestroyed d && d.Card.GameCardId == Mermaid);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
    }

    // Counters

    private static DuelEngine BreakerOnField()
    {
        var e = Engine(Deck(Breaker, Storm), Deck());
        EnterMain(e); Summon(e, Breaker, 0);
        EventEffectScenario.Drain(e);
        return e;
    }

    [Fact]
    public void BreakerGainsAttackFromItsCounter()
    {
        var e = BreakerOnField();
        Assert.Equal(1, P(e, PlayerId.One).MonsterZones[0]!.Counters);
        Assert.Equal(1300, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void BreakerRemovesItsCounterToDestroyASpellOrTrap()
    {
        var e = BreakerOnField();
        SetCard(e, Storm, 1);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Target(e, PlayerId.One, FieldLocation.SpellTrapZone, 1);
        Resolve(e);
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[1]);
        Assert.Equal(0, P(e, PlayerId.One).MonsterZones[0]!.Counters);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void BreakerCannotPayWithoutACounter()
    {
        var e = Engine(Deck(Breaker, Storm), Deck());
        EnterMain(e); Summon(e, Breaker, 0, set: true);
        Next(e); Next(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack));
        SetCard(e, Storm, 1);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.DecisionUnavailable);
    }

    // Banish cost

    [Fact]
    public void BanishCostMovesTheChosenCardAndNeedsOne()
    {
        var e = Engine(Deck(BanishCost, W), Deck(Hole));
        EnterMain(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, BanishCost).InstanceId, 4), DuelError.DecisionUnavailable);
        Summon(e, W, 0); Next(e);
        Activate(e, Hole); Resolve(e); Next(e);
        Activate(e, BanishCost);
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        Assert.Contains(P(e, PlayerId.One).Banished, c => c.GameCardId == W);
        Resolve(e);
        Assert.Equal(7900, P(e, PlayerId.Two).LifePoints);
    }

    // Continuous negation

    [Fact]
    public void SkillDrainNegatesMonsterEffectsWhileFaceUp()
    {
        var e = Engine(Deck(Breaker, Drain), Deck(Storm));
        EnterMain(e); Summon(e, Breaker, 0);
        EventEffectScenario.Drain(e);
        SetCard(e, Drain, 0); Next(e); Next(e);
        Assert.Equal(1300, Atk(e, PlayerId.One, 0));

        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Assert.Equal(7000, P(e, PlayerId.One).LifePoints);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));

        Next(e); Activate(e, Storm); Resolve(e);
        Assert.Equal(1300, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void SkillDrainStopsAnActivatedEffectAtResolution()
    {
        var e = Engine(Deck(Breaker, Drain, Storm), Deck());
        EnterMain(e); Summon(e, Breaker, 0);
        EventEffectScenario.Drain(e);
        SetCard(e, Drain, 0); SetCard(e, Storm, 1); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Target(e, PlayerId.One, FieldLocation.SpellTrapZone, 1);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceEffectsNegated });
        Assert.NotNull(P(e, PlayerId.One).SpellTrapFields[1]);
        Assert.Equal(0, P(e, PlayerId.One).MonsterZones[0]!.Counters);
    }

    private static DuelEngine ImperialActive(uint[] first)
    {
        var e = Engine(first, Deck());
        EnterMain(e); SetCard(e, Imperial, 0); Next(e); Next(e);
        return e;
    }

    [Fact]
    public void ImperialOrderNegatesSpellsAndLeavesThemInPlace()
    {
        var e = Engine(Deck(Imperial, Yami, Burn, Fiendish), Deck());
        EnterMain(e); Summon(e, Fiendish, 0); SetCard(e, Imperial, 0); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Activate(e, Burn);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceEffectsNegated });
        Assert.Equal(8000, P(e, PlayerId.Two).LifePoints);
        Activate(e, Yami, FieldZone); Resolve(e);
        Assert.Equal(Yami, P(e, PlayerId.One).FieldZone!.GameCardId);
        Assert.Equal(1000, Atk(e, PlayerId.One, 0));
    }

    [Fact]
    public void ImperialOrderUpkeepMustBeSettledBeforeMainPhase()
    {
        var e = ImperialActive(Deck(Imperial));
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Next(e);
        ToStandby(e);
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.Main1), DuelError.UpkeepPending);
        Ok(e, new ResolveUpkeep(PlayerId.One, 0, true));
        Assert.Equal(7300, P(e, PlayerId.One).LifePoints);
        Reject(e, new ResolveUpkeep(PlayerId.One, 0, true), DuelError.NoUpkeepDue);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.Main1));
    }

    [Fact]
    public void ImperialOrderIsDestroyedIfUpkeepIsDeclined()
    {
        var e = ImperialActive(Deck(Imperial));
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Resolve(e);
        Next(e);
        ToStandby(e);
        Ok(e, new ResolveUpkeep(PlayerId.One, 0, false));
        Assert.True(InGraveyard(e, PlayerId.One, Imperial));
        Assert.Equal(8000, P(e, PlayerId.One).LifePoints);
    }

    // Adapter

    [Fact]
    public void AdapterOffersFieldZoneMonsterAndUpkeepActions()
    {
        var e = Engine(Deck(Yami, Cannon), Deck());
        var source = new DuelActionSource(e, Catalog());
        EnterMain(e); Summon(e, Cannon, 0);
        var actions = source.Observe(PlayerId.One).Actions;
        Assert.Contains(actions, a => a.Kind == ActionKind.ActivateSpell && a.Zone == FieldZone);
        Assert.Contains(actions, a => a.Kind == ActionKind.MonsterEffect && a.Zone == 0);
    }

    [Fact]
    public void ViewsShowTheFieldSpellAndHideASetOne()
    {
        var e = Engine(Deck(Yami), Deck());
        EnterMain(e); SetCard(e, Yami, FieldZone);
        Assert.False(PlayerProjection.Create(e.State, PlayerId.One, PlayerId.One, Catalog()).FieldSpell!.Hidden);
        Assert.True(PlayerProjection.Create(e.State, PlayerId.One, PlayerId.Two, Catalog()).FieldSpell!.Hidden);
    }

    [Fact]
    public void DefinitionsValidateTheirKindsAndTargets()
    {
        var modifier = new LastingStatModifier(TargetSide.Either, 100, 0, equippedOnly: true);
        Assert.Throws<ArgumentException>(() => new LastingStatModifier(TargetSide.Own, 100, 0, equippedOnly: true));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Mage, EffectKind.EquipSpell, [], lastingModifiers: [modifier]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Mage, EffectKind.NormalSpell, [new EquipTargetStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Yami, EffectKind.ContinuousSpell, [], isMarker: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectDefinition("x", Yami, EffectKind.NormalSpell, [new DealDamageStep(1)], cost: new TributeSelfCost()));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", Yami, EffectKind.NormalSpell, [new DealDamageStep(1)], oncePerTurn: true));
    }
}
