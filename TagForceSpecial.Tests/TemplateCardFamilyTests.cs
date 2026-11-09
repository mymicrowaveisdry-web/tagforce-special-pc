using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>The shared templates and table builder, through representative cards.</summary>
public sealed class TemplateCardFamilyTests
{
    private const uint WarriorA = 901, FiendA = 902, DarkLow = 903, DarkHigh = 904, HighWarrior = 905, Sword = 4307, Salamandra = 4660, Plasma = 4937, WarriorWipe = 4659,
        Reinforcement = 5328, Returning = 5330, Eruption = 7453, Storm = 910, Curse = 5510, Javelin = 4992, Probe = 950;

    private static readonly AuthoredCard ProbeIgnition = new CardTable("probe").Ignition(Probe, "Probe", [new DealDamageStep(100)], oncePerTurn: true);

    private static CardNumericCodes Codes(uint type, uint attribute) => new(attribute, type, 1, 1, 0, 0, 0);

    private static CardCatalog Catalog() => new(new(null, "synthetic-template"),
        TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(0, 0, 1), Codes(0, 0))).Concat(
        [
            new(WarriorA, "Warrior", "", new(1000, 1000, 4), Codes(MonsterTypes.Warrior, MonsterAttributes.Earth)),
            new(FiendA, "Fiend", "", new(1200, 1000, 4), Codes(MonsterTypes.Fiend, MonsterAttributes.Dark)),
            new(DarkLow, "Dark Low", "", new(1400, 1000, 4), Codes(MonsterTypes.Zombie, MonsterAttributes.Dark)),
            new(DarkHigh, "Dark High", "", new(1600, 1000, 4), Codes(MonsterTypes.Zombie, MonsterAttributes.Dark)),
            new(HighWarrior, "High Warrior", "", new(2000, 1000, 5), Codes(MonsterTypes.Warrior, MonsterAttributes.Earth)),
            new(Storm, "Storm", "", new(0, 0, 1), Codes(0, 0)),
            new(Probe, "Probe", "", new(1000, 1000, 4), Codes(MonsterTypes.Machine, MonsterAttributes.Earth))
        ]));

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        foreach (uint id in new[] { WarriorA, FiendA, DarkLow, DarkHigh, HighWarrior })
            rules[id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Storm] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Probe] = ProbeIgnition.Rules;
        return new(Catalog(), rules, first, second, experimentalTriggerEffects: TemplateCardFamily.Family.Triggers,
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, .. ProbeIgnition.Definitions, new("storm", Storm, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)])], shuffleSeed: 3);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(WarriorA, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id, int zone = 4) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), zone));
    private static int? Atk(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)!.Attack;
    private static int? Def(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)!.Defense;

    private static void Target(DuelEngine e, PlayerId owner, FieldLocation location, int zone)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Player == owner && o.Location == location && o.Zone == zone);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, owner, location, zone, t.Card.InstanceId));
    }

    [Fact]
    public void RestrictedEquipOnlyGoesOnItsTypeAndAddsItsBonus()
    {
        var e = Engine(Deck(Sword, FiendA), Deck());
        EnterMain(e); Summon(e, FiendA, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Sword), 4), DuelError.DecisionUnavailable);
        Next(e); Next(e);
        Summon(e, WarriorA, 1);
        Activate(e, Sword);
        Assert.Single(e.State.PendingDecision!.FieldTargetOptions!);
        Target(e, PlayerId.One, FieldLocation.MonsterZone, 1);
        Resolve(e);
        Assert.Equal(1300, Atk(e, PlayerId.One, 1));
        Assert.Equal(1300, Def(e, PlayerId.One, 1));
    }

    [Fact]
    public void AttributeEquipChecksTheAttribute()
    {
        var e = Engine(Deck(Salamandra), Deck());
        EnterMain(e); Summon(e, WarriorA, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Salamandra), 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void AttributeFieldSpellChangesOnlyThatAttributeOnBothSides()
    {
        var e = Engine(Deck(Plasma, FiendA), Deck(DarkLow));
        EnterMain(e); Summon(e, FiendA, 0);
        Activate(e, Plasma, DuelPlayerState.FieldZoneIndex); Resolve(e);
        Next(e); Summon(e, DarkLow, 0);
        Assert.Equal(1700, Atk(e, PlayerId.One, 0));
        Assert.Equal(600, Def(e, PlayerId.One, 0));
        Assert.Equal(1900, Atk(e, PlayerId.Two, 0));
    }

    [Fact]
    public void TypeWipeNeedsAMatchingMonsterAndLeavesOthers()
    {
        var e = Engine(Deck(WarriorWipe, FiendA), Deck());
        EnterMain(e); Summon(e, FiendA, 0);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, WarriorWipe), 4), DuelError.DecisionUnavailable);
        Next(e); Summon(e, WarriorA, 0); Next(e);
        Activate(e, WarriorWipe);
        Resolve(e);
        Assert.NotNull(e.State.GetPlayer(PlayerId.One).MonsterZones[0]);
        Assert.Null(e.State.GetPlayer(PlayerId.Two).MonsterZones[0]);
    }

    [Fact]
    public void LevelAndTypeSearchOffersOnlyMatchingCards()
    {
        var e = Engine([Reinforcement, FiendA, FiendA, FiendA, FiendA, HighWarrior, WarriorA, DarkLow, .. Enumerable.Repeat(FiendA, 32)], Deck());
        EnterMain(e);
        Activate(e, Reinforcement);
        for (int i = 0; i < 2; i++) Ok(e, new PassChainResponse(e.State.PendingChain!.Responder, e.State.PendingChain.DecisionId));
        Ok(e, new ResolveNextChainLink(PlayerId.One, e.State.PendingChain!.DecisionId));
        var choice = e.State.PendingChain!.DefinitionCardChoice!;
        Assert.Equal([WarriorA], choice.Options.Select(c => c.GameCardId).Distinct());
    }

    [Fact]
    public void GraveyardRecoveryUsesTheFilter()
    {
        var e = Engine(Deck(Returning, Eruption, WarriorA, DarkHigh, DarkLow), Deck(Storm, Storm));
        EnterMain(e); Summon(e, WarriorA, 0); Next(e);
        Activate(e, Storm); Resolve(e); Next(e);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Eruption), 3), DuelError.DecisionUnavailable);
        int before = e.State.GetPlayer(PlayerId.One).Hand.Count(c => c.GameCardId == WarriorA);
        Activate(e, Returning);
        Target(e, PlayerId.One, FieldLocation.Graveyard, -1);
        Resolve(e);
        Assert.Equal(before + 1, e.State.GetPlayer(PlayerId.One).Hand.Count(c => c.GameCardId == WarriorA));
        Assert.DoesNotContain(e.State.GetPlayer(PlayerId.One).Graveyard, c => c.GameCardId == WarriorA);
    }

    [Fact]
    public void WhenSummonedTriggersOnANormalSummon()
    {
        var e = Engine(Deck(Curse), Deck());
        EnterMain(e); Summon(e, Curse, 0);
        EventEffectScenario.Drain(e);
        Assert.Equal(7500, e.State.GetPlayer(PlayerId.Two).LifePoints);
    }

    [Fact]
    public void IgnitionBuilderRespectsOncePerTurn()
    {
        var e = Engine(Deck(Probe), Deck());
        EnterMain(e); Summon(e, Probe, 0);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Resolve(e);
        Assert.Equal(7900, e.State.GetPlayer(PlayerId.Two).LifePoints);
        Reject(e, new ActivateMonsterEffect(PlayerId.One, 0), DuelError.EffectAlreadyUsed);
    }

    [Fact]
    public void AttackTrapUsesTheAttackerAutomatically()
    {
        var e = Engine(Deck(Javelin), Deck(FiendA));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Javelin), 0)); Next(e);
        Summon(e, FiendA, 0);
        Phase(e, DuelPhase.Battle); BattleFlowScenario.Ready(e);
        Ok(e, new DeclareAttack(PlayerId.Two, 0));
        BattleFlowScenario.Respond(e, 0);
        Resolve(e);
        Assert.Equal(9200, e.State.GetPlayer(PlayerId.One).LifePoints);
    }

    [Fact]
    public void FamilyKeepsTriggerDefinitionsInsideTheirTriggers()
    {
        var family = TemplateCardFamily.Family;
        Assert.DoesNotContain(family.Definitions, d => d.Kind == EffectKind.MonsterTrigger);
        Assert.Equal(3, family.Triggers.Count(t => t.CardId == Curse));
        Assert.Equal(3, family.Reviewed[Curse].Effects.References.Count);
    }

    [Fact]
    public void ArchetypeListsHoldMonstersOnly()
    {
        var spellsAndTraps = TemplateCardFamily.Family.Cards.Select(c => c.CardId).ToHashSet();
        foreach (var list in new[] { CardArchetypes.ElementalHero, CardArchetypes.Resonator, CardArchetypes.Hieratic, CardArchetypes.IceBarrier, CardArchetypes.Nordic })
        {
            Assert.NotEmpty(list);
            Assert.Empty(list.Intersect(spellsAndTraps));
        }
    }

    [Fact]
    public void ManifestTextIsGeneratedFromTheEffect()
    {
        var card = TemplateCardFamily.Family.Cards.Single(c => c.CardId == WarriorWipe);
        Assert.Contains("DestroyAll", card.Operation);
        Assert.Contains("MonsterTypeFilter", card.Operation);
        Assert.Equal("none", card.Costs);
        var recover = TemplateCardFamily.Family.Cards.Single(c => c.CardId == 7630);
        Assert.Contains("PayLifePointsCost", recover.Costs);
        Assert.Contains("Banished", recover.Targets);
    }
}
