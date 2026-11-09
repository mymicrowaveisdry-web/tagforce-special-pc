using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Summon procedures and Extra Deck mechanics, one test per rule rather than per card.</summary>
public sealed class SummonProcedureFamilyTests
{
    private const uint W = 901, DarkOne = 902, WaterOne = 903, FireOne = 904, L3 = 905, L2 = 906, TunerTwo = 907, L5 = 908,
        Mill = 910, Reborn = 911, Hole = 912, Negator = 913, SpellNegator = 914, DetachXyz = 915,
        Cyber = 3847, Chaos = 5833, Frost = 7210, Poly = 4837, GaiaFusion = 4043, GaiaA = 3809, GaiaB = 4044, Curse = 4045, BlackLuster = 4370, LusterRitual = 4676,
        Pearl = 9601, Wayne = 7835, Comrade = 7895;
    private const string DetachProcedure = "xyz-test-4";

    private static readonly EffectDefinition[] Extras =
    [
        new("mill", Mill, EffectKind.NormalSpell, [new MillTopOfDeckStep(3)]),
        new("reborn", Reborn, EffectKind.NormalSpell, [new SpecialSummonTargetStep()],
            fieldTargets: [new ZoneTarget(CardZone.Graveyard, TargetSide.Own, new AnyMonsterFilter(), mustBeSummonable: true)]),
        new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
        ResponseProbe.NegateSummon("negator", Negator, kinds: SummonKinds.Special),
        new("spell-negator", SpellNegator, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells),
        new("detach-xyz", DetachXyz, EffectKind.MonsterIgnition, [new DealDamageStep(2000)], cost: new DetachCost(1), oncePerTurn: true)
    ];

    private static CardDefinition Probe(uint id, uint level, uint attribute, uint type, int attack = 1000) =>
        new(id, $"Probe {id}", "", new(attack, 1000, level), new(attribute, type, 1, 1, 0, 0, 0));

    private static CardCatalog Catalog() => new(new(null, "synthetic-summon-family"),
        SummonProcedureFamily.CardRules.Keys.Concat(Extras.Select(d => d.CardId)).Concat([W, DarkOne, WaterOne, FireOne, L3, L2, TunerTwo, L5]).Distinct().Select(id => id switch
        {
            W => Probe(id, 4, 1, 15), DarkOne => Probe(id, 4, 2, 15), WaterOne => Probe(id, 4, 3, 7), FireOne => Probe(id, 4, 4, 7),
            L3 => Probe(id, 3, 1, 7), L2 => Probe(id, 2, 1, 7), TunerTwo => Probe(id, 2, 1, 7), L5 => Probe(id, 5, 1, 7),
            Chaos => Probe(id, 6, 2, 18, 2300), Frost => Probe(id, 6, 3, 1), BlackLuster or 4366 => Probe(id, 8, 5, 15), 4362 => Probe(id, 7, 5, 11), GaiaFusion => Probe(id, 7, 6, 1),
            Pearl or DetachXyz => Probe(id, 4, 5, 6), Wayne => Probe(id, 5, 5, 15), Comrade => Probe(id, 3, 5, 15), Cyber => Probe(id, 5, 1, 7),
            _ => Probe(id, 4, 1, 15)
        }));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second, IEnumerable<uint>? extra = null)
    {
        var rules = SummonProcedureFamily.CardRules.ToDictionary();
        DuelCardRules monster = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true, AllowsTributeSummon: true);
        foreach (uint id in new[] { W, DarkOne, WaterOne, FireOne, L3, L2, L5 }) rules[id] = monster;
        rules[TunerTwo] = monster with { IsTuner = true };
        foreach (var definition in Extras)
            rules[definition.CardId] = definition.IsMonsterEffect ? new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true)
                : new(definition.Kind is EffectKind.NormalSpell ? CardCategory.Spell : CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(Catalog(), rules, first, second,
            experimentalTriggerEffects: SummonProcedureFamily.Triggers, experimentalContinuousEffects: SummonProcedureFamily.ContinuousEffects,
            experimentalSpecialSummonCards: [.. SummonProcedureFamily.SpecialSummonCards, new SpecialSummonCardProfile(DetachXyz, SpecialSummonSources.None, DetachProcedure)],
            experimentalSpecialSummonProcedures: SummonProcedureFamily.Procedures,
            playerOneExtraDeck: extra,
            experimentalMaterialProcedures: [.. SummonProcedureFamily.MaterialProcedures,
                new ExtraDeckMaterialProcedureProfile(DetachProcedure, [DetachXyz], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(4), anyMaterial: true)],
            experimentalSummonEffects: SummonProcedureFamily.SummonEffects, experimentalPendulumCards: SummonProcedureFamily.PendulumCards,
            effectDefinitions: [.. SummonProcedureFamily.Definitions, .. Extras]);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId player) => e.State.GetPlayer(player);
    private static CardInstance Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id);
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id).InstanceId, zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id).InstanceId, zone));
    private static MonsterBinding Bind(DuelEngine e, int zone)
    {
        var m = P(e, Turn(e)).MonsterZones[zone]!;
        return new(Turn(e), zone, m.Card, m.PlacementId);
    }

    private static int[] Graveyard(DuelEngine e, params uint[] ids) =>
        ids.Select(id => P(e, Turn(e)).Graveyard.First(c => c.GameCardId == id).InstanceId).ToArray();

    private static SpecialSummonByProcedure ChaosSummon(DuelEngine e, int zone, params int[] cost) =>
        new(Turn(e), e.State.Revision, "chaos-sorcerer", Hand(e, Chaos).InstanceId, zone, BattlePosition.FaceUpAttack, cost);

    /// <summary>Player One mills a LIGHT and DARK monster, then properly Summons Chaos Sorcerer to zone 0.</summary>
    private static DuelEngine ChaosOnField(uint[]? second = null)
    {
        var e = Engine(Deck(Mill, Chaos, Reborn, L3, L3, W, DarkOne, L3), second ?? Deck());
        EnterMain(e); Activate(e, Mill); Resolve(e);
        Ok(e, ChaosSummon(e, 0, Graveyard(e, W, DarkOne)));
        return e;
    }

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    private static PendingEffectSummon RequestSummon(DuelEngine e)
    {
        while (e.State.PendingChain is { SummonDecision: null } chain)
            Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId));
        return e.State.PendingChain!.SummonDecision!;
    }

    // Hand procedures

    [Fact]
    public void ConditionalProcedureNeedsItsConditionsAndKeepsTheNormalSummon()
    {
        var e = Engine(Deck(W), Deck(Cyber, W));
        EnterMain(e); Next(e);
        Reject(e, new SpecialSummonByProcedure(PlayerId.Two, e.State.Revision, "cyber-dragon", Hand(e, Cyber).InstanceId, 0), DuelError.SpecialSummonConditionsNotMet);
        Next(e); Summon(e, W, 0); Next(e);
        Ok(e, new SpecialSummonByProcedure(PlayerId.Two, e.State.Revision, "cyber-dragon", Hand(e, Cyber).InstanceId, 0));
        Assert.NotNull(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.False(e.State.NormalSummonUsed);
    }

    [Fact]
    public void BanishCostMustMatchEachRequirementExactly()
    {
        var e = Engine(Deck(Mill, Chaos, L3, L3, L3, W, W, DarkOne), Deck());
        EnterMain(e); Activate(e, Mill); Resolve(e);
        int[] lights = P(e, PlayerId.One).Graveyard.Where(c => c.GameCardId == W).Select(c => c.InstanceId).ToArray();
        int dark = Graveyard(e, DarkOne)[0];
        Reject(e, ChaosSummon(e, 0), DuelError.InvalidSummonMaterials);
        Reject(e, ChaosSummon(e, 0, lights[0], lights[1]), DuelError.InvalidSummonMaterials);
        Reject(e, ChaosSummon(e, 0, dark, dark), DuelError.InvalidSummonMaterials);
        Ok(e, ChaosSummon(e, 0, dark, lights[0]));
        Assert.Equal(2, P(e, PlayerId.One).Banished.Count);
        Assert.NotNull(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void NegatedProcedureLeavesItsCostPaid()
    {
        var e = Engine(Deck(Mill, Chaos, L3, L3, L3, W, DarkOne, L3), Deck(Negator));
        EnterMain(e); Activate(e, Mill); Resolve(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Negator).InstanceId, 0)); Next(e);
        Ok(e, ChaosSummon(e, 0, Graveyard(e, W, DarkOne)));
        Ok(e, new ActivateSummonNegation(PlayerId.Two, e.State.PendingSummon!.DecisionId, 0));
        Resolve(e);
        Assert.Equal(2, P(e, PlayerId.One).Banished.Count);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Chaos);
    }

    [Fact]
    public void MonstersNotProperlySummonedCannotBeRevived()
    {
        var e = Engine(Deck(Mill, Reborn, L3, L3, L3, Chaos, Frost, BlackLuster), Deck());
        EnterMain(e); Activate(e, Mill); Resolve(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Reborn).InstanceId, 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ProperlySummonedMonsterCanBeRevived()
    {
        var e = ChaosOnField(Deck(Hole));
        Next(e); Activate(e, Hole); Resolve(e); Next(e);
        Activate(e, Reborn);
        var decision = e.State.PendingDecision!;
        var revive = decision.FieldTargetOptions!.First(o => o.Card.GameCardId == Chaos);
        Ok(e, new ChooseDecisionFieldTarget(decision.Player, decision.DecisionId, revive.Player, revive.Location, revive.Zone, revive.Card.InstanceId));
        while (e.State.PendingChain is { DefinitionSummon: null } chain)
            Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId));
        var pending = e.State.PendingChain!.DefinitionSummon!;
        Ok(e, new ChooseDefinitionSummon(pending.Player, pending.DecisionId, pending.Options[0].Card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Resolve(e);
        Assert.Equal(Chaos, P(e, PlayerId.One).MonsterZones[1]!.Card.GameCardId);
    }

    [Fact]
    public void AnEffectThatStopsAttackingAppliesOnlyOnceActivatedAndNotAfterAttacking()
    {
        var e = ChaosOnField(Deck(W, W));
        Next(e); Summon(e, W, 0); Next(e);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Target(e, PlayerId.Two, FieldLocation.MonsterZone, 0);
        Resolve(e);
        Assert.Contains(P(e, PlayerId.Two).Banished, c => c.GameCardId == W);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
        Phase(e, DuelPhase.Battle);
        Reject(e, new DeclareAttack(PlayerId.One, 0), DuelError.AttackLocked);

        Next(e); Summon(e, W, 0); Next(e); Next(e); Summon(e, W, 1); Next(e);
        Phase(e, DuelPhase.Battle);
        Ok(e, new DeclareAttack(PlayerId.One, 0, 0));
        Phase(e, DuelPhase.Main2);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.ActivationConditionNotMet);
    }

    // Xyz and Synchro

    private static DuelEngine TwoMonsters(uint first, uint second, uint extra)
    {
        var e = Engine(Deck(first, second, Wayne == extra ? W : L3), Deck(), [extra]);
        EnterMain(e); Summon(e, first, 0); Next(e); Next(e); Summon(e, second, 1);
        return e;
    }

    private static SpecialSummonWithMaterials MaterialSummon(DuelEngine e, string procedure, int zone = 0) =>
        new(Turn(e), e.State.Revision, procedure, P(e, Turn(e)).ExtraDeck[0].InstanceId, zone, [Bind(e, 0), Bind(e, 1)]);

    [Fact]
    public void XyzSummonAcceptsAnyMonstersOfTheRightLevel()
    {
        var e = TwoMonsters(W, DarkOne, Pearl);
        Ok(e, MaterialSummon(e, "xyz-rank-4"));
        var pearl = P(e, PlayerId.One).MonsterZones[0]!;
        Assert.Equal(Pearl, pearl.Card.GameCardId);
        Assert.Equal(2, pearl.XyzMaterials.Count);
    }

    [Fact]
    public void XyzSummonRejectsTheWrongLevel()
    {
        var e = TwoMonsters(W, L3, Pearl);
        Reject(e, MaterialSummon(e, "xyz-rank-4"), DuelError.InvalidSummonMaterials);
    }

    [Fact]
    public void DetachCostIsChosenWithTheActivationAndStaysPaid()
    {
        var e = TwoMonsters(W, DarkOne, DetachXyz);
        Ok(e, MaterialSummon(e, DetachProcedure));
        var materials = P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials;
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.InvalidDecisionChoice);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0, [materials[0].InstanceId, materials[1].InstanceId]), DuelError.InvalidDecisionChoice);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0, [materials[0].InstanceId]));
        Assert.Single(P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials);
        Assert.Contains(materials[0], P(e, PlayerId.One).Graveyard);
        Resolve(e);
        Assert.Equal(6000, P(e, PlayerId.Two).LifePoints);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0, [materials[1].InstanceId]), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void SynchroSummonNeedsOneTunerAndTheExactLevels()
    {
        var noTuner = TwoMonsters(L2, L3, Wayne);
        Reject(noTuner, MaterialSummon(noTuner, SummonProcedureFamily.SynchroProcedureId), DuelError.InvalidSummonMaterials);
        var wrong = TwoMonsters(TunerTwo, W, Wayne);
        Reject(wrong, MaterialSummon(wrong, SummonProcedureFamily.SynchroProcedureId), DuelError.InvalidSummonMaterials);
        var e = TwoMonsters(TunerTwo, L3, Wayne);
        Ok(e, MaterialSummon(e, SummonProcedureFamily.SynchroProcedureId));
        Assert.Equal(Wayne, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Assert.Equal(2, P(e, PlayerId.One).Graveyard.Count);
    }

    [Fact]
    public void TriggerNamedForASynchroSummonFiresOnlyForThatProcedure()
    {
        var e = TwoMonsters(TunerTwo, L3, Wayne);
        Ok(e, MaterialSummon(e, SummonProcedureFamily.SynchroProcedureId));
        Assert.NotNull(e.State.EventEffects.PendingTriggers);
        EventEffectScenario.Drain(e);
        Assert.Equal(W, P(e, PlayerId.One).MonsterZones.First(m => m is not null && m.Card.GameCardId != Wayne)!.Card.GameCardId);
    }

    // Fusion and Ritual

    [Fact]
    public void FusionAcceptsAnyArtworkOfANamedMaterialButNotTwoInOneSlot()
    {
        var e = Engine(Deck(Poly, GaiaA, GaiaB, Curse), Deck(), [GaiaFusion]);
        EnterMain(e); Activate(e, Poly);
        var pending = RequestSummon(e);
        int Id(uint card) => P(e, PlayerId.One).Hand.First(c => c.GameCardId == card).InstanceId;
        Reject(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, pending.Targets[0].InstanceId, [Id(GaiaA), Id(GaiaB)], 0), DuelError.InvalidSummonMaterials);
        Ok(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, pending.Targets[0].InstanceId, [Id(GaiaB), Id(Curse)], 0));
        Assert.Equal(GaiaFusion, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void RitualAcceptsLevelsOverTheTotalOnlyWithoutASpareTribute()
    {
        var e = Engine(Deck(LusterRitual, BlackLuster, W, W, W), Deck());
        EnterMain(e); Activate(e, LusterRitual);
        var pending = RequestSummon(e);
        int[] tributes = P(e, PlayerId.One).Hand.Where(c => c.GameCardId == W).Select(c => c.InstanceId).ToArray();
        int target = pending.Targets[0].InstanceId;
        Reject(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, target, [tributes[0]], 0), DuelError.InvalidSummonMaterials);
        Reject(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, target, tributes, 0), DuelError.InvalidSummonMaterials);
        Ok(e, new ChooseEffectSummon(PlayerId.One, pending.DecisionId, target, tributes.Take(2), 0));
        Assert.Equal(BlackLuster, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
    }

    [Fact]
    public void SetFusionSpellCanBeActivated()
    {
        var e = Engine(Deck(Poly, GaiaA, Curse), Deck(), [GaiaFusion]);
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Poly).InstanceId, 0));
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Assert.NotNull(RequestSummon(e));
    }

    [Fact]
    public void SpellNegationAlsoStopsFusionSpells()
    {
        var e = Engine(Deck(SpellNegator, Poly, GaiaA, Curse), Deck(), [GaiaFusion]);
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, SpellNegator).InstanceId, 0)); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Resolve(e);
        Activate(e, Poly);
        var events = Resolve(e);
        Assert.Contains(events, ev => ev is ChainLinkSkipped { Reason: ChainSkipReason.SourceEffectsNegated });
        Assert.All(P(e, PlayerId.One).MonsterZones, Assert.Null);
    }

    // Continuous effect filter

    [Fact]
    public void MonsterAuraCanBeLimitedByType()
    {
        var e = Engine(Deck(Comrade, L3), Deck());
        EnterMain(e); Summon(e, Comrade, 0); Next(e); Next(e); Summon(e, L3, 1);
        Assert.Equal(1400, e.GetMonsterStatistics(PlayerId.One, 0)!.Attack);
        Assert.Equal(1000, e.GetMonsterStatistics(PlayerId.One, 1)!.Attack);
    }

    // Adapter and validation

    [Fact]
    public void AdapterOffersProceduresAndEffectSummonChoices()
    {
        var e = Engine(Deck(Poly, GaiaA, Curse, W), Deck(), [GaiaFusion, Pearl]);
        var source = new DuelActionSource(e, Catalog());
        EnterMain(e); Summon(e, W, 0); Next(e); Next(e); Summon(e, W, 1);
        Assert.Contains(source.Observe(PlayerId.One).Actions, a => a.Kind == ActionKind.Summon && a.Label.StartsWith("Special Summon"));
        Activate(e, Poly);
        RequestSummon(e);
        var observation = source.Observe(PlayerId.One);
        Assert.Equal(ActionAvailability.Ready, observation.Availability);
        Assert.Contains(observation.Actions, a => a.Kind == ActionKind.ChooseCard);
    }

    [Fact]
    public void ProfilesValidateTheirNewOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetachCost(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectDefinition("x", Mill, EffectKind.NormalSpell, [new DealDamageStep(1)], cost: new DetachCost(1)));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [1u], [], 2, anyMaterial: true));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("x", [1u], [2u], 2, xyz: new XyzMaterialRequirements(4), destination: MaterialDestination.Attached, anyMaterial: true));
        Assert.Throws<ArgumentException>(() => FusionSummonRecipe.WithAlternatives(1, [2u]));
        Assert.Throws<ArgumentException>(() => RitualSummonRecipe.AnyTributes(1, 0));
    }
}
