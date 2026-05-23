# RDP Watermark

A Windows desktop overlay that displays a transparent, click-through, always-on-top
watermark whenever a user connects to the machine via RDP. Each watermark shows:

- **User + host**: `DOMAIN\user @ HOSTNAME`
- **RDP client**: `ClientName (192.168.x.x)`
- **Live timestamp**: refreshes every 30 seconds
- **Custom label**: configurable via `settings.json`

The watermark is **hidden when logged on locally at the console** — it appears only
inside RDP sessions, so internal users at the physical machine are unaffected.

Supports **Windows Server 2019 / 2022** (x64), and desktop Windows 10 / 11.

---

## Install

Double-click `dist/Watermark.msi` (administrator rights required).

After install:

- Binaries land in `C:\Program Files\Watermark\`
- `HKLM\Software\Microsoft\Windows\CurrentVersion\Run\Watermark` is written so
  every user logon auto-starts the watermark
- A `LaunchWatermark` custom action also fires the executable at the **end of
  install**, so the watermark appears immediately without requiring a logoff

### Silent install / uninstall

```powershell
msiexec /i dist\Watermark.msi /qn
msiexec /x dist\Watermark.msi /qn
```

Uninstall stops the running `Watermark.exe`, removes the files, and clears the
Run key.

---

## Customise the watermark

`C:\Program Files\Watermark\settings.json` (defaults shown):

```json
{
  "watermark": {
    "customText": "CONFIDENTIAL - DO NOT DISTRIBUTE",
    "fontFamily": "Microsoft JhengHei",
    "fontSize": 16,
    "color": "#FFFFFF",
    "opacity": 0.65,
    "rotationAngle": -30,
    "watermarksPerScreen": 5,
    "tileWidth": 360,
    "tileHeight": 200,
    "timestampFormat": "yyyy-MM-dd HH:mm:ss",
    "timestampRefreshSeconds": 30,
    "showUserAndHost": true,
    "showClientAddress": true,
    "showTimestamp": true,
    "showCustomText": true
  },
  "behavior": {
    "showOnRdpOnly": true,
    "hideOnLock": false,
    "watchdogIntervalMs": 2000
  }
}
```

`watermarksPerScreen` controls density: the program derives tile size from screen
dimensions so the chosen number of watermarks remains stable regardless of
resolution or RDP window size. Set it to `0` to fall back to explicit
`tileWidth` / `tileHeight`.

A per-user override file is also honoured (read order, first wins):

1. `%LOCALAPPDATA%\Watermark\settings.json`
2. `%ProgramData%\Watermark\settings.json`
3. `C:\Program Files\Watermark\settings.json`

Restart `Watermark.exe` after editing to apply.

---

## Build from source

```powershell
# Requires .NET 8 SDK
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.Util.wixext/5.0.2

.\build.ps1
```

The script publishes a self-contained single-file executable and packages it
into `dist\Watermark.msi`.

---

## Project layout

```
Watermark/
├── DESIGN.md                ← design document
├── README.md                ← this file
├── build.ps1                ← one-shot build script
├── src/Watermark/           ← C# WPF source
│   ├── App.xaml(.cs)        ← entry point, single-instance, event wiring
│   ├── Windows/
│   │   └── WatermarkWindow  ← transparent overlay + diagonal tiling
│   ├── Services/
│   │   ├── RdpSessionDetector       ← WTS session notifications
│   │   ├── SessionInfoProvider      ← user / host / client IP
│   │   ├── WatermarkWindowManager   ← multi-monitor management
│   │   └── WatchdogService          ← self-recovery
│   ├── Native/NativeMethods.cs      ← P/Invoke
│   ├── Config/AppSettings.cs        ← settings model
│   ├── settings.json                ← default settings
│   ├── GlobalUsings.cs              ← WPF/WinForms namespace aliases
│   └── app.manifest
└── installer/
    └── Package.wxs          ← WiX v5 MSI definition
```

`bin/`, `obj/`, `publish/`, and `dist/` are build outputs (ignored by git).
Generate `dist/Watermark.msi` with `.\build.ps1`.

---

## How it works

1. **Detection** — `GetSystemMetrics(SM_REMOTESESSION)` checks at startup;
   `WTSRegisterSessionNotification` + `WM_WTSSESSION_CHANGE` track connect /
   disconnect / lock / unlock events in real time.

2. **Overlay** — One borderless WPF `Window` per monitor with
   `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`
   so it never steals input, never appears in the taskbar / Alt-Tab, and stays
   above other windows.

3. **Resilience** — A `WatermarkWindowManager` listens to
   `SystemEvents.DisplaySettingsChanged` and a 2-second watchdog ticks so that
   any RDP window resize / monitor change / external close is repaired within
   one tick. Bounds are re-derived from `Screen.AllScreens` on every health
   check so the watermark layout stays consistent at any resolution.

4. **Multi-session safety** — The single-instance mutex uses the `Local\`
   prefix (per-session), so each RDP session runs its own `Watermark.exe`
   independently.

---

## Known limitations

See [`DESIGN.md`](DESIGN.md) §9 for details. Highlights:

- Administrators can still kill `Watermark.exe` via Task Manager — the
  watchdog re-creates the windows on the next tick (within 2 seconds)
- Exclusive-mode fullscreen (DirectX exclusive) cannot be overlaid
- The UAC Secure Desktop cannot be overlaid (by Windows design)
- Application log: `%LOCALAPPDATA%\Watermark\watermark.log`

---

## License

TBD.
