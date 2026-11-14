using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Effects;
using static TagForceSpecial.Tests.EventEffectScenario;

namespace TagForceSpecial.Tests;

public sealed class AdditiveAuraDeclarationTests
{
    private const uint Shield = 5161, Chorus = 4899, Rain = 7457, Wetlands = 7627, Wasteland = 4337,
        Dino = 901, Aqua = 902, HighAqua = 903, Zombie = 904, Warrior = 905, Xyz = 906, Rock = 907, EarthAqua = 908,
        Negator = 920, Remove = 921, Down = 922, Up = 923, Type = 924, Attribute = 925, Level = 926;

    private static DuelEngine Make(uint aura, uint monster = Dino, params uint[] tools)
    {
        uint[] auraIds = [Shield, Chorus, Rain, Wetlands, Wasteland];
        (uint Id, uint Type, uint Attribute, uint Level, uint Kind)[] stats =
        [
            (Dino, MonsterTypes.Dinosaur, MonsterAttributes.Earth, 2, 0),
            (Aqua, MonsterTypes.Aqua, MonsterAttributes.Water, 2, 0),
            (HighAqua, MonsterTypes.Aqua, MonsterAttributes.Water, 3, 0),
            (Zombie, MonsterTypes.Zombie, MonsterAttributes.Water, 4, 0),
            (Warrior, MonsterTypes.Warrior, MonsterAttributes.Water, 2, 0),
            (Xyz, MonsterTypes.Aqua, MonsterAttributes.Water, 2, 22),
            (Rock, MonsterTypes.Rock, MonsterAttributes.Earth, 4, 0),
            (EarthAqua, MonsterTypes.Aqua, MonsterAttributes.Earth, 2, 0)
        ];
        uint[] utility = [Negator, Remove, Down, Up, Type, Attribute, Level];
        var family = ShortTextCardFamily.Family;
        var catalog = new CardCatalog(new(null, "synthetic-additive-aura-boundaries"),
            stats.Select(m => new CardDefinition(m.Id, $"Monster {m.Id}", "", new(1000, 1000, m.Level), new(m.Attribute, m.Type, m.Kind, m.Kind, 0, 0, 0)))
            .Concat(auraIds.Concat(utility).Select(id => new CardDefinition(id, $"Spell {id}", "", new(0, 0, 0)))));
        var rules = family.Rules.Where(p => auraIds.Contains(p.Key)).ToDictionary();
        foreach (var m in stats) rules[m.Id] = new(CardCategory.Monster, AllowsNormalSummon: true, AllowsBasicBattle: true);
        foreach (uint id in utility) rules[id] = new(id is Negator or Remove ? CardCategory.Trap : CardCategory.Spell, AllowsSpellTrapSet: true);
        var faceUpOwn = new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceUp);
        return new(catalog, rules,
            [aura, monster, .. tools, .. Enumerable.Repeat(monster, 38 - tools.Length)],
            Enumerable.Repeat(monster, 40), effectDefinitions:
            [.. family.Definitions.Where(d => auraIds.Contains(d.CardId)),
                new("negator", Negator, EffectKind.ContinuousTrap, [], negation: LastingNegation.Spells),
                new("remove", Remove, EffectKind.NormalTrap, [new DestroyTargetsStep()],
                    fieldTargets: [new FieldTarget(FieldCardKind.SpellTrap, TargetSide.Own, FieldCardFilter.FaceUp)]),
                new("down", Down, EffectKind.NormalSpell, [new FlipTargetsFaceDownStep()], fieldTargets: [faceUpOwn]),
                new("up", Up, EffectKind.NormalSpell, [new FlipTargetsFaceUpStep(BattlePosition.FaceUpDefense)],
                    fieldTargets: [new FieldTarget(FieldCardKind.Monster, TargetSide.Own, FieldCardFilter.FaceDown)]),
                new EffectDefinition("type", Type, EffectKind.NormalSpell, [new ChangeTargetsTraitStep(IdentityTrait.Type)], fieldTargets: [faceUpOwn]).WithDeclaration(DeclarationKind.MonsterType),
                new EffectDefinition("attribute", Attribute, EffectKind.NormalSpell, [new ChangeTargetsTraitStep(IdentityTrait.Attribute)], fieldTargets: [faceUpOwn]).WithDeclaration(DeclarationKind.Attribute),
                new EffectDefinition("level", Level, EffectKind.NormalSpell, [new SetDeclaredLevelStep(new AnyMonsterFilter())]).WithDeclaration(DeclarationKind.Level)], shuffleSeed: 15);
    }

    private static int Hand(DuelEngine e, uint id) => e.State.GetPlayer(e.State.ActivePlayer).Hand.First(c => c.GameCardId == id).InstanceId;
    private static int Zone(uint aura) => aura == Shield ? 0 : DuelPlayerState.FieldZoneIndex;
    private static void Activate(DuelEngine e, uint id, int value = 0, uint? target = null)
    {
        Ok(e, new ActivateSpell(e.State.ActivePlayer, Hand(e, id), id is Shield or Chorus or Rain or Wetlands or Wasteland ? Zone(id) : 3));
        Drive(e, value, target);
    }

    private static void Drive(DuelEngine e, int value = 0, uint? target = null)
    {
        for (int i = 0; i < 40; i++)
        {
            DuelCommand? command = e.State.PendingDecision is { } p
                ? p.FieldTargetOptions is { } options
                    ? options.First(t => target is null || t.Card.GameCardId == target) is { } t
                        ? new ChooseDecisionFieldTarget(p.Player, p.DecisionId, t.Player, t.Location, t.Zone, t.Card.InstanceId) : null
                    : new ChooseDecisionCard(p.Player, p.DecisionId, p.CardOptions[0].InstanceId)
                : e.State.PendingChain is not { } chain ? null
                : chain.Declaration is { } call ? new DeclareValue(call.Player, call.DecisionId, value)
                : chain.NextLink == 0 ? new PassChainResponse(chain.Responder, chain.DecisionId)
                : new ResolveNextChainLink(e.State.ActivePlayer, chain.DecisionId);
            if (command is null) return;
            Ok(e, command);
        }
        throw new InvalidOperationException("Aura scenario did not terminate.");
    }

    private static void Ready(DuelEngine e, uint aura, uint monster)
    {
        EnterMain(e); Summon(e, monster); Activate(e, aura);
        NextMain(e); Summon(e, monster); NextMain(e);
    }

    private static void Stats(DuelEngine e, PlayerId player, int attack, int defense)
    {
        Assert.Equal(attack, e.GetMonsterStatistics(player, 0)!.Attack);
        Assert.Equal(defense, e.GetMonsterStatistics(player, 0)!.Defense);
    }

    [Theory]
    [InlineData(Shield, Dino, 1000, 1300, 1000)]
    [InlineData(Chorus, Dino, 1000, 1000, 1000)]
    [InlineData(Rain, Dino, 500, 1400, 1400)]
    [InlineData(Wetlands, Aqua, 2200, 1000, 1000)]
    [InlineData(Wasteland, Dino, 1200, 1200, 1200)]
    public void ActualDeclarationsApplyCorrectSideAndAdditiveValues(uint aura, uint monster, int attack, int defense, int opposingDefense)
    {
        var e = Make(aura, monster); Ready(e, aura, monster);
        Stats(e, PlayerId.One, attack, defense);
        Stats(e, PlayerId.Two, aura == Shield ? 1000 : attack, opposingDefense);
    }

    [Theory]
    [InlineData(HighAqua)]
    [InlineData(Warrior)]
    [InlineData(EarthAqua)]
    [InlineData(Xyz)]
    public void WetlandsRequiresAllThreeCurrentTraitsAndALevel(uint monster)
    {
        var e = Make(Wetlands, monster); Ready(e, Wetlands, monster); Stats(e, PlayerId.One, 1000, 1000);
    }

    [Theory]
    [InlineData(Dino, 1200)]
    [InlineData(Zombie, 1200)]
    [InlineData(Rock, 1200)]
    [InlineData(Warrior, 1000)]
    public void WastelandMatchesAUnionOfTypesWithoutStacking(uint monster, int amount)
    {
        var e = Make(Wasteland, monster); Ready(e, Wasteland, monster); Stats(e, PlayerId.One, amount, amount);
    }

    [Fact]
    public void ChorusRecalculatesImmediatelyOnPositionChangesAndEffectFlips()
    {
        var e = Make(Chorus, Dino, Down, Up); Ready(e, Chorus, Dino);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpDefense)); Stats(e, PlayerId.One, 1000, 1500);
        Activate(e, Down); Assert.Equal(BattlePosition.FaceDownDefense, e.State.GetPlayer(PlayerId.One).MonsterZones[0]!.Position);
        Activate(e, Up); Stats(e, PlayerId.One, 1000, 1500);
        NextMain(e); NextMain(e);
        Ok(e, new ChangeMonsterPosition(PlayerId.One, 0, BattlePosition.FaceUpAttack)); Stats(e, PlayerId.One, 1000, 1000);
    }

    [Theory]
    [InlineData(Type, 15)]
    [InlineData(Attribute, 5)]
    [InlineData(Level, 3)]
    public void WetlandsReevaluatesCurrentTypeAttributeAndLevel(uint change, int value)
    {
        var e = Make(Wetlands, Aqua, change); Ready(e, Wetlands, Aqua);
        Stats(e, PlayerId.One, 2200, 1000); Activate(e, change, value); Stats(e, PlayerId.One, 1000, 1000);
        Stats(e, PlayerId.Two, 2200, 1000);
    }

    [Theory]
    [InlineData(Shield, 1000, 1300)]
    [InlineData(Rain, 500, 1400)]
    [InlineData(Wasteland, 1200, 1200)]
    public void SuppressionRemovalAndSourceLifetimeRecalculateWithoutLosingTheAura(uint aura, int attack, int defense)
    {
        var e = Make(aura, Dino, Negator, Remove, Remove);
        EnterMain(e); Summon(e, Dino); Set(e, Negator, 2); Set(e, Remove, 1); Activate(e, aura);
        NextMain(e); NextMain(e); Ok(e, new ActivateSetSpell(PlayerId.One, 2)); Drive(e);
        Stats(e, PlayerId.One, 1000, 1000);
        Ok(e, new ActivateSetSpell(PlayerId.One, 1)); Drive(e, target: Negator); Stats(e, PlayerId.One, attack, defense);
        Set(e, Remove, 1); NextMain(e); NextMain(e);
        Ok(e, new ActivateSetSpell(PlayerId.One, 1)); Drive(e, target: aura); Stats(e, PlayerId.One, 1000, 1000);
    }
}
