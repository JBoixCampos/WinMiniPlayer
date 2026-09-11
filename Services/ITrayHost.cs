namespace MiniPlayer.Services;

/// <summary>
/// The bar-owning window's surface exposed to <see cref="TrayService"/>, so the tray
/// menu drives the exact same state/logic as the bar's own right-click menu instead
/// of duplicating it.
/// </summary>
public interface ITrayHost
{
    bool IsAboveTaskbar { get; }
    bool IsOnTaskbar { get; }
    bool Draggable { get; }
    bool StartupEnabled { get; }

    void SetPositionAboveTaskbar();
    void SetPositionOnTaskbar();
    void ToggleDraggable();
    void ToggleStartup();

    /// <summary>Recovery action: snaps back to the default position on the primary monitor.</summary>
    void ResetPosition();

    void ExitApp();
}
