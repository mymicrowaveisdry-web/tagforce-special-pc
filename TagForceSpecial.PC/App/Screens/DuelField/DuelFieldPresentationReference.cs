using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Decks;
using TagForceSpecial.Core.Views;
using TagForceSpecial.Effects;
using TagForceSpecial.Import.Installed;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Game;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens.DuelField;

internal static class DuelFieldPresentationReference
{
    private const uint BattleOx = 4032;

    public static void Capture(GraphicsDevice device, SpriteRenderer renderer, DuelFieldVisuals field, string root, InstalledGame game)
    {
        var catalog = game.Catalog.Catalog;
        var duel = SupportedDuelFactory.Create(game, OpeningDeck(), seed: 7);
        var input = new DuelFieldInput();
        var presentation = new DuelFieldPresentation(TimeSpan.Zero, enabled: true);
        var elapsed = TimeSpan.Zero;
        var frame = TimeSpan.FromSeconds(1 / 60.0);
        var style = DuelFieldReference.States[0].Style;

        void Advance(InputAction pressed = InputAction.None)
        {
            elapsed += frame;
            var before = duel.View;
            if (input.Handle(before, new(pressed, pressed), frame) is not { } submit) return;
            if (!duel.Choose(submit.Revision, submit.ChoiceId)) throw new InvalidOperationException(duel.Notice);
            presentation.Accept(before, duel.View, submit.ChoiceId, elapsed, input.SubmittedFrom);
        }

        void Save(string name, DuelSceneInput scene) => DuelFieldReference.Save(device, renderer, field,
            DuelScene.Compose(scene, field.Profile), Path.Combine(root, name + ".png"));
        void SaveSequence(string name) => Save(name,
            new(duel.View, 0, -1, "", elapsed.TotalSeconds, style, id => DuelCardFacts.From(catalog[id]), input)
            {
                Presentation = presentation.Frame(duel.View, elapsed)
            });

        SaveSequence("summon-00-opening");
        elapsed = DuelFieldPresentation.OpeningHold;
        // The first draw (skipped on turn 1) and the Standby Phase pass by themselves; Main Phase 1 starts on the centre Spell & Trap Zone.
        for (int i = 0; i < 4 && duel.View.Phase != DuelPhase.Main1; i++) Advance();
        Advance();
        Advance(InputAction.Down);
        if (duel.View.Phase != DuelPhase.Main1 || input.Focus.Current.Card?.CardId != BattleOx)
            throw new InvalidOperationException("The summon reference must start in Main Phase 1 with Battle Ox focused.");
        SaveSequence("summon-01-hand");
        Advance(InputAction.Confirm);
        if (input.Modal is not CardMenuModal menu || menu.Items[menu.Index].Verb != MenuVerb.Summon)
            throw new InvalidOperationException("The reference menu must highlight Normal Summon.");
        SaveSequence("summon-02-menu");
        Advance();
        long beforeSummon = duel.View.Revision;
        Advance(InputAction.Confirm);
        if (duel.View.Revision != beforeSummon + 1 || duel.View.Self.Hand.Count != 4 || duel.View.Self.Monsters[2]?.CardId != BattleOx ||
            presentation.Frame(duel.View, elapsed).SummonWorld is null)
            throw new InvalidOperationException("Expected one accepted, animated centre-slot Normal Summon.");
        var accepted = elapsed;
        // Half a vblank into each step, away from its boundary.
        TimeSpan At(int vblanks) => accepted + TimeSpan.FromSeconds((vblanks + 0.5) / DuelFieldPresentation.VblankHz);
        elapsed = At(DuelFieldPresentation.MidPose);
        SaveSequence("summon-03-motion");
        elapsed = At(DuelFieldPresentation.Landed);
        SaveSequence("summon-04-landed");
        elapsed = At(DuelFieldPresentation.PointerBack);
        SaveSequence("summon-05-settled");

        Save("field-layout-cases", new(LayoutCases(catalog), 0, -1, "", 0, style, id => DuelCardFacts.From(catalog[id]), new DuelFieldInput()));
    }

    private static DeckDefinition OpeningDeck()
    {
        var deck = InitialOpponentDecks.Create(0);
        var main = deck.Main.ToList();
        if (!main.Remove(BattleOx)) throw new InvalidOperationException("The reference deck needs Battle Ox.");
        main.Insert(2, BattleOx);
        return new(deck.DeckId, deck.Revision, deck.OwnerId, deck.CatalogId, deck.CatalogRevision, deck.SourceBuild,
            deck.PolicyId, deck.PolicyRevision, main, deck.Extra, deck.Side);
    }

    private static DuelView LayoutCases(CardCatalog catalog)
    {
        var view = DuelFieldReference.AkiBoard();
        var pendulum = catalog.Cards.First(card => card.Codes?.RawKind is 25 or 26);
        var fieldSpell = catalog.Cards.First(card => card.Codes is { RawKind: 13, Icon: 2 });
        CardView Face(int instance, CardDefinition card) => new(instance, card.GameCardId, card.Name, card.Description,
            null, null, null, false);
        var backs = Enumerable.Repeat(new CardView(null, null, "Hidden card", "", null, null, null, true), 9).ToArray();
        return view with
        {
            ActivePlayer = PlayerId.Two, Choices = [],
            Self = view.Self with { Hand = [.. view.Self.Hand, .. view.Self.Hand.Select(c => c with { InstanceId = c.InstanceId + 10 })],
                FieldSpell = Face(21, fieldSpell), Pendulum = [Face(22, pendulum), null], BanishedCount = 2 },
            Opponent = view.Opponent with { Hand = backs, FieldSpell = Face(23, fieldSpell), Pendulum = [null, Face(24, pendulum)], BanishedCount = 1 }
        };
    }
}
