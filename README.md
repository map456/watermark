# RDP Watermark

當使用者透過 RDP 連入主機時，桌面會浮現一層**透明、滑鼠穿透、置頂**的浮水印，顯示：

- 使用者 + 電腦：`DOMAIN\user @ HOSTNAME`
- RDP 來源：`ClientName (192.168.x.x)`
- 動態時間戳記：每 30 秒刷新
- 自訂標記文字：可在 `settings.json` 修改

本地（主控台）登入時**不顯示**。

支援 Windows Server **2019 / 2022**（x64）、桌面版 Windows 10 / 11。

---

## 安裝

雙擊 `dist\Watermark.msi` 安裝（需要系統管理員權限）。

安裝後：

- 程式安裝到 `C:\Program Files\Watermark\`
- 寫入 `HKLM\Software\Microsoft\Windows\CurrentVersion\Run\Watermark`
- **下一次登入時**自動啟動。若想立即啟動，直接執行：

  ```powershell
  Start-Process "C:\Program Files\Watermark\Watermark.exe"
  ```

### 靜默安裝 / 解除安裝

```powershell
msiexec /i dist\Watermark.msi /qn
msiexec /x dist\Watermark.msi /qn
```

---

## 自訂浮水印

`C:\Program Files\Watermark\settings.json` — 預設值：

```json
{
  "watermark": {
    "customText": "機密文件 - 禁止外流",
    "fontFamily": "Microsoft JhengHei",
    "fontSize": 14,
    "color": "#FFFFFF",
    "opacity": 0.18,
    "rotationAngle": -30,
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

也可放到 `%LOCALAPPDATA%\Watermark\settings.json` 做使用者層級覆寫（讀取順序：LocalAppData → ProgramData → 安裝目錄）。

修改後重新啟動 `Watermark.exe` 生效。

---

## 從原始碼建置

```powershell
# 需要 .NET 8 SDK
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.Util.wixext/5.0.2

.\build.ps1
```

產出 `dist\Watermark.msi`。

---

## 專案結構

```
Watermark/
├── DESIGN.md                ← 設計文件
├── README.md                ← 本檔
├── build.ps1                ← 一鍵建置腳本
├── src/Watermark/           ← C# WPF 原始碼
│   ├── App.xaml(.cs)        ← 進入點、單一實例、事件接線
│   ├── Windows/
│   │   └── WatermarkWindow  ← 透明穿透視窗 + 斜向平鋪
│   ├── Services/
│   │   ├── RdpSessionDetector       ← WTS session 通知
│   │   ├── SessionInfoProvider      ← User / Host / Client IP
│   │   ├── WatermarkWindowManager   ← 多螢幕管理
│   │   └── WatchdogService          ← 自我復原
│   ├── Native/NativeMethods.cs      ← P/Invoke
│   ├── Config/AppSettings.cs        ← 設定模型
│   ├── settings.json                ← 預設設定
│   ├── GlobalUsings.cs              ← WPF/WinForms 命名空間別名
│   └── app.manifest
├── installer/
│   └── Package.wxs          ← WiX v5 MSI 定義
├── publish/                 ← 發佈輸出（self-contained exe）
└── dist/                    ← MSI 輸出
```

---

## 已知限制

詳見 [`DESIGN.md`](DESIGN.md) §9。重點：

- 系統管理員仍可用 Task Manager 結束程序（watchdog 會 2 秒內復原）
- 全螢幕獨佔（DirectX exclusive）覆蓋不到
- UAC 安全桌面（Secure Desktop）不會被覆蓋
- 程式日誌：`%LOCALAPPDATA%\Watermark\watermark.log`
