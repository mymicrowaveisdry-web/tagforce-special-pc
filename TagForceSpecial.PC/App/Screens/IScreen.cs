using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.DuelUI;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Screens;

public interface IGameScreen
{
    string ScreenId { get; }
    void OnEnter(ScreenContext context, ScreenArguments arguments);
    void HandleInput(PspInputFrame input);
    void Update(TimeSpan simulationDelta, TimeSpan presentationTime);
    void Draw(ScreenDrawContext draw);
    void OnExit();
}

public sealed record ScreenArguments(string? Selection = null);
public sealed record ScreenDrawContext(SpriteRenderer Renderer, TimeSpan PresentationTime);
public sealed record PresentationSettings(bool IntegerScaling);
public interface IScreenNavigation
{
    void GoTo(string screenId, ScreenArguments? arguments = null);
    void PushModal(string screenId, ScreenArguments? arguments = null);
    void PopModal();
}
public interface IScreenFocus
{
    string? CaptureFocus();
    void RestoreFocus(string? focus);
}

/// <summary>Presentation capabilities only; engine mutation stays in application command adapters.</summary>
public sealed record ScreenContext(OriginalScreenAssetResolver Assets, IScreenNavigation Navigation,
    Func<PresentationSettings> Settings, Action<AudioCue> EnqueueAudio, Func<DuelView?> AuthorizedDuelView);
