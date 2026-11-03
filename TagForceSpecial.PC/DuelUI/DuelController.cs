using TagForceSpecial.Cards;
using TagForceSpecial.Core;
using TagForceSpecial.Core.Views;

namespace TagForceSpecial.PC.DuelUI;

/// <summary>Initial presentation controller; all choices use the shared authorized command adapter.</summary>
public sealed class DuelController
{
    private readonly DuelActionSource source;
    private readonly PlayerId viewer, opponent;
    public DuelView View { get; private set; }
    public string Notice { get; private set; } = "Choose an action";
    public DuelController(DuelEngine engine, CardCatalog catalog, PlayerId viewer = PlayerId.One)
    {
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        this.viewer = viewer;
        opponent = viewer == PlayerId.One ? PlayerId.Two : PlayerId.One;
        source = new(engine, catalog); View = Build();
    }
    public bool Choose(long revision, int token)
    {
        var before = source.Observe(viewer);
        var result = source.Execute(viewer, revision, token);
        Notice = result.Accepted ? before.Actions[token].Label : "That choice has expired";
        View = Build(); return result.Accepted;
    }
    public bool StepOpponent()
    {
        var view = source.Observe(opponent);
        if (view.Availability != ActionAvailability.Ready) return false;
        var result = source.Execute(opponent, view.Revision, view.Actions[0].Token);
        Notice = "Opponent acted"; View = Build(); return result.Accepted;
    }
    private DuelView Build()
    {
        var view = source.Observe(viewer);
        string prompt = view.Outcome is { } result ? result.Winner == viewer ? "YOU WIN" : "YOU LOSE"
            : view.Decision is { } decision ? $"Choose {decision.Kind}" : view.Actor == viewer ? "Choose an action" : "Opponent turn";
        return new(view.Revision, viewer, view.ActivePlayer, view.Turn, view.Phase, view.Self, view.Opponent, prompt,
            PlayerProjection.Read(view.Actions.Select(a => new ChoiceView(a.Token, a.Label, a.CardInstanceId))), view.Outcome);
    }
}
