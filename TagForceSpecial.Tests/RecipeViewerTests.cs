using TagForceSpecial.Assets.GameSource;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.Recipes;
using TagForceSpecial.Import.Storage;

namespace TagForceSpecial.Tests;

public sealed class RecipeViewerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tfsp-recipes-" + Guid.NewGuid().ToString("N"));
    private readonly CardCatalog cards = new(new(null, "recipe-fixture"),
        [new CardDefinition(1, "Fixture", "", new(1000, 1000, 4))]);
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private RecipeDocument Document() => new(1, GameBuildProfile.English21.BuildId, GameBuildProfile.English21.ImageSha256,
        Digest, Digest, Digest,
        [new(RecipeListKind.User, 4, "Zeta", new DateTime(2020, 1, 1), [1], [], [], Digest, 0x08af0000),
         new(RecipeListKind.User, 8, "Alpha", new DateTime(2021, 1, 1), [1, 1], [], [], Digest, 0x08af01b4)],
        [], new([], [], [], 36, 29, 8, 174, 38, 155, 52));

    private RecipeCatalog Open(RecipeDocument? document = null)
    {
        var storage = new PrivateStorage(directory);
        storage.Write("recipes/viewer.json", document ?? Document());
        return RecipeCatalog.Open(storage, cards)!;
    }

    private static bool Press(RecipeViewerSession viewer, InputAction action) => viewer.Handle(new(action, action));

    [Fact]
    public void SortingKeepsSourceSlotsAndRecordsAndReturnsAnExplicitlyUnverifiedSelection()
    {
        var catalog = Open();
        var viewer = new RecipeViewerSession(catalog);
        Assert.Equal(4, viewer.Selection!.Slot);
        viewer.SetOrder(RecipeSort.Name, false);
        Assert.Equal(8, viewer.Selection!.Slot);
        viewer.SetOrder(RecipeSort.CardCount, true);
        Assert.Equal(8, viewer.Selection!.Slot);
        Assert.Equal(new[] { "Zeta", "Alpha" }, catalog.Records.Select(r => r.Name));
        Assert.True(Press(viewer, InputAction.Back));
        Assert.Equal("unverified", viewer.Selection!.Verification);
        Assert.Equal(Digest, viewer.Selection.SourceIdentity);
    }

    [Fact]
    public void ConfirmManagesUserRecipesAndCancelDoesNotReplaceOrDeleteSourceCards()
    {
        var source = Open();
        var viewer = new RecipeViewerSession(source);
        Press(viewer, InputAction.Confirm);
        Assert.Equal(RecipeViewerModal.Manage, viewer.Modal);
        Press(viewer, InputAction.Down);
        Press(viewer, InputAction.Confirm);
        Assert.Equal(RecipeViewerModal.Notice, viewer.Modal);
        Press(viewer, InputAction.Back);
        Assert.Equal(2, source.Records.Count);
        Assert.Equal(new uint[] { 1 }, source.Records[0].Main);
        Assert.Equal(4, viewer.Selection!.Slot);
    }

    [Fact]
    public void EmptyCharacterMappingHasNoInventedSelectionAndCanReturnToUserRecords()
    {
        var viewer = new RecipeViewerSession(Open());
        Press(viewer, InputAction.Switch);
        Assert.Equal(RecipeListKind.Character, viewer.Kind);
        Assert.Empty(viewer.Rows);
        Assert.Null(viewer.Selection);
        Assert.False(Press(viewer, InputAction.Confirm));
        Press(viewer, InputAction.NextPage);
        Assert.Equal(4, viewer.Selection!.Slot);
    }

    [Fact]
    public void WrongSourceUnknownCardsAndDuplicateSlotsAreRejectedRatherThanReplaced()
    {
        var doc = Document();
        Assert.Throws<InvalidDataException>(() => Open(doc with { IsoSha256 = Digest }));
        Assert.Throws<InvalidDataException>(() => Open(doc with { Recipes = [doc.Recipes[0] with { Main = [999] }] }));
        Assert.Throws<InvalidDataException>(() => Open(doc with { Recipes = [doc.Recipes[0], doc.Recipes[0]] }));
    }

    [Fact]
    public void CancelledSortLeavesThePriorOrderingAndCursorUntouched()
    {
        var viewer = new RecipeViewerSession(Open());
        Press(viewer, InputAction.Down);
        Press(viewer, InputAction.Menu);
        Press(viewer, InputAction.Down);
        Press(viewer, InputAction.Right);
        Press(viewer, InputAction.Down);
        Press(viewer, InputAction.Back);
        Assert.Equal(RecipeSort.Id, viewer.Sort);
        Assert.False(viewer.Inverse);
        Assert.Equal(8, viewer.Selection!.Slot);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
