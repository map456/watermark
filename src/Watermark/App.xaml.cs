using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Watermark.Config;
using Watermark.Services;

namespace Watermark;

public partial class App : System.Windows.Application
{
    // Local\ prefix scopes the mutex to the current Terminal Services session.
    // Each RDP session needs its own watermark instance, so DO NOT use Global\.
    private const string MutexName = "Local\\Watermark_SingleInstance_{8B3F9C42-7E51-4A2D-9F18-2B4C8D7E1F30}";
    private Mutex? _singleInstanceMutex;
    private WatermarkWindowManager? _windowManager;
    private RdpSessionDetector? _rdpDetector;
    private WatchdogService? _watchdog;
    private SessionInfoProvider? _infoProvider;
    private AppSettings _settings = AppSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] FATAL: {ex}\r\n");
            }
            catch { }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] UI: {args.Exception}\r\n"); } catch { }
            args.Handled = true;
        };

        _settings = AppSettings.Load();
        _infoProvider = new SessionInfoProvider();
        _windowManager = new WatermarkWindowManager(_settings, _infoProvider);
        _rdpDetector = new RdpSessionDetector();
        _watchdog = new WatchdogService(_windowManager, _settings);

        bool showAlways = !_settings.Behavior.ShowOnRdpOnly;

        _rdpDetector.RdpConnected += (_, _) => Dispatcher.Invoke(() => _windowManager.Show());
        _rdpDetector.RdpDisconnected += (_, _) =>
        {
            if (!showAlways) Dispatcher.Invoke(() => _windowManager.Hide());
        };
        _rdpDetector.SessionLocked += (_, _) =>
        {
            if (_settings.Behavior.HideOnLock) Dispatcher.Invoke(() => _windowManager.Hide());
        };
        _rdpDetector.SessionUnlocked += (_, _) =>
        {
            if (_settings.Behavior.HideOnLock && (showAlways || _rdpDetector!.IsRdpSession))
                Dispatcher.Invoke(() => _windowManager.Show());
        };

        _rdpDetector.Start();
        _watchdog.Start();

        if (showAlways || _rdpDetector.IsRdpSession)
        {
            _windowManager.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _watchdog?.Stop();
            _rdpDetector?.Stop();
            _windowManager?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }

    internal static string GetLogPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watermark");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "watermark.log");
    }
}
