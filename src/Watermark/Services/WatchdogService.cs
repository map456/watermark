using System;
using System.Threading;
using System.Windows;
using Watermark.Config;

namespace Watermark.Services;

public class WatchdogService
{
    private readonly WatermarkWindowManager _manager;
    private readonly AppSettings _settings;
    private System.Threading.Timer? _timer;

    public WatchdogService(WatermarkWindowManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
    }

    public void Start()
    {
        int interval = Math.Max(500, _settings.Behavior.WatchdogIntervalMs);
        _timer = new System.Threading.Timer(_ =>
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { _manager.HealthCheck(); } catch { }
                }));
            }
            catch { }
        }, null, interval, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
