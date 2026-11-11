using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;
using S = TagForceSpecial.Tests.BattleFlowScenario;

namespace TagForceSpecial.Tests;

/// <summary>Locks that hold only while a condition is true, exceptions for what is being Summoned, and locks bound to one monster.</summary>
public sealed class LockRefinementTests
{
    private const uint W = 1061, Pyro = 1062, Filler = 1063, Wipe = 1064, LightBig = 1065, DarkBig = 1066, DrawSeal = 1067, Lockee = 1068, Xyz = 1069, OpenXyz = 1070,
        Guardian = 8274, SolarFlare = 5974, Fires = 7454, MindControl = 4817, TerminalWorld = 9353, RecklessGreed = 5448;
    private const string Rank4 = "test-rank-4";

    private static DuelEngine Engine(uint[] first, uint[] second, uint[]? extra = null)
    {
        var families = new[] { TemplateCardFamily.Family, TokenCardFamily.Family };
        static CardDefinition Monster(uint id, string name, int attack, uint level, uint type, uint attribute) =>
            new(id, name, "", new(attack, 1000, level), new(attribute, type, 1, 1, 0, 0, 0));
        CardCatalog catalog = new(new(null, "synthetic-lock-refinements"),
            families.SelectMany(f => f.Cards).Select(c => Monster(c.CardId, c.Name, 1000, 4, c.CardId == SolarFlare ? MonsterTypes.Pyro : MonsterTypes.Warrior, MonsterAttributes.Light))
                .Concat(TokenCardFamily.TokenIds.Select(id => Monster(id, id == 3940 ? "Doomsday Token" : "Fluff Token", 0, 1, MonsterTypes.Fiend, MonsterAttributes.Dark)))
                .Append(Monster(W, "Warrior", 1500, 4, MonsterTypes.Warrior, MonsterAttributes.Light))
                .Append(Monster(Pyro, "Pyro", 1200, 4, MonsterTypes.Pyro, MonsterAttributes.Fire))
                .Append(Monster(LightBig, "Light Big", 2400, 5, MonsterTypes.Warrior, MonsterAttributes.Light))
                .Append(Monster(DarkBig, "Dark Big", 2400, 5, MonsterTypes.Fiend, MonsterAttributes.Dark))
                .Append(Monster(Lockee, "No Xyz", 1000, 4, MonsterTypes.Warrior, MonsterAttributes.Light))
                .Append(Monster(Xyz, "Xyz", 2000, 4, MonsterTypes.Warrior, MonsterAttributes.Light))
                .Append(Monster(OpenXyz, "Open Xyz", 2000, 4, MonsterTypes.Warrior, MonsterAttributes.Light))
                .Concat(new[] { Filler, Wipe, DrawSeal }.Select(id => new CardDefinition(id, $"Spell {id}", "", new(0, 0, 1)))));
        var rules = families.SelectMany(f => f.Rules).ToDictionary();
        rules[W] = rules[Pyro] = rules[LightBig] = rules[DarkBig] = rules[Lockee] =
            new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsTributeSummon: true, AllowsSummonMaterial: true);
        rules[Xyz] = rules[OpenXyz] = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true);
        rules[Filler] = rules[Wipe] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        rules[DrawSeal] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second, experimentalTriggerEffects: families.SelectMany(f => f.Triggers), experimentalBattleFlow: true,
            experimentalSpecialSummonCards: [new SpecialSummonCardProfile(Xyz, SpecialSummonSources.None, Rank4), new SpecialSummonCardProfile(OpenXyz, SpecialSummonSources.None, Rank4)],
            playerOneExtraDeck: extra ?? [],
            experimentalMaterialProcedures: [new ExtraDeckMaterialProcedureProfile(Rank4, [Xyz, OpenXyz], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(4), anyMaterial: true)],
            effectDefinitions: [.. families.SelectMany(f => f.Definitions),
                new("test-filler", Filler, EffectKind.NormalSpell, [new GainLifePointsStep(1)]),
                new("test-wipe", Wipe, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("test-draw-seal", DrawSeal, EffectKind.NormalTrap, [new ApplyRuleLockStep(new RuleLock(LockedAction.Draw, TargetSide.Opponent))])],
            shuffleSeed: 13,
            ruleLocks: [.. families.SelectMany(f => f.RuleLocks), new RuleLockProfile("test-no-xyz", Lockee, new RuleLock(LockedAction.BeXyzMaterial, Scope: LockScope.Self,
                ExceptSummoning: new CardIdFilter([OpenXyz])))],
            battleAbilities: TemplateCardFamily.Family.BattleAbilities, destructionReplacements: TemplateCardFamily.Family.DestructionReplacements,
            graveyardRedirects: TemplateCardFamily.Family.GraveyardRedirects, damageRules: TemplateCardFamily.Family.DamageRules);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, PlayerId p, uint id) => P(e, p).Hand.First(c => c.GameCardId == id).InstanceId;
    private static MonsterBinding At(DuelEngine e, PlayerId p, int zone) =>
        new(p, zone, P(e, p).MonsterZones[zone]!.Card, P(e, p).MonsterZones[zone]!.PlacementId);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHandEmptyLockHoldsOnlyWhileTheHandIsEmpty(bool emptyHand)
    {
        var e = Engine(Deck(Guardian, Filler, Filler, Filler, emptyHand ? Filler : W), Deck(Wipe));
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Guardian), 0));
        for (int z = 0; z < (emptyHand ? 4 : 3); z++) Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, Filler), z));
        Assert.Equal(emptyHand ? 0 : 1, P(e, PlayerId.One).Hand.Count);
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, PlayerId.Two, Wipe), 0)); Drive(e);
        Assert.Equal(emptyHand, P(e, PlayerId.One).MonsterZones[0] is not null);
    }

    [Fact]
    public void ALockNeedingAnotherMatchingMonsterAppliesOnlyWithOneAndTheEndPhaseBurnIsOwnTurnOnly()
    {
        var e = Engine(Deck(SolarFlare, Pyro), Deck(W));
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, SolarFlare), 0));
        Phase(e, DuelPhase.End); Drive(e);
        Assert.Equal(7500, P(e, PlayerId.Two).LifePoints);
        Ok(e, new EndTurn(PlayerId.One)); EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0));
        Phase(e, DuelPhase.End); Drive(e);
        Assert.Equal(7500, P(e, PlayerId.Two).LifePoints);
        Ok(e, new EndTurn(PlayerId.Two)); EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Pyro), 1));
        Phase(e, DuelPhase.End); Drive(e); Ok(e, new EndTurn(PlayerId.One)); EnterMain(e);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Assert.False(e.Execute(new DeclareAttack(PlayerId.Two, 0, 0)).Succeeded);
        Ok(e, new DeclareAttack(PlayerId.Two, 0, 1));
    }

    [Fact]
    public void TokensThatCannotBeTributedForASummonStillCanForTheExceptedMonsters()
    {
        var e = Engine(Deck(Fires, LightBig, DarkBig), Deck());
        EnterMain(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Fires), 0)); Drive(e);
        Next(e); Next(e);
        var token = At(e, PlayerId.One, 0);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, LightBig), 0, [token]), DuelError.TributeNotAllowed);
        Ok(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, DarkBig), 0, [token]));
    }

    [Fact]
    public void AMaterialLockAllowsOnlyTheExceptedXyzMonster()
    {
        var e = Engine(Deck(Lockee, W), Deck(), extra: [Xyz, OpenXyz]);
        EnterMain(e); Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Lockee), 0)); Next(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, W), 1));
        MonsterBinding[] materials = [At(e, PlayerId.One, 0), At(e, PlayerId.One, 1)];
        int Extra(uint id) => P(e, PlayerId.One).ExtraDeck.First(c => c.GameCardId == id).InstanceId;
        Reject(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, Rank4, Extra(Xyz), 0, materials, BattlePosition.FaceUpAttack), DuelError.SummonMaterialNotAllowed);
        Ok(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, Rank4, Extra(OpenXyz), 0, materials, BattlePosition.FaceUpAttack));
    }

    [Fact]
    public void ATakenMonsterKeepsItsLocksAfterChangingSides()
    {
        var e = Engine(Deck(MindControl, LightBig), Deck(W));
        EnterMain(e); Next(e);
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, W), 0)); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, MindControl), 0)); Drive(e);
        int zone = P(e, PlayerId.One).MonsterZones.ToList().FindIndex(m => m is not null);
        Reject(e, new TributeSummon(PlayerId.One, e.State.Revision, Hand(e, PlayerId.One, LightBig), 1, [At(e, PlayerId.One, zone)]), DuelError.TributeNotAllowed);
        Phase(e, DuelPhase.Battle); S.Ready(e);
        Assert.False(e.Execute(new DeclareAttack(PlayerId.One, zone)).Succeeded);
    }

    [Fact]
    public void AMainPhaseOneCardSkipsMainPhaseTwoWhileOnTheField()
    {
        var e = Engine(Deck(TerminalWorld), Deck());
        EnterMain(e); Next(e); Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, TerminalWorld), 0)); Drive(e);
        Phase(e, DuelPhase.Battle); S.Ready(e); S.Advance(e); S.Close(e);
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.Main2), DuelError.PhaseSkipped);
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));

        var late = Engine(Deck(TerminalWorld), Deck());
        EnterMain(late); Next(late); Next(late);
        Phase(late, DuelPhase.Battle); S.Ready(late); S.Advance(late); S.Close(late);
        Ok(late, new ChangePhase(PlayerId.One, DuelPhase.Main2));
        Reject(late, new ActivateSpell(PlayerId.One, Hand(late, PlayerId.One, TerminalWorld), 0), DuelError.ActivationConditionNotMet);
    }

    [Fact]
    public void ADrawStoppedInResponseAlsoStopsTheSkip()
    {
        var e = Engine(Deck(RecklessGreed), Deck(DrawSeal));
        EnterMain(e); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, RecklessGreed), 0)); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, DrawSeal), 0)); Next(e);
        int deck = P(e, PlayerId.One).Deck.Count;
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        var chain = e.State.PendingChain!;
        Ok(e, new ActivateChainResponse(PlayerId.Two, chain.DecisionId, 0));
        Drive(e);
        Assert.Equal(deck, P(e, PlayerId.One).Deck.Count);
        Next(e); Next(e);
        Assert.Equal(deck - 1, P(e, PlayerId.One).Deck.Count);
    }
}
