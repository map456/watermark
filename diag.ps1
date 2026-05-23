Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

public class WinEnum {
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int idx);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }

    public static List<string> AllByPid(uint targetPid) {
        var list = new List<string>();
        EnumWindows((h, _) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid) return true;
            int len = GetWindowTextLength(h);
            var sb = new StringBuilder(Math.Max(len + 1, 64));
            GetWindowText(h, sb, sb.Capacity);
            int ex = GetWindowLong(h, -20);
            int wl = GetWindowLong(h, -16);
            RECT r; GetWindowRect(h, out r);
            list.Add(string.Format("hwnd={0} visible={1} ex=0x{2:X8} style=0x{3:X8} rect=({4},{5})-({6},{7}) title='{8}'",
                h, IsWindowVisible(h), ex, wl, r.L, r.T, r.R, r.B, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$p = Get-Process Watermark -ErrorAction SilentlyContinue
if (-not $p) { Write-Output "no Watermark process"; exit }
Write-Output "PID=$($p.Id)  WS=$([math]::Round($p.WorkingSet64/1MB,1))MB"
$ws = [WinEnum]::AllByPid([uint32]$p.Id)
Write-Output ("Found {0} windows owned by Watermark.exe" -f $ws.Count)
foreach ($w in $ws) { Write-Output $w }
