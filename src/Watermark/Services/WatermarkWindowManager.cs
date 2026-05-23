using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using Watermark.Config;
using Watermark.Windows;

namespace Watermark.Services;

public class WatermarkWindowManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SessionInfoProvider _info;
    private readonly List<WatermarkWindow> _windows = new();
    private bool _shouldBeVisible;
    private bool _disposed;

    public IReadOnlyList<WatermarkWindow> Windows => _windows;
    public bool ShouldBeVisible => _shouldBeVisible;

    public WatermarkWindowManager(AppSettings settings, SessionInfoProvider info)
    {
        _settings = settings;
        _info = info;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public void Show()
    {
        _shouldBeVisible = true;
        EnsureWindowsForScreens();
        foreach (var w in _windows)
        {
            if (!w.IsVisible) w.Show();
            w.ForceTopmost();
        }
    }

    public void Hide()
    {
        _shouldBeVisible = false;
        foreach (var w in _windows)
        {
            if (w.IsVisible) w.Hide();
        }
    }

    /// <summary>
    /// Verifies all expected windows exist, are alive, and match the *current* screen bounds.
    /// Also re-creates any that were closed externally. Called by watchdog + DisplaySettingsChanged.
    /// </summary>
    public void HealthCheck()
    {
        if (!_shouldBeVisible) return;
        EnsureWindowsForScreens();

        // Re-target every existing window to the current Screen instance with up-to-date bounds.
        // Screen.AllScreens returns fresh instances; matching by DeviceName lets us follow RDP resize.
        var screens = System.Windows.Forms.Screen.AllScreens;
        foreach (var w in _windows)
        {
            try
            {
                var current = System.Array.Find(screens, s => s.DeviceName == w.Screen.DeviceName);
                if (current != null)
                {
                    w.UpdateForScreen(current);
                }
                if (!w.IsVisible) w.Show();
                w.ForceTopmost();
            }
            catch { }
        }
    }

    private void EnsureWindowsForScreens()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;

        // Drop windows for monitors that no longer exist or are broken
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            var w = _windows[i];
            bool dead = false;
            try { dead = !w.IsLoaded && w.IsActive == false && w.Dispatcher.HasShutdownStarted; }
            catch { dead = true; }
            if (dead)
            {
                try { w.Close(); } catch { }
                _windows.RemoveAt(i);
            }
        }

        foreach (var screen in screens)
        {
            if (_windows.Any(w => SameScreen(w, screen))) continue;
            var win = new WatermarkWindow(_settings, _info, screen);
            win.Closed += (_, _) => _windows.Remove(win);
            _windows.Add(win);
            if (_shouldBeVisible) win.Show();
        }

        // Remove windows whose screen no longer exists
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            var w = _windows[i];
            if (!screens.Any(s => SameScreen(w, s)))
            {
                try { w.Close(); } catch { }
                _windows.RemoveAt(i);
            }
        }
    }

    private static bool SameScreen(WatermarkWindow w, System.Windows.Forms.Screen s)
        => w.Screen.DeviceName == s.DeviceName;

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => HealthCheck());
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var w in _windows)
        {
            try { w.Close(); } catch { }
        }
        _windows.Clear();
    }
}
