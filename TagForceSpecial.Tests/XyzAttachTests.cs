using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Effects that attach cards to Xyz Monsters as Xyz Material.</summary>
public sealed class XyzAttachTests
{
    private const uint X = 951, Ark = 952, W = 953, Kill = 954, Hole = 955, TokenMaker = 956, Tok = 957, Regen = 10182, Reborn = 9950;
    private const string Rank4 = "xyz-rank-4-attach";

    private static readonly EffectDefinition ArkAttach = new("ark-attach", Ark, EffectKind.MonsterIgnition, [new AttachTargetsToSelfStep()],
        fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Opponent)]);

    private static DuelEngine Engine(uint[] first, uint[] second, uint[] extra)
    {
        uint[] probes = [Kill, Hole, TokenMaker];
        CardCatalog catalog = new(new(null, "synthetic-xyz-attach"),
            TemplateCardFamily.Family.Cards.Select(c => new CardDefinition(c.CardId, c.Name, "", new(0, 0, 1)))
                .Concat(new uint[] { X, Ark, W, Tok }.Select(id => new CardDefinition(id, $"Monster {id}", "", new(1500, 1000, id == W ? 4u : 0u), new(1, 15, 1, 1, 0, 0, 0))))
                .Concat(probes.Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));
        var rules = TemplateCardFamily.Family.Rules.ToDictionary();
        rules[W] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true);
        rules[X] = rules[Ark] = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true);
        rules[Tok] = new(CardCategory.Monster, AllowsBasicBattle: true, IsToken: true);
        rules[Kill] = new(CardCategory.Trap, AllowsSpellTrapSet: true);
        rules[Hole] = rules[TokenMaker] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second,
            experimentalSpecialSummonCards: [new SpecialSummonCardProfile(X, SpecialSummonSources.Graveyard, Rank4), new SpecialSummonCardProfile(Ark, SpecialSummonSources.None, Rank4)],
            playerOneExtraDeck: extra,
            experimentalMaterialProcedures: [new ExtraDeckMaterialProcedureProfile(Rank4, [X, Ark], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(4), anyMaterial: true)],
            effectDefinitions: [.. TemplateCardFamily.Family.Definitions, ArkAttach,
                new("kill", Kill, EffectKind.NormalTrap, [new DestroyTargetsStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster)]),
                new("hole", Hole, EffectKind.NormalSpell, [new DestroyAllStep(FieldCardKind.Monster)]),
                new("token-maker", TokenMaker, EffectKind.NormalSpell, [new SummonTokensStep([Tok], BattlePosition.FaceUpAttack)])],
            shuffleSeed: 9);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));

    private static void Choose(DuelEngine e, Func<FieldCardTarget, bool> pick)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(pick);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId));
    }

    /// <summary>Resolves the whole chain, answering any revival placement with the first free zone.</summary>
    private static List<DuelEvent> ResolveAll(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { } chain && e.State.Outcome is null)
        {
            if (chain.DefinitionSummon is { } summon)
                events.AddRange(Ok(e, new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[0].Card.InstanceId, summon.MonsterZoneOptions[0], summon.PositionOptions[0])).Events);
            else
                events.AddRange(Ok(e, chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId)).Events);
        }
        return events;
    }

    /// <summary>Player One controls two Level 4 monsters on turn three, then Xyz Summons <paramref name="xyz"/> to zone 0.</summary>
    private static void XyzSummon(DuelEngine e, uint xyz)
    {
        var player = P(e, PlayerId.One);
        MonsterBinding Bind(int zone) => new(PlayerId.One, zone, player.MonsterZones[zone]!.Card, player.MonsterZones[zone]!.PlacementId);
        Ok(e, new SpecialSummonWithMaterials(PlayerId.One, e.State.Revision, Rank4, player.ExtraDeck.First(c => c.GameCardId == xyz).InstanceId, 0, [Bind(0), Bind(1)]));
    }

    [Fact]
    public void ASpellAttachedAsMaterialNeverReachesTheGraveyard()
    {
        var e = Engine(Deck(W, W, Regen), Deck(), [X]);
        EnterMain(e); Summon(e, W, 0); Next(e); Next(e);
        Summon(e, W, 1); XyzSummon(e, X);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Regen), 4));
        Choose(e, t => t.Card.GameCardId == X);
        var events = ResolveAll(e);
        var host = P(e, PlayerId.One).MonsterZones[0]!;
        Assert.Equal(3, host.XyzMaterials.Count);
        Assert.Contains(host.XyzMaterials, c => c.GameCardId == Regen);
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[4]);
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Regen);
        Assert.Contains(events, ev => ev is CardAttachedAsMaterial { Card.GameCardId: Regen });
    }

    [Fact]
    public void NothingIsAttachedWhenTheXyzMonsterLeavesInResponse()
    {
        var e = Engine(Deck(W, W, Regen), Deck(Kill), [X]);
        EnterMain(e); Summon(e, W, 0); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, Kill), 0)); Next(e);
        Summon(e, W, 1); XyzSummon(e, X);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, Regen), 4));
        Choose(e, t => t.Card.GameCardId == X);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Choose(e, t => t.Card.GameCardId == X);
        ResolveAll(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == Regen);
        Assert.Equal(2, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId == W));
    }

    [Fact]
    public void ARevivalCanAttachItsSourceToTheRevivedMonster()
    {
        var e = Engine(Deck(W, W, Reborn), Deck(Hole), [X]);
        EnterMain(e); Summon(e, W, 0); Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, Reborn), 0)); Next(e); Next(e);
        Summon(e, W, 1); XyzSummon(e, X); Next(e);
        Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, Hole), 4)); ResolveAll(e); Next(e);
        Assert.Contains(P(e, PlayerId.One).Graveyard, c => c.GameCardId == X);
        Ok(e, new ActivateSetSpell(PlayerId.One, 0));
        Choose(e, t => t.Card.GameCardId == X);
        ResolveAll(e);
        var revived = P(e, PlayerId.One).MonsterZones.Single(m => m is not null)!;
        Assert.Equal(X, revived.Card.GameCardId);
        Assert.Equal([Reborn], revived.XyzMaterials.Select(c => c.GameCardId));
        Assert.Null(P(e, PlayerId.One).SpellTrapFields[0]);
    }

    [Fact]
    public void AMonsterEffectAttachesAnOpposingMonsterButNeverAToken()
    {
        var e = Engine(Deck(W, W), Deck(W, TokenMaker), [Ark]);
        EnterMain(e); Summon(e, W, 0); Next(e);
        Summon(e, W, 0); Ok(e, new ActivateSpell(PlayerId.Two, Hand(e, TokenMaker), 4)); ResolveAll(e); Next(e);
        Summon(e, W, 1); XyzSummon(e, Ark);
        Ok(e, new ActivateMonsterEffect(PlayerId.One, 0));
        Assert.DoesNotContain(e.State.PendingDecision!.FieldTargetOptions!, t => t.Card.GameCardId == Tok);
        Choose(e, t => t.Player == PlayerId.Two && t.Card.GameCardId == W);
        ResolveAll(e);
        Assert.Null(P(e, PlayerId.Two).MonsterZones[0]);
        Assert.DoesNotContain(P(e, PlayerId.Two).Graveyard, c => c.GameCardId == W);
        Assert.Equal(3, P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
    }

    [Fact]
    public void AttachStepsNeedTheirCardKindsAndTargets()
    {
        FieldTarget xyz = new(FieldCardKind.Monster, TargetSide.Either, FieldCardFilter.FaceUp, cardFilter: new XyzMonsterFilter());
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.ContinuousSpell, [new AttachSourceStep()], fieldTargets: [xyz]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.MonsterIgnition, [new AttachSourceStep()], fieldTargets: [xyz]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new AttachTargetsToSelfStep()], fieldTargets: [xyz]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.MonsterIgnition, [new AttachTargetsToSelfStep()],
            fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap)]));
    }
}
