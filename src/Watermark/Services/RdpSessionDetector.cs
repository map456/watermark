using System;
using System.Windows;
using System.Windows.Interop;
using Watermark.Native;

namespace Watermark.Services;

/// <summary>
/// Detects RDP sessions and listens to WTS session events via a hidden HwndSource.
/// </summary>
public class RdpSessionDetector
{
    public event EventHandler? RdpConnected;
    public event EventHandler? RdpDisconnected;
    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;

    private HwndSource? _hwndSource;
    private bool _registered;
    public bool IsRdpSession { get; private set; }

    public void Start()
    {
        var parameters = new HwndSourceParameters("WatermarkSessionNotifier")
        {
            Width = 0,
            Height = 0,
            PositionX = -10000,
            PositionY = -10000,
            WindowStyle = 0,
            ParentWindow = IntPtr.Zero,
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        _registered = NativeMethods.WTSRegisterSessionNotification(_hwndSource.Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION);

        IsRdpSession = NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
    }

    public void Stop()
    {
        if (_registered && _hwndSource != null)
        {
            try { NativeMethods.WTSUnRegisterSessionNotification(_hwndSource.Handle); } catch { }
            _registered = false;
        }
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WTSSESSION_CHANGE)
        {
            int reason = wParam.ToInt32();
            switch (reason)
            {
                case NativeMethods.WTS_REMOTE_CONNECT:
                    IsRdpSession = true;
                    RdpConnected?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WTS_REMOTE_DISCONNECT:
                    IsRdpSession = false;
                    RdpDisconnected?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WTS_CONSOLE_CONNECT:
                    IsRdpSession = NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
                    break;
                case NativeMethods.WTS_CONSOLE_DISCONNECT:
                    IsRdpSession = NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
                    break;
                case NativeMethods.WTS_SESSION_LOCK:
                    SessionLocked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WTS_SESSION_UNLOCK:
                    SessionUnlocked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WTS_SESSION_LOGON:
                    IsRdpSession = NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
                    if (IsRdpSession) RdpConnected?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        return IntPtr.Zero;
    }
}
