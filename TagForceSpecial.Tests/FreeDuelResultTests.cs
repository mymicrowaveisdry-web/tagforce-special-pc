using TagForceSpecial.PC.DuelUI.Input;
using TagForceSpecial.PC.Screens;

namespace TagForceSpecial.Tests;

public sealed class FreeDuelResultTests
{
    private static readonly GuiButtons Confirm = new(false, false, false, false, true, false, false);
    private static readonly GuiButtons Down = new(false, true, false, false, false, false, false);
    private static readonly GuiButtons Back = new(false, false, false, false, false, true, false);

    [Fact]
    public void ConfirmOnTheResultCanStartARematch()
    {
        var result = new FreeDuelResultSession();
        Assert.Equal(FreeDuelResultAction.Rematch, result.Handle(Confirm));
    }

    [Theory]
    [InlineData(false, FreeDuelResultAction.Return)]
    [InlineData(true, FreeDuelResultAction.SaveAndReturn)]
    public void ReturnOpensSeparateSaveChoice(bool save, FreeDuelResultAction expected)
    {
        var result = new FreeDuelResultSession();
        result.PointAt(1);
        Assert.Equal(FreeDuelResultAction.None, result.Handle(Confirm));
        Assert.Equal(0, result.Focus);
        if (save) result.Handle(Down);
        Assert.Equal(expected, result.Handle(Confirm));
    }

    [Fact]
    public void BackFromSaveReturnsToTheResult()
    {
        var result = new FreeDuelResultSession();
        Assert.Equal(FreeDuelResultAction.None, result.Handle(Back));
        Assert.Equal(0, result.Focus);
        Assert.Equal(FreeDuelResultAction.None, result.Handle(Back));
        Assert.Equal(1, result.Focus);
    }
}
