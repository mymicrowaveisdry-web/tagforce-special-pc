using System.Text.Json;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

/// <summary>Pendulum tests.</summary>
public sealed class PendulumTests
{
    private const uint Left = 200_001, Right = 200_002, Pend = 200_003, Ordinary = 200_004,
        Low = 200_005, High = 200_006, Negator = 200_007, Response = 200_008, FusionSpell = 200_009,
        RitualSpell = 200_010, Fusion = 200_011, Ritual = 200_012, Tribute = 200_013, Xyz = 200_014;

    private static DuelEngine Make(IEnumerable<uint>? opening = null, ChainOperation? response = ChainOperation.NegateEffect,
        bool groupNegator = true, bool triggers = false, uint leftScale = 1, uint rightScale = 8,
        MaterialDestination genericDestination = MaterialDestination.Graveyard, bool xyzDetach = false)
    {
        uint[] ids = [Left, Right, Pend, Ordinary, Low, High, Negator, Response, FusionSpell, RitualSpell, Fusion, Ritual, Tribute, Xyz];
        var catalog = new CardCatalog(new(null, "synthetic-pendulum"), ids.Select(id => new CardDefinition(id, $"{id}", "",
            new(id == High ? 3000 : 1000, 1000, id == Low ? 1u : id == High ? 8u : id is Ritual or Tribute ? 8u : id is Fusion or Xyz ? 0u : 4u))));
        var rules = ids.ToDictionary(id => id, id => new DuelCardRules(
            id is Negator or Response ? CardCategory.Trap : id is FusionSpell or RitualSpell ? CardCategory.Spell : CardCategory.Monster,
            AllowsNormalSummon: id is Left or Right or Pend or Ordinary or Low or High or Tribute,
            AllowsBasicBattle: id is Left or Right or Pend or Ordinary or Low or High or Fusion or Ritual or Tribute or Xyz,
            AllowsSpellTrapSet: id is Negator or Response, AllowsTributeSummon: id is High or Tribute,
            AllowsTributeForSummon: id is Pend or Ordinary or Left or Right,
            AllowsExtraDeck: id is Fusion or Xyz, AllowsSummonMaterial: id is Pend or Ordinary or Left or Right,
            AllowsPendulumSummon: id is Pend or Ordinary or Low or High or Ritual));
        var first = (opening ?? new uint[] { Left, Right, Pend, Ordinary, Pend }).ToArray();
        uint[] second = [Negator, Response, High, Low, Low, .. Enumerable.Repeat(Low, 35)];
        List<TriggerEffectProfile> effects = triggers ? [
            new("pend-when-summoned", Pend, MonsterEventKind.SpecialSummoned, EffectRelation.Self, TriggerRequirement.Optional,
                TriggerOperation.GainLifePoints, lifePointGain: 100, timing: TriggerTiming.When),
            new("ordinary-when-summoned", Ordinary, MonsterEventKind.SpecialSummoned, EffectRelation.Self, TriggerRequirement.Optional,
                TriggerOperation.GainLifePoints, lifePointGain: 100, timing: TriggerTiming.When),
            Gain("pend-grave", Pend, MonsterEventKind.SentToGraveyard, gain: 700, location: TriggerSourceLocation.GraveyardSelf),
            Gain("pend-destroyed", Pend, MonsterEventKind.Destroyed, gain: 200, location: TriggerSourceLocation.DestroyedSelf)] : [];
        if (xyzDetach) effects.Add(new("xyz-detach", Xyz, MonsterEventKind.SpecialSummoned, EffectRelation.Self,
            TriggerRequirement.Optional, TriggerOperation.GainLifePoints, lifePointGain: 100, detachMaterialCount: 1));
        return new(catalog, rules, first.Concat(Enumerable.Repeat(Ordinary, 40 - first.Length)), second,
            playerOneExtraDeck: [Fusion, Xyz], experimentalPendulumCards: [new(Left, leftScale, 99), new(Right, 99, rightScale), new(Pend, 0, 9)],
            experimentalSummonNegations: [new("group-negator", Negator, kinds: SummonNegationKinds.Special, allowsMultipleSummons: groupNegator)],
            // No operation: the response is a targeted destruction definition (Normal Trap) instead of a negation probe.
            experimentalChainResponses: response is { } negation ? [new("response", Response, negation, ChainSpellSpeed.Three, respondsToCategory: CardCategory.Spell)] : [],
            effectDefinitions: response is null ? [new("response-destroy", Response, EffectKind.NormalTrap, [new DestroyTargetStep()], target: new SpellTrapOnFieldTarget())] : [],
            experimentalTriggerEffects: effects,
            experimentalSpecialSummonCards: [new(Fusion), new(Ritual, SpecialSummonSources.None, "ritual-proper"), new(Xyz)],
            experimentalMaterialProcedures: [new("generic", [Fusion], [Pend, Ordinary], 2, genericDestination),
                new("xyz", [Xyz], [Pend, Ordinary], 2, MaterialDestination.Attached, xyz: new(4))],
            experimentalSummonEffects: [new("fusion-effect", FusionSpell, "fusion-proper", fusionRecipes: [new(Fusion, [Pend, Ordinary])]),
                new("ritual-effect", RitualSpell, "ritual-proper", ritualRecipes: [new(Ritual, 8, [Pend, Ordinary])])]);
    }

    private static void Scale(DuelEngine e, uint id, int zone)
    {
        var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == id);
        Ok(e, new ActivatePendulumScale(PlayerId.One, card.InstanceId, zone)); Drain(e);
    }
    private static void Scales(DuelEngine e) { EnterMain(e); Scale(e, Left, 0); Scale(e, Right, 1); }
    private static void Complete(DuelEngine e)
    {
        for (int i = 0; i < 2; i++) { var p = e.State.PendingSummon!; Ok(e, new PassSummonResponse(p.Responder, p.DecisionId)); }
    }
    private static PendulumSummon SummonCommand(DuelEngine e, params uint[] ids)
    {
        var hand = e.State.GetPlayer(PlayerId.One).Hand.ToList();
        return new(PlayerId.One, e.State.Revision, ids.Select((id, zone) =>
        {
            var card = hand.First(c => c.GameCardId == id); hand.Remove(card);
            return new PendulumSummonSelection(card.InstanceId, DecisionCardSource.Hand, zone);
        }));
    }
    private static MonsterBinding Bound(DuelEngine e, int zone)
    {
        var m = e.State.GetPlayer(PlayerId.One).MonsterZones[zone]!;
        return new(PlayerId.One, zone, m.Card, m.PlacementId);
    }

    [Theory]
    [InlineData(1u, 8u)] [InlineData(8u, 1u)] [InlineData(0u, uint.MaxValue)]
    public void DedicatedScalesPermitSimultaneousOrdinaryAndPendulumHandSummons(uint left, uint right)
    {
        var e = Make(triggers: true, leftScale: left, rightScale: right); Scales(e);
        Assert.All(e.State.GetPlayer(PlayerId.One).SpellTrapFields, Assert.Null);
        var before = e.State; var json = JsonSerializer.Serialize(before);
        var command = SummonCommand(e, Ordinary, Pend); Ok(e, command);
        Assert.Equal(2, e.State.PendingSummon!.PendulumGroup.Count);
        Assert.All(e.State.GetPlayer(PlayerId.One).MonsterZones, Assert.Null);
        Assert.Null(e.State.EventEffects.PendingTriggers);
        Complete(e);
        var player = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(new uint[] { Ordinary, Pend }, player.MonsterZones.Take(2).Select(m => m!.Card.GameCardId));
        Assert.False(e.State.NormalSummonUsed);
        Assert.Equal(2, e.State.EventEffects.PendingTriggers!.Options.Count);
        Assert.All(e.State.SummonHistory.Where(h => command.Selections.Any(s => s.CardInstanceId == h.Card.InstanceId)),
            h => { Assert.Equal(SummonMethod.Pendulum, h.Method); Assert.Equal(SummonStatus.Completed, h.Status); });
        Assert.Equal(json, JsonSerializer.Serialize(before));
        Drain(e); Assert.Equal(8200, e.State.GetPlayer(PlayerId.One).LifePoints);
        Reject(e, SummonCommand(e, Pend), DuelError.PendulumSummonLimitReached);
        Summon(e, Pend, 2); Assert.True(e.State.NormalSummonUsed);
    }

    [Fact]
    public void InvalidScaleAndBatchSelectionsRejectWithoutPartialMovement()
    {
        var e = Make([Left, Right, Low, High, Pend]); EnterMain(e);
        Reject(e, SummonCommand(e, Pend), DuelError.PendulumScalesUnavailable);
        Reject(e, new ActivatePendulumScale(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[2].InstanceId, 0), DuelError.UnsupportedPendulumCard);
        Reject(e, new ActivatePendulumScale(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand[0].InstanceId, 2), DuelError.InvalidPendulumZone);
        Scale(e, Left, 0);
        Reject(e, new ActivatePendulumScale(PlayerId.One, e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Right).InstanceId, 0), DuelError.PendulumZoneOccupied);
        Scale(e, Right, 1);
        Reject(e, SummonCommand(e, Low), DuelError.UnsupportedMonsterLevel);
        Reject(e, SummonCommand(e, High), DuelError.UnsupportedMonsterLevel);
        var valid = SummonCommand(e, Pend); var choice = valid.Selections[0];
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [choice, choice]), DuelError.InvalidSummonMaterials);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [choice with { Source = DecisionCardSource.ExtraDeck }]), DuelError.SpecialSummonSourceUnavailable);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [choice with { MonsterZone = 5 }]), DuelError.InvalidMonsterZone);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision - 1, [choice]), DuelError.StaleSummonSelection);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [choice with { Position = BattlePosition.FaceDownDefense }]), DuelError.InvalidBattlePosition);
        Ok(e, valid); Complete(e);
    }

    [Theory]
    [InlineData(ChainOperation.NegateActivation)] [InlineData(ChainOperation.NegateEffect)] [InlineData(null)]
    public void ScaleActivationUsesSpellResponsesAndDistinctNegationDepartureRules(ChainOperation? operation)
    {
        var e = Make(response: operation); EnterMain(e); NextMain(e); Set(e, Response); NextMain(e);
        var card = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Left);
        Ok(e, new ActivatePendulumScale(PlayerId.One, card.InstanceId, 0));
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).PendulumZones[0]);
        Reject(e, SummonCommand(e, Pend), DuelError.ChainPending);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        if (operation is null) // targeted destruction
        {
            var p = e.State.PendingDecision!; var target = p.TargetOptions.Single(t => t.Card == card);
            Assert.True(target.IsPendulumZone);
            Reject(e, new ChooseDecisionTarget(PlayerId.Two, p.DecisionId, PlayerId.One, 0, card.InstanceId), DuelError.InvalidDecisionChoice);
            Ok(e, new ChooseDecisionTarget(PlayerId.Two, p.DecisionId, PlayerId.One, 0, card.InstanceId, true));
        }
        Drain(e); var player = e.State.GetPlayer(PlayerId.One);
        if (operation == ChainOperation.NegateActivation) { Assert.Contains(card, player.Graveyard); Assert.Null(player.PendulumZones[0]); Assert.Empty(player.FaceUpExtraDeck); }
        else if (operation == ChainOperation.NegateEffect) { Assert.Equal(card, player.PendulumZones[0]!.Card); Assert.Empty(player.Graveyard); Assert.Empty(player.FaceUpExtraDeck); }
        else { Assert.Contains(card, player.FaceUpExtraDeck); Assert.Null(player.PendulumZones[0]); Assert.Empty(player.Graveyard); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BatchNegationRequiresOptInAndSendsAllUnsuccessfulCardsToGraveyard(bool multiple)
    {
        var e = Make(groupNegator: multiple); Scales(e); NextMain(e); Set(e, Negator); NextMain(e);
        var command = SummonCommand(e, Pend, Ordinary); Ok(e, command); var pending = e.State.PendingSummon!;
        var negate = new ActivateSummonNegation(PlayerId.Two, pending.DecisionId, 0);
        if (!multiple) { Reject(e, negate, DuelError.UnsupportedSummonResponse); Complete(e); return; }
        Ok(e, negate); Drain(e);
        var player = e.State.GetPlayer(PlayerId.One);
        Assert.Equal(2, player.Graveyard.Count); Assert.Empty(player.FaceUpExtraDeck); Assert.All(player.MonsterZones, Assert.Null);
        Assert.All(e.State.SummonHistory.Where(h => command.Selections.Any(s => s.CardInstanceId == h.Card.InstanceId)), h => Assert.Equal(SummonStatus.Negated, h.Status));
        Reject(e, SummonCommand(e, Pend), DuelError.PendulumSummonLimitReached);
    }

    [Fact]
    public void BattleDestructionRoutesToPublicExtraDeckPreservingDestructionButNotGraveyardTriggers()
    {
        var e = Make(triggers: true); Scales(e); Summon(e, Pend);
        var host = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        var old = e.State; var json = JsonSerializer.Serialize(old);
        NextMain(e); Summon(e, Low); Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
        var result = Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
        Assert.Contains(host, e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
        Assert.DoesNotContain(host, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(FieldCardDestination.FaceUpExtraDeck, result.Events.OfType<MonsterDestroyed>().Single(x => x.Card == host).FieldDestination);
        Assert.Equal("pend-destroyed", Assert.Single(e.State.EventEffects.PendingTriggers!.Options).Profile.EffectId);
        Drain(e); Assert.Equal(8200, e.State.GetPlayer(PlayerId.One).LifePoints);
        Assert.Equal(json, JsonSerializer.Serialize(old));
        NextMain(e);
        var ordinary = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == Ordinary);
        var command = new PendulumSummon(PlayerId.One, e.State.Revision, [new(host.InstanceId, DecisionCardSource.FaceUpExtraDeck, 0),
            new(ordinary.InstanceId, DecisionCardSource.Hand, 1, BattlePosition.FaceUpDefense)]);
        Ok(e, command); Complete(e);
        Assert.Empty(e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
        Assert.Equal(host, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card);
        Assert.Equal(DecisionCardSource.FaceUpExtraDeck, e.State.SummonHistory.Single(h => h.Card == host).Source);
        Assert.Equal(2, e.State.EventEffects.PendingTriggers!.Options.Count);
    }

    [Theory]
    [InlineData(MaterialDestination.Graveyard)] [InlineData(MaterialDestination.Banished)] [InlineData(MaterialDestination.Attached)]
    public void GenericMaterialDestinationDistinguishesPendulumRedirectBanishmentAndAttachment(MaterialDestination destination)
    {
        var e = Make([Pend, Ordinary, Left, Right, Tribute], genericDestination: destination == MaterialDestination.Attached ? MaterialDestination.Graveyard : destination,
            xyzDetach: destination == MaterialDestination.Attached);
        EnterMain(e); Summon(e, Pend); NextMain(e); NextMain(e); Summon(e, Ordinary, 1);
        var pend = Bound(e, 0); var ordinary = Bound(e, 1); var before = e.State; var json = JsonSerializer.Serialize(before);
        string procedure = destination == MaterialDestination.Attached ? "xyz" : "generic";
        uint targetId = destination == MaterialDestination.Attached ? Xyz : Fusion;
        var target = e.State.GetPlayer(PlayerId.One).ExtraDeck.Single(c => c.GameCardId == targetId);
        var result = Ok(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, procedure, target.InstanceId, 0, [pend, ordinary]));
        var player = e.State.GetPlayer(PlayerId.One);
        if (destination == MaterialDestination.Graveyard)
        {
            Assert.Contains(pend.Card, player.FaceUpExtraDeck); Assert.Contains(ordinary.Card, player.Graveyard);
            Assert.Single(result.Events.OfType<SummonMaterialSentToFaceUpExtraDeck>());
            Assert.Single(result.Events.OfType<SummonMaterialSentToGraveyard>());
        }
        else if (destination == MaterialDestination.Banished)
        {
            Assert.Empty(player.FaceUpExtraDeck); Assert.Contains(pend.Card, player.Banished); Assert.Contains(ordinary.Card, player.Banished);
        }
        else
        {
            Assert.Empty(player.FaceUpExtraDeck); Assert.Empty(player.Graveyard);
            Assert.Equal(new[] { pend.Card, ordinary.Card }, player.MonsterZones[0]!.XyzMaterials);
            Order(e); var commitment = e.State.EventEffects.PendingCommitment!;
            Ok(e, new ChooseTriggerXyzMaterials(PlayerId.One, commitment.DecisionId, [pend.Card.InstanceId]));
            Drain(e);
            Assert.Contains(pend.Card, e.State.GetPlayer(PlayerId.One).Graveyard);
            Assert.Empty(e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
            NextMain(e); Summon(e, Low); Ok(e, new ChangePhase(PlayerId.Two, DuelPhase.Battle));
            Ok(e, new DeclareAttack(PlayerId.Two, 0, 0));
            Assert.Contains(pend.Card, e.State.GetPlayer(PlayerId.One).Graveyard);
            Assert.Empty(e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck); // Attachment storage is not the field.
        }
        Assert.Equal(json, JsonSerializer.Serialize(before));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void FusionAndRitualRedirectOnlyFieldMaterialsAndPreserveHandMaterialGraveyardMovement(bool ritual, bool pendulumOnField)
    {
        uint spellId = ritual ? RitualSpell : FusionSpell;
        var e = Make([Pend, Ordinary, spellId, Ritual, Pend]); EnterMain(e); Summon(e, pendulumOnField ? Pend : Ordinary);
        var field = e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Card;
        var hand = e.State.GetPlayer(PlayerId.One).Hand.First(c => c.GameCardId == (pendulumOnField ? Ordinary : Pend));
        var spell = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == spellId);
        Ok(e, new ActivateSpell(PlayerId.One, spell.InstanceId, 0)); Pass(e); Pass(e); Resolve(e);
        var pending = e.State.PendingChain!.SummonDecision!;
        var result = Ok(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, pending.Targets[0].InstanceId,
            [field.InstanceId, hand.InstanceId], 0));
        Assert.Contains(hand, e.State.GetPlayer(PlayerId.One).Graveyard);
        if (pendulumOnField)
        {
            Assert.Contains(field, e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
            Assert.DoesNotContain(field, e.State.GetPlayer(PlayerId.One).Graveyard);
            if (ritual) Assert.Equal(FieldCardDestination.FaceUpExtraDeck, result.Events.OfType<RitualMaterialTributed>().Single(x => x.Material.Card == field).FieldDestination);
            else Assert.Single(result.Events.OfType<FusionMaterialSentToFaceUpExtraDeck>());
        }
        else
        {
            Assert.Contains(field, e.State.GetPlayer(PlayerId.One).Graveyard);
            Assert.Empty(e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
        }
        Assert.Empty(result.Events.OfType<MonsterDestroyed>());
    }

    [Fact]
    public void TributeDepartureRedirectsPendulumAndPreservesOtherTributeGraveyard()
    {
        var e = Make([Pend, Ordinary, Tribute, Left, Right]); EnterMain(e); Summon(e, Pend);
        NextMain(e); NextMain(e); Summon(e, Ordinary, 1); NextMain(e); NextMain(e);
        var pend = Bound(e, 0); var ordinary = Bound(e, 1);
        var high = e.State.GetPlayer(PlayerId.One).Hand.Single(c => c.GameCardId == Tribute);
        var result = Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, high.InstanceId, 0, [ordinary, pend]));
        Assert.Contains(pend.Card, e.State.GetPlayer(PlayerId.One).FaceUpExtraDeck);
        Assert.Contains(ordinary.Card, e.State.GetPlayer(PlayerId.One).Graveyard);
        Assert.Equal(2, result.Events.OfType<MonsterTributed>().Count());
        Assert.Equal(FieldCardDestination.FaceUpExtraDeck, result.Events.OfType<MonsterTributed>().Single(x => x.Monster == pend).FieldDestination);
    }

    [Fact]
    public void RestrictedProperProcedureAndEqualScalesDoNotPermitPendulumSummoning()
    {
        var e = Make([Left, Right, Ritual, Pend, Ordinary], leftScale: 0, rightScale: 10); Scales(e);
        Reject(e, SummonCommand(e, Ritual), DuelError.ProperSummonRequired);
        var equal = Make(leftScale: 4, rightScale: 4); Scales(equal);
        Reject(equal, SummonCommand(equal, Pend), DuelError.UnsupportedMonsterLevel);
    }

    [Fact]
    public void NewOwnTurnRestoresAllowanceButOccupiedOrDuplicateDestinationSelectionsRemainAtomic()
    {
        var e = Make(); Scales(e);
        var group = SummonCommand(e, Pend, Ordinary);
        Reject(e, new PendulumSummon(PlayerId.One, e.State.Revision, [group.Selections[0], group.Selections[1] with { MonsterZone = 0 }]), DuelError.InvalidSummonMaterials);
        Ok(e, group); Complete(e); NextMain(e); NextMain(e);
        var next = SummonCommand(e, Pend); // A later turn restores only the once-per-turn allowance, not occupied slots.
        Reject(e, next, DuelError.MonsterZoneOccupied);
        Ok(e, new PendulumSummon(PlayerId.One, e.State.Revision, [next.Selections[0] with { MonsterZone = 2 }])); Complete(e);
        Assert.Equal(3, e.State.GetPlayer(PlayerId.One).MonsterZones.Count(m => m is not null));
    }
}
