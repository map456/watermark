# RDP 浮水印工具 — 設計文件

## 1. 專案目標

開發一個 Windows 桌面工具，當使用者透過 RDP（遠端桌面）連入主機時，在整個桌面上方覆蓋一層**透明、穿透、置頂**的浮水印，顯示使用者身分與來源資訊，達到：

- **防截圖洩密**：截圖會帶上身分與來源
- **操作可追溯**：洩密後可從截圖內容追蹤責任人
- **本地登入不干擾**：直接坐在主機前操作的內部人員看不到浮水印

---

## 2. 需求摘要

| 項目 | 決定 |
|---|---|
| 顯示時機 | 僅 RDP session 內顯示，本地登入隱藏 |
| 顯示內容 | 使用者名稱 + 電腦名、動態時間戳記、Client IP / 來源主機、自訂標記文字 |
| 技術棧 | C# WPF（.NET Framework 4.8 或 .NET 8） |
| 防護強度 | 基本：登入時自動啟動 + watchdog 自我復原 |
| 多螢幕 | 支援 |
| 部署方式 | 單機安裝，登錄檔自動啟動 |

---

## 3. 系統架構

```
┌─────────────────────────────────────────────────────────────┐
│                      Watermark.exe                          │
│                                                             │
│  ┌──────────────────┐    ┌──────────────────────────────┐   │
│  │ RdpSessionDetector│───▶│  WatermarkWindowManager      │   │
│  │ (WTS event hook) │    │  - 多螢幕視窗管理            │   │
│  └──────────────────┘    │  - 顯示 / 隱藏控制           │   │
│           │              └──────────────┬───────────────┘   │
│           │                             │                   │
│  ┌──────────────────┐                   ▼                   │
│  │SessionInfoProvider│         ┌────────────────────┐       │
│  │  - User / Host    │         │ WatermarkWindow ×N │       │
│  │  - ClientIP (WTS) │────────▶│  (透明、置頂、穿透)│       │
│  │  - Timestamp      │         │  斜向平鋪文字      │       │
│  └──────────────────┘         └────────────────────┘       │
│                                                             │
│  ┌──────────────────┐    ┌──────────────────────────────┐   │
│  │ WatchdogService  │    │  ConfigLoader (settings.json)│   │
│  │ - 視窗存活檢查   │    │  - 自訂文字、字體、透明度    │   │
│  │ - 單一實例 Mutex │    └──────────────────────────────┘   │
│  └──────────────────┘                                       │
└─────────────────────────────────────────────────────────────┘
```

---

## 4. 專案檔案結構

```
Watermark/
├── Watermark.sln
├── Watermark.csproj
├── App.xaml
├── App.xaml.cs                      ← 程式進入點、單一實例檢查、初始化
├── Windows/
│   └── WatermarkWindow.xaml(.cs)    ← 浮水印視窗（每個 monitor 一個實例）
├── Services/
│   ├── RdpSessionDetector.cs        ← 偵測 RDP / 監聽 session 變化事件
│   ├── SessionInfoProvider.cs       ← 取得 User / Host / ClientIP / Time
│   ├── WatermarkWindowManager.cs    ← 管理多螢幕視窗的建立、顯隱
│   └── WatchdogService.cs           ← 定時健康檢查、自我復原
├── Native/
│   └── NativeMethods.cs             ← P/Invoke（user32、wtsapi32）
├── Config/
│   ├── AppSettings.cs               ← 設定模型
│   └── settings.json                ← 自訂文字、樣式參數
├── Installer/
│   └── AutoStartInstaller.cs        ← 註冊 HKCU\...\Run 自動啟動
└── README.md
```

---

## 5. 關鍵技術細節

### 5.1 RDP Session 偵測

**判斷當前是否 RDP**
```csharp
[DllImport("user32.dll")]
static extern int GetSystemMetrics(int nIndex);
const int SM_REMOTESESSION = 0x1000;

bool isRdp = GetSystemMetrics(SM_REMOTESESSION) != 0;
```

**監聽 session 變化**
- `WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION)` 註冊通知
- 在 WndProc 攔截 `WM_WTSSESSION_CHANGE`（0x02B1）
- 關注事件：
  - `WTS_REMOTE_CONNECT` (0x3)：RDP 連入 → 顯示
  - `WTS_REMOTE_DISCONNECT` (0x4)：RDP 斷線 → 隱藏
  - `WTS_SESSION_LOCK` (0x7) / `WTS_SESSION_UNLOCK` (0x8)：可選擇要不要在鎖屏時隱藏

### 5.2 取得 RDP Client 資訊

使用 `WTSQuerySessionInformation`：

| WTSInfoClass | 取得內容 |
|---|---|
| `WTSClientName` (10) | RDP 來源主機名 |
| `WTSClientAddress` (14) | RDP 來源 IP（WTS_CLIENT_ADDRESS 結構） |
| `WTSUserName` (5) | session 的使用者名稱 |
| `WTSDomainName` (7) | 網域名稱 |

```csharp
[DllImport("wtsapi32.dll", SetLastError = true)]
static extern bool WTSQuerySessionInformation(
    IntPtr hServer, int sessionId, WTS_INFO_CLASS infoClass,
    out IntPtr ppBuffer, out int pBytesReturned);
```

### 5.3 透明、置頂、穿透視窗

**XAML 基礎**
```xml
<Window x:Class="Watermark.Windows.WatermarkWindow"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize"
        IsHitTestVisible="False">
    <Canvas x:Name="WatermarkCanvas" />
</Window>
```

**載入後加上滑鼠穿透 / 不搶焦點**
```csharp
const int GWL_EXSTYLE = -20;
const int WS_EX_TRANSPARENT = 0x20;
const int WS_EX_TOOLWINDOW  = 0x80;
const int WS_EX_NOACTIVATE  = 0x08000000;
const int WS_EX_LAYERED     = 0x80000;

protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
    SetWindowLong(hwnd, GWL_EXSTYLE,
        ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED);
}
```

> **注意**：滑鼠穿透必須由 `WS_EX_TRANSPARENT` 提供；只靠 `IsHitTestVisible="False"` 在某些場景仍會吃事件。

### 5.4 多螢幕支援

```csharp
foreach (var screen in System.Windows.Forms.Screen.AllScreens)
{
    var w = new WatermarkWindow();
    w.Left   = screen.Bounds.Left;
    w.Top    = screen.Bounds.Top;
    w.Width  = screen.Bounds.Width;
    w.Height = screen.Bounds.Height;
    w.Show();
}
```

監聽 `SystemEvents.DisplaySettingsChanged` 處理螢幕熱插拔。

### 5.5 浮水印渲染

**斜向平鋪策略**：在 Canvas 上以一個固定格子大小（例如 320 × 180）為單位，列舉 `(col, row)`，在每格的中心放一個 `TextBlock`，整體加 `RotateTransform Angle=-30`。

**單格內容範例（多行）**：
```
DOMAIN\user @ SERVER01
192.168.1.50
2026-05-19 14:32:07
機密文件 - 禁止外流
```

**樣式建議**：
- 字體：Segoe UI 或微軟正黑體，14pt
- 顏色：`#FFFFFF`，Opacity 0.12（夠看到但不影響閱讀）
- 描邊：用 `TextBlock` + 陰影 `DropShadowEffect` 增加深淺背景上的可讀性
- 時間每 30 秒重新渲染一次

### 5.6 Watchdog

```csharp
// 主執行緒中：
var timer = new System.Threading.Timer(_ =>
{
    foreach (var w in _windows)
    {
        if (!w.IsLoaded || PresentationSource.FromVisual(w) == null)
            RecreateWindow(w);
    }
}, null, 2000, 2000);
```

搭配 `Mutex` 防止重複啟動：
```csharp
var mutex = new Mutex(true, "Global\\Watermark_SingleInstance", out bool created);
if (!created) { Shutdown(); return; }
```

### 5.7 自動啟動

寫入登錄檔（每個使用者一份）：
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    Watermark = "C:\Program Files\Watermark\Watermark.exe"
```

> 進階：改用「工作排程器」可以指定「使用者登入時、延遲 5 秒、最高權限」，比 Run 鍵更穩定。

---

## 6. 設定檔（settings.json）

```json
{
  "watermark": {
    "customText": "機密文件 - 禁止外流",
    "fontFamily": "Microsoft JhengHei",
    "fontSize": 14,
    "color": "#FFFFFF",
    "opacity": 0.12,
    "rotationAngle": -30,
    "tileWidth": 320,
    "tileHeight": 180,
    "timestampFormat": "yyyy-MM-dd HH:mm:ss",
    "timestampRefreshSeconds": 30
  },
  "behavior": {
    "showOnRdpOnly": true,
    "hideOnLock": false,
    "watchdogIntervalMs": 2000
  }
}
```

---

## 7. 浮水印視覺示意

```
   user@HOST              user@HOST              user@HOST
  192.168.1.50          192.168.1.50          192.168.1.50
 2026-05-19 14:32     2026-05-19 14:32      2026-05-19 14:32
   機密文件               機密文件               機密文件

         user@HOST              user@HOST
        192.168.1.50          192.168.1.50
       2026-05-19 14:32      2026-05-19 14:32
         機密文件               機密文件

   user@HOST              user@HOST              user@HOST
  192.168.1.50          192.168.1.50          192.168.1.50
 ...
```

整片桌面以約 -30° 斜向、半透明灰白色平鋪。

---

## 8. 開發階段

| 階段 | 目標 | 驗收標準 |
|---|---|---|
| **Phase 1** | 最小可行版本 | 寫死文字、單螢幕、永遠顯示，透明穿透 OK、斜向平鋪正確 |
| **Phase 2** | RDP 偵測 | 本機登入無顯示，RDP 連入後自動顯示，斷線後消失 |
| **Phase 3** | 動態資訊 | 真實使用者、Client IP、時間戳記每 30 秒更新 |
| **Phase 4** | 多螢幕 + 設定檔 | 所有 monitor 都覆蓋，熱插拔正常，settings.json 生效 |
| **Phase 5** | Watchdog + 自啟 | 強制關閉後 2 秒內復原，登入自動啟動，單一實例 |

---

## 9. 已知限制與風險

| 項目 | 說明 | 緩解 |
|---|---|---|
| 系統管理員可結束程序 | Task Manager 可 kill | watchdog 重啟；若要更強需 Windows Service / Driver |
| 部分截圖工具走 DWM 直接抓 | 仍會帶到浮水印 | OK，這正是我們要的 |
| 手機翻拍 | 浮水印仍可見且可讀 | 設計目的之一 |
| 全螢幕獨佔模式（遊戲、某些影片播放器） | 可能覆蓋不到 | RDP 環境下少見，可接受 |
| GPU 加速畫面（部分 RDP 設定） | 透明合成可能有閃爍 | 測試後調整 opacity / 更新頻率 |
| UAC 提示視窗 | Secure Desktop，不會被覆蓋 | 屬於系統行為，預期內 |

---

## 10. 後續可擴充項目

- **集中部署**：透過 GPO 推送、AD 整合自動帶入網域使用者資訊
- **Windows Service 版本**：跑在 session 0，難以結束
- **遠端設定**：從中央伺服器拉設定檔，可動態調整文字
- **稽核日誌**：記錄每次 RDP 連線的 user / IP / 時間到 log 或 SIEM
- **截圖事件偵測**：監聽 PrintScreen 鍵並記錄（僅參考，無法 100% 阻擋）
