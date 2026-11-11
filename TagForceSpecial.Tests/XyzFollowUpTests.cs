using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>Rank-Up Summons, overlay Xyz procedures, attaching from the hand or Graveyard, and detaching all materials.</summary>
public sealed class XyzFollowUpTests
{
    private const uint X = 961, Up = 962, X2 = 963, W = 964, Kill = 965, RankUp = 966, Barian = 967, Strip = 968, Boost = 969, Grave = 970;
    private const string Rank4 = "xyz-follow-4", Rank5 = "xyz-follow-5", Overlay = "xyz-follow-overlay";
    private static readonly FieldTarget OwnXyz = new(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp, cardFilter: new XyzMonsterFilter());

    private static DuelEngine Engine(uint[] first, uint[] second, uint[] firstExtra, uint[]? secondExtra = null)
    {
        uint[] spells = [RankUp, Barian, Strip, Boost, Grave];
        CardCatalog catalog = new(new(null, "synthetic-xyz-follow-up"),
            new[] { (X, 4u), (Up, 5u), (X2, 4u), (W, 4u) }.Select(m => new CardDefinition(m.Item1, $"Monster {m.Item1}", "", new(1500, 1000, m.Item2), new(1, 15, 1, 1, 0, 0, 0)))
                .Concat(spells.Append(Kill).Select(id => new CardDefinition(id, $"Probe {id}", "", new(0, 0, 1)))));
        var rules = new Dictionary<uint, DuelCardRules>
        {
            [W] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true, AllowsTributeForSummon: true, AllowsSummonMaterial: true),
            [Kill] = new(CardCategory.Trap, AllowsSpellTrapSet: true)
        };
        foreach (uint id in new[] { X, Up, X2 }) rules[id] = new(CardCategory.Monster, AllowsBasicBattle: true, AllowsExtraDeck: true);
        foreach (uint id in spells) rules[id] = new(CardCategory.Spell, AllowsSpellTrapSet: true);
        return new(catalog, rules, first, second,
            experimentalSpecialSummonCards: [new SpecialSummonCardProfile(X, SpecialSummonSources.Graveyard, Rank4),
                new SpecialSummonCardProfile(X2, SpecialSummonSources.None, Rank4), new SpecialSummonCardProfile(Up, SpecialSummonSources.None, Rank5)],
            playerOneExtraDeck: firstExtra, playerTwoExtraDeck: secondExtra,
            experimentalMaterialProcedures: [
                new ExtraDeckMaterialProcedureProfile(Rank4, [X, X2], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(4), anyMaterial: true),
                new ExtraDeckMaterialProcedureProfile(Rank5, [Up], [], 2, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(5), anyMaterial: true),
                new ExtraDeckMaterialProcedureProfile(Overlay, [Up], [], 1, MaterialDestination.Attached, xyz: new XyzMaterialRequirements(5),
                    overlayFrom: new XyzMonsterFilter(4, 4), properProcedureId: Rank5)],
            effectDefinitions: [
                new("rank-up", RankUp, EffectKind.NormalSpell, [new RankUpStep(new CardIdFilter([Up]))], fieldTargets: [OwnXyz]),
                new("barian", Barian, EffectKind.NormalSpell, [new RankUpStep(new CardIdFilter([Up])), new TakeOpponentMaterialStep()], fieldTargets: [OwnXyz]),
                new("strip", Strip, EffectKind.NormalSpell, [new DetachAllStep(), new AttachSourceStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own,
                    FieldCardFilter.FaceUp, cardFilter: new XyzMonsterFilter()) { MinXyzMaterials = 1 }]),
                new("boost", Boost, EffectKind.NormalSpell, [new AttachFromStep(new AnyMonsterFilter())], fieldTargets: [OwnXyz]),
                new("grave", Grave, EffectKind.NormalSpell, [new AttachFromStep(new AnyMonsterFilter(), Hand: false, Graveyard: true)], fieldTargets: [OwnXyz]),
                new("kill", Kill, EffectKind.NormalTrap, [new DestroyTargetsStep()], fieldTargets: [new FieldTarget(FieldCardKind.Monster)])],
            shuffleSeed: 12);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(W, 40 - top.Length)];
    private static PlayerId Turn(DuelEngine e) => e.State.ActivePlayer;
    private static DuelPlayerState P(DuelEngine e, PlayerId p) => e.State.GetPlayer(p);
    private static int Hand(DuelEngine e, uint id) => P(e, Turn(e)).Hand.First(c => c.GameCardId == id).InstanceId;
    private static void Summon(DuelEngine e, uint id, int zone) => Ok(e, new NormalSummon(Turn(e), Hand(e, id), zone));
    private static void Activate(DuelEngine e, uint id) => Ok(e, new ActivateSpell(Turn(e), Hand(e, id), 4));

    private static MonsterBinding Bind(DuelEngine e, int zone)
    {
        var m = P(e, Turn(e)).MonsterZones[zone]!;
        return new(Turn(e), zone, m.Card, m.PlacementId);
    }

    private static SpecialSummonWithMaterials MaterialSummon(DuelEngine e, string procedure, uint card, params int[] zones) =>
        new(Turn(e), e.State.Revision, procedure, P(e, Turn(e)).ExtraDeck.First(c => c.GameCardId == card).InstanceId, zones[0], zones.Select(z => Bind(e, z)));

    private static void Choose(DuelEngine e, uint id)
    {
        var p = e.State.PendingDecision!;
        var t = p.FieldTargetOptions!.First(o => o.Card.GameCardId == id);
        Ok(e, new ChooseDecisionFieldTarget(p.Player, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId));
    }

    /// <summary>Resolves the whole chain, taking the first option of any resolution-time choice.</summary>
    private static List<DuelEvent> ResolveAll(DuelEngine e)
    {
        List<DuelEvent> events = [];
        while (e.State.PendingChain is { } chain && e.State.Outcome is null)
        {
            DuelCommand command = chain.DefinitionSummon is { } summon
                ? new ChooseDefinitionSummon(summon.Player, summon.DecisionId, summon.Options[0].Card.InstanceId, summon.MonsterZoneOptions[0], summon.PositionOptions[0])
                : chain.DefinitionCardChoice is { } card ? new ChooseDefinitionCard(card.Player, card.DecisionId, card.Options[0].InstanceId)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId) : new ResolveNextChainLink(Turn(e), chain.DecisionId);
            events.AddRange(Ok(e, command).Events);
        }
        return events;
    }

    /// <summary>Turn three: Player One Xyz Summons X to zone 0 from two Level 4 monsters.</summary>
    private static DuelEngine WithX(uint[] first, uint[] extra, uint[]? second = null, Action<DuelEngine>? opponentTurn = null)
    {
        var e = Engine(first, second ?? Deck(), extra);
        EnterMain(e); Summon(e, W, 0); Next(e);
        opponentTurn?.Invoke(e); Next(e);
        Summon(e, W, 1);
        Ok(e, MaterialSummon(e, Rank4, X, 0, 1));
        return e;
    }

    [Fact]
    public void ARankUpSummonsTheNextRankAndCarriesEveryMaterial()
    {
        var e = WithX(Deck(W, W, RankUp), [X, Up]);
        Activate(e, RankUp); Choose(e, X);
        var events = ResolveAll(e);
        var up = P(e, PlayerId.One).MonsterZones[0]!;
        Assert.Equal(Up, up.Card.GameCardId);
        Assert.Equal([X, W, W], up.XyzMaterials.Select(c => c.GameCardId));
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId is W or X);
        Assert.Contains(events, ev => ev is MonsterSpecialSummoned { ProperProcedureId: Rank5 });
    }

    [Fact]
    public void ARankUpCannotTargetAMonsterWithNothingToBecome()
    {
        var e = WithX(Deck(W, W, RankUp), [X]);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, RankUp), 4), DuelError.DecisionUnavailable);
    }

    [Fact]
    public void ARankUpDoesNothingIfItsTargetLeftInResponse()
    {
        var e = WithX(Deck(W, W, RankUp), [X, Up], Deck(Kill), o => Ok(o, new SetSpellTrap(PlayerId.Two, Hand(o, Kill), 0)));
        Activate(e, RankUp); Choose(e, X);
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        Choose(e, X);
        ResolveAll(e);
        Assert.Null(P(e, PlayerId.One).MonsterZones[0]);
        Assert.Contains(P(e, PlayerId.One).ExtraDeck, c => c.GameCardId == Up);
    }

    [Fact]
    public void AnOverlayProcedureUsesOneXyzMonsterAndKeepsItsMaterials()
    {
        var e = Engine(Deck(W, W), Deck(), [X, Up]);
        EnterMain(e); Summon(e, W, 0); Next(e); Next(e);
        Summon(e, W, 1);
        Reject(e, MaterialSummon(e, Overlay, Up, 0), DuelError.InvalidSummonMaterials);
        Ok(e, MaterialSummon(e, Rank4, X, 0, 1));
        Ok(e, MaterialSummon(e, Overlay, Up, 0));
        Assert.Equal([X, W, W], P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials.Select(c => c.GameCardId));
        Assert.DoesNotContain(P(e, PlayerId.One).Graveyard, c => c.GameCardId is W or X);
    }

    [Fact]
    public void ARankUpCanTakeAMaterialFromTheOpponent()
    {
        var e = Engine(Deck(W, W, Barian), Deck(W, W), [X, Up], [X2]);
        EnterMain(e); Summon(e, W, 0); Next(e);
        Summon(e, W, 0); Next(e);
        Summon(e, W, 1); Ok(e, MaterialSummon(e, Rank4, X, 0, 1)); Next(e);
        Summon(e, W, 1); Ok(e, MaterialSummon(e, Rank4, X2, 0, 1)); Next(e);
        Activate(e, Barian); Choose(e, X);
        ResolveAll(e);
        Assert.Equal(4, P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
        Assert.Single(P(e, PlayerId.Two).MonsterZones[0]!.XyzMaterials);
    }

    [Fact]
    public void DetachAllSendsMaterialsToTheGraveyardAndThenAttachesTheSource()
    {
        var e = WithX(Deck(W, W, Strip), [X]);
        Activate(e, Strip); Choose(e, X);
        ResolveAll(e);
        Assert.Equal([Strip], P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials.Select(c => c.GameCardId));
        Assert.Equal(2, P(e, PlayerId.One).Graveyard.Count(c => c.GameCardId == W));
    }

    [Fact]
    public void CardsCanBeAttachedFromTheHandButOnlyIfThereIsOne()
    {
        var e = WithX(Deck(W, W, Boost, Grave, W), [X]);
        Reject(e, new ActivateSpell(PlayerId.One, Hand(e, Grave), 4), DuelError.DecisionUnavailable);
        int hand = P(e, PlayerId.One).Hand.Count(c => c.GameCardId == W);
        Activate(e, Boost); Choose(e, X);
        ResolveAll(e);
        Assert.Equal(3, P(e, PlayerId.One).MonsterZones[0]!.XyzMaterials.Count);
        Assert.Equal(hand - 1, P(e, PlayerId.One).Hand.Count(c => c.GameCardId == W));
    }

    [Fact]
    public void FollowUpStepsNeedTheirCardKindsAndTargets()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.MonsterIgnition, [new RankUpStep(new AnyMonsterFilter())], fieldTargets: [OwnXyz]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new TakeOpponentMaterialStep()]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new AttachSourceStep(1)], fieldTargets: [OwnXyz]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("bad", 1, EffectKind.NormalSpell, [new AttachFromStep(new AnyMonsterFilter(), ToTarget: false)]));
        Assert.Throws<ArgumentException>(() => new ExtraDeckMaterialProcedureProfile("bad", [Up], [], 1, MaterialDestination.Attached, xyz: new(5),
            overlayFrom: new XyzMonsterFilter(4, 4)));
    }
}
