using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Monsters with their own stat rules: the Egyptian Gods, the Qliphort monsters and cards that change Levels or scales.</summary>
public sealed class SpecialMonsterTests
{
    private const uint W = 1081, Strong = 1082, Zapper = 1083, Hole = 1084, Gear = 1085, Bust = 1086, Marshalling = 11299, ArgentChaosForce = 11022, Chevalier = 11330, Kepler = 11442, Covenant = 11446, Slifer = 4999, Ra = 5000, Carrier = 11354, Helix = 11355;
    private const string NoTributes = "template-11354-no-tributes-v1";
    private static readonly Dictionary<uint, (CardStatistics Stats, CardNumericCodes Codes)> Printed = new()
    {
        [Carrier] = (new(2400, 1000, 6), new(5, MonsterTypes.Machine, 26, 26, 0, 1, 1)),
        [Helix] = (new(2400, 1000, 6), new(5, MonsterTypes.Machine, 26, 26, 0, 9, 9)),
        [Chevalier] = (new(2000, 700, 5), new(MonsterAttributes.Dark, 3, 26, 26, 0, 6, 6)),
        [Kepler] = (new(0, 0, 1), new(MonsterAttributes.Light, 18, 26, 26, 0, 10, 10))
    };
    private const string SliferProcedure = "egyptian-god-4999-three-tributes-v1", RaProcedure = "egyptian-god-5000-three-tributes-v1";

    private static IEnumerable<CardFamily> Families => [TemplateCardFamily.Family, EgyptianGodFamily.Family, ApoqliphortFamily.Family];

    private static DuelEngine Engine(uint[] first, uint[] second)
    {
        var gods = new HashSet<uint>([Slifer, Ra, 3861, 3859]);
        CardCatalog catalog = new(new(null, "synthetic-special-monsters"),
            Families.SelectMany(f => f.Cards).Select(c => Printed.TryGetValue(c.CardId, out var printed) ? new CardDefinition(c.CardId, c.Name, "", printed.Stats, printed.Codes)
                    : new CardDefinition(c.CardId, c.Name, "",
                    gods.Contains(c.CardId) ? new(null, null, 10) : c.CardId == 4998 ? new(4000, 4000, 10) : new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Zapper, "Zapper", "", new(1000, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Hole, "Hole", "", new(0, 0, 1)))
                .Append(new CardDefinition(Bust, "Bust", "", new(0, 0, 1)))
                .Append(new CardDefinition(Covenant, "Covenant with the Hellgate", "", new(0, 0, 1)))
                .Append(new CardDefinition(ArgentChaosForce, "Rank-Up-Magic Argent Chaos Force", "", new(0, 0, 1)))
                .Append(new CardDefinition(Gear, "Gear", "", new(2000, 1000, 5), new(1, MonsterTypes.Machine, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(W, "Warrior", "", new(1500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0)))
                .Append(new CardDefinition(Strong, "Strong", "", new(2500, 1000, 4), new(1, MonsterTypes.Warrior, 1, 1, 0, 0, 0))));
        var rules = Families.SelectMany(f => f.Rules).ToDictionary();
        rules[W] = rules[Strong] = rules[Zapper] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true);
        rules[Hole] = rules[Bust] = rules[ArgentChaosForce] = rules[Covenant] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[Gear] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeSummon: true, AllowsTributeForSummon: true);
        EffectDefinition bust = new("test-bust", Bust, EffectKind.NormalSpell, [new DestroyTargetsStep()], fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Opponent)]);
        var opponentMonster = new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent);
        EffectDefinition zap = new("test-zap", Zapper, EffectKind.MonsterIgnition, [new DestroyTargetsStep()], fieldTargets: [opponentMonster]);
        EffectDefinition hole = new("test-hole", Hole, EffectKind.NormalSpell, [new DestroyTargetsStep()], fieldTargets: [opponentMonster]);
        return new(catalog, rules, first, second, experimentalTriggerEffects: Families.SelectMany(f => f.Triggers), experimentalBattleFlow: true,
            effectDefinitions: [.. Families.SelectMany(f => f.Definitions), zap, hole, bust], shuffleSeed: 13, ruleLocks: Families.SelectMany(f => f.RuleLocks),
            battleAbilities: Families.SelectMany(f => f.BattleAbilities), destructionReplacements: Families.SelectMany(f => f.DestructionReplacements),
            graveyardRedirects: Families.SelectMany(f => f.GraveyardRedirects), damageRules: Families.SelectMany(f => f.DamageRules),
            experimentalTributeProcedures: Families.SelectMany(f => f.TributeProcedures), monsterStats: Families.SelectMany(f => f.MonsterStats),
            experimentalPendulumCards: Families.SelectMany(f => f.PendulumCards), experimentalSpecialSummonCards: Families.SelectMany(f => f.SpecialSummonCards));
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;
    private static int? Attack(DuelEngine e, PlayerId p, int zone) => e.GetMonsterStatistics(p, zone)?.Attack;

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
                : chain.DefinitionSummon is { } summon ? new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[0].Card.InstanceId, summon.MonsterZoneOptions[0], BattlePosition.FaceUpAttack)
                : chain.DefinitionCardChoice is { } choice ? new ChooseDefinitionCard(choice.Player, choice.DecisionId, choice.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) break;
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Player One puts three Warriors on the field over turns 1, 3 and 5 and Tribute Summons <paramref name="god"/> on turn 5.</summary>
    private static (DuelEngine Engine, List<DuelEvent> Events) SummonGod(uint god, string procedure, uint[]? second = null)
    {
        var e = Engine(Deck(god, W, W, W), second ?? Deck());
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0)); Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 1)); Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 2)); Next(e); Next(e);
        MonsterBinding[] tributes = [.. Enumerable.Range(0, 3).Select(z => new MonsterBinding(PlayerId.One, z, P(e, PlayerId.One).MonsterZones[z]!.Card, P(e, PlayerId.One).MonsterZones[z]!.PlacementId))];
        var events = Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, god), 0, tributes, procedureId: procedure)).Events.ToList();
        events.AddRange(Drive(e));
        return (e, events);
    }

    [Fact]
    public void SliferGainsATKPerCardInHandAndWeakensMonstersTheOpponentSummonsInAttackPosition()
    {
        var (e, _) = SummonGod(Slifer, SliferProcedure, Deck(W, Strong));
        Assert.Equal(1000 * P(e, PlayerId.One).Hand.Count, Attack(e, PlayerId.One, 0));
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Drive(e);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Next(e); Next(e);
        Ok(e, new SetMonster(PlayerId.Two, Hand(e, PlayerId.Two, Strong), 2));
        Assert.Null(e.State.EventEffects.PendingTriggers);
    }

    [Fact]
    public void SliferMakesAStrongMonsterWeakerForGood()
    {
        var (e, _) = SummonGod(Slifer, SliferProcedure, Deck(Strong));
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Strong), 0)); Drive(e);
        Assert.Equal(500, Attack(e, PlayerId.Two, 0));
        Next(e); Next(e);
        Assert.Equal(500, Attack(e, PlayerId.Two, 0));
    }

    private static MonsterBinding Bind(DuelEngine e, PlayerId p, int zone) => new(p, zone, P(e, p).MonsterZones[zone]!.Card, P(e, p).MonsterZones[zone]!.PlacementId);

    [Fact]
    public void AQliphortSummonedWithoutTributingHasLowerATKAndItsTributedEffectTriggers()
    {
        var e = Engine(Deck(Carrier, Helix), Deck());
        EnterMain(e);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Carrier), 0, [], procedureId: NoTributes));
        Assert.Equal(1800, Attack(e, PlayerId.One, 0));
        Assert.Equal(4u, e.GetMonsterLevel(PlayerId.One, 0));
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Helix), 1, [], faceDown: true, procedureId: "template-11355-no-tributes-v1"),
            DuelError.UnsupportedTributeProcedure);
        Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Helix), 0, [Bind(e, PlayerId.One, 0)]));
        var order = e.State.EventEffects.PendingTriggers!;
        Ok(e, new ChooseTriggerOrder(order.Player, order.DecisionId, order.Options.Select(o => o.Id).ToArray()));
        var commitment = e.State.EventEffects.PendingCommitment!;
        var theirs = commitment.ZoneOptions.Single(o => o.Player == PlayerId.Two);
        Ok(e, new ChooseTriggerZoneTarget(commitment.Player, commitment.DecisionId, theirs.Player, theirs.Card.InstanceId));
        Drive(e);
        Assert.Equal(2400, Attack(e, PlayerId.One, 0));
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.Two).Hand, c => c.GameCardId == W);
    }

    [Fact]
    public void ATributeSummonedQliphortIgnoresEffectsOfLowerLevelMonstersButNotSpells()
    {
        var e = Engine(Deck(Carrier), Deck(Zapper, Hole));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0)); Next(e); Next(e);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Carrier), 0, [Bind(e, PlayerId.One, 0)])); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Zapper), 0));
        Ok(e, new ActivateMonsterEffect(PlayerId.Two, 0)); Drive(e);
        Assert.Equal(Carrier, P(e, PlayerId.One).MonsterZones[0]!.Card.GameCardId);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Hole), 0)); Drive(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
    }

    [Fact]
    public void ADeclaredLevelChangesMachinesAndTheTrapSearchesWhenItLeavesTheField()
    {
        var e = Engine(Deck(Marshalling, W, Gear, W, W, W, ArgentChaosForce), Deck(Bust));
        EnterMain(e);
        Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Marshalling), 0));
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0)); Next(e); Next(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0)); Drive(e);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, Gear), 0, [Bind(e, PlayerId.One, 0)]));
        Assert.Equal(5u, e.GetMonsterLevel(PlayerId.One, 0));
        Ok(e, new ActivateCardEffect(PlayerId.One, 0, 1));
        var ask = e.State.PendingChain!.Declaration!;
        Assert.Equal([5, 6, 7, 8, 9], ask.Options);
        Ok(e, new DeclareValue(PlayerId.One, ask.DecisionId, 7)); Drive(e);
        Assert.Equal(7u, e.GetMonsterLevel(PlayerId.One, 0));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Bust), 0)); Drive(e);
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == ArgentChaosForce);
    }

    private static void PlaceScale(DuelEngine e, PlayerId p, uint card, int zone)
    {
        Ok(e, new ActivatePendulumScale(p, Hand(e, p, card), zone));
        Drive(e);
    }

    [Fact]
    public void AScaleNeedsAPartnerInTheOtherZoneToKeepItsPrintedValue()
    {
        var e = Engine(Deck(Chevalier, Kepler), Deck());
        EnterMain(e);
        PlaceScale(e, PlayerId.One, Chevalier, 0);
        Assert.Equal(5u, e.GetPendulumScale(PlayerId.One, 0));
        PlaceScale(e, PlayerId.One, Kepler, 1);
        Assert.Equal(6u, e.GetPendulumScale(PlayerId.One, 0));
        Assert.Equal(10u, e.GetPendulumScale(PlayerId.One, 1));
    }

    [Fact]
    public void EachOwnStandbyPhaseLowersTheScaleAndDestroysHigherLevelMonsters()
    {
        var e = Engine(Deck(Kepler), Deck());
        EnterMain(e);
        PlaceScale(e, PlayerId.One, Kepler, 1);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 0));
        int[] expected = [8, 6, 4, 2];
        foreach (int scale in expected)
        {
            Next(e); Phase(e, DuelPhase.End);
            while (P(e, PlayerId.Two).Hand.Count > 6) Ok(e, new DiscardForHandLimit(PlayerId.Two, P(e, PlayerId.Two).Hand[^1].InstanceId));
            Ok(e, new EndTurn(PlayerId.Two));
            Ok(e, new ResolveDrawStep(PlayerId.One)); Drive(e);
            Assert.Equal((uint)scale, e.GetPendulumScale(PlayerId.One, 1));
            Phase(e, DuelPhase.Main1);
        }
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == W);
    }

    [Fact]
    public void TwoSummonEffectsSharingOneUseResolveOnlyOnce()
    {
        var e = Engine(Deck(Kepler, Chevalier, W, W, W, Covenant), Deck());
        EnterMain(e);
        PlaceScale(e, PlayerId.One, Chevalier, 0);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Kepler), 0));
        var order = e.State.EventEffects.PendingTriggers!;
        Assert.Equal(2, order.Options.Count);
        var events = Ok(e, new ChooseTriggerOrder(order.Player, order.DecisionId, order.Options.OrderByDescending(o => o.Profile.EffectId).Select(o => o.Id).ToArray())).Events.ToList();
        events.AddRange(Drive(e));
        Assert.Single(events.OfType<TriggerActivated>());
        Assert.Contains(P(e, PlayerId.One).Hand, c => c.GameCardId == Covenant);
        Assert.NotNull(P(e, PlayerId.One).PendulumZones[0]);
    }

    [Fact]
    public void RaPaysLifePointsDownToOneHundredToGainThemAndItsOwnEffectStillTriggers()
    {
        var (e, events) = SummonGod(Ra, RaProcedure);
        Assert.Contains(events, ev => ev is TriggerActivated);
        Assert.Equal(100, P(e, PlayerId.One).LifePoints);
        Assert.Equal(7900, Attack(e, PlayerId.One, 0));
        Assert.Equal(7900, e.GetMonsterStatistics(PlayerId.One, 0)!.Defense);
    }
}
