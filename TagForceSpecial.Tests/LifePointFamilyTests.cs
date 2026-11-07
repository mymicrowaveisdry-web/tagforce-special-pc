using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.NormalSpellTests;

namespace TagForceSpecial.Tests;

/// <summary>
/// Life-point family wiring through the production definitions, with hand-authored expected LP changes.
/// </summary>
public sealed class LifePointFamilyTests
{
    private const uint Filler = 4012, Krebons = 7716, Remedy = 4346, Pure = 4347, Flame = 4351, Raimei = 4841,
        Fire = 4353, Mercy = 5151, Thief = 5985, Restructer = 4871, Desserts = 4865, Elf = 4986, Barrel = 5620,
        Bomb = 6148, Dynamite = 6283;

    private static CardCatalog Catalog() => new(new(null, "synthetic-life-point-family"),
        LifePointFamily.Cards.Select(c => c.CardId).Concat([Filler, Krebons, ChainCardFamily.EmergencyTeleport, ChainCardFamily.MagicJammer])
            .Select(id => new CardDefinition(id, $"Probe {id}", "", new(1000, 1000, id == Krebons ? 2u : id == Filler ? 4u : 0u))));

    private static DuelEngine Engine(IEnumerable<uint> first, IEnumerable<uint> second)
    {
        var rules = LifePointFamily.Rules.ToDictionary();
        rules[Filler] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[Krebons] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        rules[ChainCardFamily.EmergencyTeleport] = ChainCardFamily.Rules[ChainCardFamily.EmergencyTeleport];
        rules[ChainCardFamily.MagicJammer] = ChainCardFamily.Rules[ChainCardFamily.MagicJammer];
        return new(Catalog(), rules, first, second, effectDefinitions: [.. LifePointFamily.Definitions,
            .. ChainCardFamily.Definitions.Where(d => d.CardId is ChainCardFamily.EmergencyTeleport or ChainCardFamily.MagicJammer)],
            shuffleSeed: 5);
    }

    private static uint[] Deck(params uint[] top) => [.. top, .. Enumerable.Repeat(Filler, 40 - top.Length)];
    private static CardInstance Hand(DuelEngine e, PlayerId p, uint id) => e.State.GetPlayer(p).Hand.First(c => c.GameCardId == id);
    private static int Lp(DuelEngine e, PlayerId p) => e.State.GetPlayer(p).LifePoints;

    private static List<DuelEvent> Activate(DuelEngine e, PlayerId p, uint id, int zone = 0)
    {
        Ok(e, new ActivateSpell(p, Hand(e, p, id).InstanceId, zone));
        return Resolve(e);
    }

    /// <summary>(card, activating player's LP change, opponent's LP change), authored from the card texts.</summary>
    public static IEnumerable<object[]> FixedSpells() => new object[][]
    {
        [Remedy, 600, 0], [Pure, 800, 0], [Flame, 0, -600], [Raimei, 0, -300],
        [Fire, -500, -1000], [Mercy, 1000, 1000], [Thief, 500, -500]
    };

    [Theory]
    [MemberData(nameof(FixedSpells))]
    public void EachFixedSpellChangesTheAuthoredLifePointsFromHandAndSet(uint id, int own, int opponent)
    {
        foreach (bool set in new[] { false, true })
        {
            var e = Engine(Deck(id), Deck());
            EnterMain(e);
            if (set) Ok(e, new SetSpellTrap(PlayerId.One, Hand(e, PlayerId.One, id).InstanceId, 2));
            var result = Ok(e, set ? new ActivateSetSpell(PlayerId.One, 2) : new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, id).InstanceId, 2));
            Assert.Equal(ChainSpellSpeed.One, e.State.PendingChain!.Links[0].SpellSpeed);
            Assert.IsType<SpellActivated>(result.Events[0]);
            var events = Resolve(e);
            Assert.Equal(8000 + own, Lp(e, PlayerId.One));
            Assert.Equal(8000 + opponent, Lp(e, PlayerId.Two));
            Assert.Contains(events, ev => ev is SpellSentToGraveyard s && s.Card.GameCardId == id);
            Assert.Null(e.State.Outcome);
        }
    }

    [Fact]
    public void TableBackedAmountsAgreeWithTheSignedTableConvention()
    {
        // Positive table values recover the activating player's LP; negative ones damage the opponent.
        foreach (var (id, amount) in LifePointFamily.TableAmounts)
        {
            var step = Assert.Single(LifePointFamily.Cards.Single(c => c.CardId == id).Definition.Steps);
            if (amount > 0) Assert.Equal(new GainLifePointsStep(amount), step);
            else Assert.Equal(new DealDamageStep(-amount), step);
        }
    }

    [Fact]
    public void BothPlayersEffectsOrderTheActivatingPlayerFirstAndTheThiefDamagesBeforeItGains()
    {
        var mercy = Activate(Engine(Deck(Mercy), Deck()), PlayerId.One, Mercy).OfType<LifePointsGained>().ToArray();
        Assert.Equal([PlayerId.One, PlayerId.Two], mercy.Select(g => g.Player));
        var thief = Activate(Engine(Deck(Thief), Deck()), PlayerId.One, Thief);
        Assert.True(thief.FindIndex(ev => ev is EffectDamageDealt) < thief.FindIndex(ev => ev is LifePointsGained));
        var fire = Activate(Engine(Deck(Fire), Deck()), PlayerId.One, Fire).OfType<EffectDamageDealt>().ToArray();
        Assert.Equal([PlayerId.Two, PlayerId.One], fire.Select(d => d.Player));
        Assert.Equal([1000, 500], fire.Select(d => d.Damage));
    }

    [Fact]
    public void TremendousFireTakingBothPlayersToZeroIsADraw()
    {
        var e = Engine(Enumerable.Repeat(Fire, 40), Enumerable.Repeat(Fire, 40));
        EnterMain(e);
        for (int i = 0; i < 5; i++) Activate(e, PlayerId.One, Fire, i);
        Assert.Equal((5500, 3000), (Lp(e, PlayerId.One), Lp(e, PlayerId.Two)));
        Next(e);
        for (int i = 0; i < 5; i++) Activate(e, PlayerId.Two, Fire, i);
        Assert.Equal((500, 500), (Lp(e, PlayerId.One), Lp(e, PlayerId.Two)));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Fire).InstanceId, 0));
        var events = Resolve(e);
        var outcome = e.State.Outcome!;
        Assert.True(outcome.IsDraw);
        Assert.Null(outcome.Loser);
        Assert.Equal(DuelEndReason.BothLifePointsDepleted, outcome.Reason);
        Assert.IsType<DuelEnded>(events[^1]); // terminal at once: no link cleanup
        Reject(e, new ChangePhase(PlayerId.One, DuelPhase.End), DuelError.DuelFinished);
    }

    [Fact]
    public void JustDessertsCountsOpponentMonstersAndNeedsOneToActivate()
    {
        var e = Engine(Deck(), Deck(Desserts));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, Desserts).InstanceId, 0));
        Next(e);
        Assert.Null(e.State.OpenWindow); // Player One controls no monster yet: no window opened
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 0));
        Ok(e, new ChangePhase(PlayerId.One, DuelPhase.End));
        var window = e.State.OpenWindow!;
        Ok(e, new ActivateChainResponse(PlayerId.Two, window.DecisionId, 0));
        Resolve(e);
        Assert.Equal(7500, Lp(e, PlayerId.One));
        Assert.Equal(ChainSpellSpeed.Two, Assert.Single(LifePointFamily.Definitions, d => d.CardId == Desserts).SpellSpeed);
    }

    [Fact]
    public void CountedTrapsReadTheirZonesAtResolution()
    {
        // Player Two sets the four Traps on turn 2; Player One then builds the counted zones.
        var e = Engine(Deck(Flame, Filler, Filler, Filler, Filler), Deck(Elf, Barrel, Bomb, Desserts));
        EnterMain(e);
        Ok(e, new NormalSummon(PlayerId.One, Hand(e, PlayerId.One, Filler).InstanceId, 0));
        Activate(e, PlayerId.One, Flame); // one card in Player One's Graveyard
        Next(e);
        foreach (var (id, zone) in new[] { (Elf, 0), (Barrel, 1), (Bomb, 2), (Desserts, 3) })
            Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, id).InstanceId, zone));
        Ok(e, new NormalSummon(PlayerId.Two, Hand(e, PlayerId.Two, Filler).InstanceId, 0));
        Next(e); Next(e);
        int lp = Lp(e, PlayerId.Two);
        Ok(e, new ActivateSetSpell(PlayerId.Two, 0)); Resolve(e);
        Assert.Equal(lp + 600, Lp(e, PlayerId.Two)); // 2 monsters on the field × 300
        int hand = e.State.GetPlayer(PlayerId.One).Hand.Count;
        int before = Lp(e, PlayerId.One);
        Ok(e, new ActivateSetSpell(PlayerId.Two, 1)); Resolve(e);
        Assert.Equal(before - 200 * (hand + 1), Lp(e, PlayerId.One)); // hand + 1 monster; no set cards
        before = Lp(e, PlayerId.One);
        Ok(e, new ActivateSetSpell(PlayerId.Two, 2)); Resolve(e);
        Assert.Equal(before - 100, Lp(e, PlayerId.One)); // Final Flame is Player One's only Graveyard card
    }

    [Fact]
    public void RestructerRevolutionAndDDDynamiteCountHandAndBanishedCards()
    {
        var e = Engine(Deck(Restructer), Deck());
        EnterMain(e);
        int hand = e.State.GetPlayer(PlayerId.Two).Hand.Count;
        Activate(e, PlayerId.One, Restructer);
        Assert.Equal(8000 - 200 * hand, Lp(e, PlayerId.Two));

        var d = Engine(Deck(ChainCardFamily.EmergencyTeleport, Krebons), Deck(Dynamite));
        EnterMain(d); Next(d);
        Ok(d, new SetSpellTrap(PlayerId.Two, Hand(d, PlayerId.Two, Dynamite).InstanceId, 0));
        Reject(d, new ActivateSetSpell(PlayerId.Two, 0), DuelError.SetThisTurn);
        Next(d);
        Ok(d, new ActivateSpell(PlayerId.One, Hand(d, PlayerId.One, ChainCardFamily.EmergencyTeleport).InstanceId, 0));
        for (int i = 0; i < 2; i++) Ok(d, new PassChainResponse(d.State.PendingChain!.Responder, d.State.PendingChain.DecisionId));
        Ok(d, new ResolveNextChainLink(PlayerId.One, d.State.PendingChain!.DecisionId));
        var choice = d.State.PendingChain!.DefinitionSummon!;
        Ok(d, new ChooseDefinitionSummon(PlayerId.One, choice.DecisionId, choice.Options[0].Card.InstanceId, 1, BattlePosition.FaceUpAttack));
        Next(d); // the End Phase banishes Krebons; it is now Player Two's turn
        Assert.Single(d.State.GetPlayer(PlayerId.One).Banished);
        Ok(d, new ActivateSetSpell(PlayerId.Two, 0)); Resolve(d);
        Assert.Equal(7700, Lp(d, PlayerId.One));
    }

    [Fact]
    public void NothingToCountRejectsActivationAtomically()
    {
        var e = Engine(Deck(), Deck(Bomb, Dynamite, Desserts));
        EnterMain(e); Next(e);
        for (int zone = 0; zone < 3; zone++)
            Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, new[] { Bomb, Dynamite, Desserts }[zone]).InstanceId, zone));
        Next(e); Next(e);
        foreach (int zone in new[] { 0, 1, 2 }) Reject(e, new ActivateSetSpell(PlayerId.Two, zone), DuelError.ActivationConditionNotMet);
    }

    [Fact]
    public void MagicJammerNegatesALifePointSpellWithoutAnyLifePointChange()
    {
        var e = Engine(Deck(Fire), Deck(ChainCardFamily.MagicJammer));
        EnterMain(e); Next(e);
        Ok(e, new SetSpellTrap(PlayerId.Two, Hand(e, PlayerId.Two, ChainCardFamily.MagicJammer).InstanceId, 0));
        Next(e);
        Ok(e, new ActivateSpell(PlayerId.One, Hand(e, PlayerId.One, Fire).InstanceId, 0));
        Ok(e, new ActivateChainResponse(PlayerId.Two, e.State.PendingChain!.DecisionId, 0));
        var cost = e.State.PendingDecision!;
        Ok(e, new ChooseDecisionCard(PlayerId.Two, cost.DecisionId, cost.CardOptions[0].InstanceId));
        var events = Resolve(e);
        Assert.Empty(events.OfType<EffectDamageDealt>());
        Assert.Equal((8000, 8000), (Lp(e, PlayerId.One), Lp(e, PlayerId.Two)));
    }

    [Fact]
    public void StepsAndBatchMetadataValidate()
    {
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DamageBothPlayersStep(0, 1)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new GainLifePointsStep(1, (EffectPlayers)9)]));
        Assert.Throws<ArgumentException>(() => new EffectDefinition("x", 1, EffectKind.NormalSpell, [new DealDamageStep(1, (EffectCount)99)]));
        Assert.Equal(13, LifePointFamily.Cards.Select(c => c.CardId).Distinct().Count());
        Assert.Empty(LifePointFamily.Cards.Select(c => c.CardId).Intersect(PlayablePreview.Cards.Select(c => c.CardId)));
        Assert.All(LifePointFamily.Cards, c =>
        {
            Assert.Equal(c.Kind == CardKind.Trap ? EffectKind.NormalTrap : EffectKind.NormalSpell, c.Definition.Kind);
            Assert.Equal([c.Definition.EffectId], ReviewedCardMetadata.Reviewed[c.CardId].Effects.References);
        });
    }
}
