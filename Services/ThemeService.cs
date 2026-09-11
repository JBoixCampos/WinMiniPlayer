using Microsoft.Win32;

namespace MiniPlayer.Services;

/// <summary>
/// Tracks whether Windows' taskbar/system theme is light or dark, so the bar can
/// match it. Reads the same registry value Explorer uses and re-checks on any
/// user-preference change (theme switches surface as a "General" category change).
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public event Action? Changed;

    public bool IsLightTheme { get; private set; }

    public ThemeService()
    {
        IsLightTheme = ReadIsLightTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        bool light = ReadIsLightTheme();
        if (light == IsLightTheme) return;

        IsLightTheme = light;
        Changed?.Invoke();
    }

    private static bool ReadIsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
            // Default to dark (0) to match the app's existing dark-only look when unreadable.
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
