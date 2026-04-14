using System;
using Microsoft.Win32;
using CommandDeck.Models;

namespace CommandDeck.Helpers;

/// <summary>
/// Reads the Windows "AppsUseLightTheme" preference and notifies when it changes.
/// Depends only on Microsoft.Win32 — no WinUI/UWP required.
/// </summary>
public static class SystemThemeDetector
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>
    /// Raised when the user changes the Windows app color mode (dark ↔ light).
    /// Subscribe once at startup; events arrive on the thread-pool so dispatch
    /// to the UI thread before calling WPF APIs.
    /// </summary>
    public static event EventHandler? SystemModeChanged;

    static SystemThemeDetector()
    {
        // SystemEvents.UserPreferenceChanged fires for many categories — filter to General
        // which is triggered by OS theme changes.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            SystemModeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the current OS app mode. Returns <see cref="ThemeMode.Dark"/> if
    /// the registry key is absent, cannot be read, or reports value 0 (dark mode).
    /// </summary>
    public static ThemeMode GetSystemMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue(AppsUseLightThemeValue) is int value)
                return value == 1 ? ThemeMode.Light : ThemeMode.Dark;
        }
        catch
        {
            // Fallback to dark on any error (registry unavailable, permissions, etc.)
        }

        return ThemeMode.Dark;
    }
}
