using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Watermark.Config;

public class AppSettings
{
    [JsonPropertyName("watermark")]
    public WatermarkSettings Watermark { get; set; } = new();

    [JsonPropertyName("behavior")]
    public BehaviorSettings Behavior { get; set; } = new();

    public static AppSettings Default => new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        foreach (var path in CandidatePaths())
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(LogPath(), $"[{DateTime.Now:O}] Settings load failed from {path}: {ex.Message}\r\n"); } catch { }
            }
        }
        return Default;
    }

    private static System.Collections.Generic.IEnumerable<string> CandidatePaths()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watermark", "settings.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Watermark", "settings.json");
        yield return Path.Combine(baseDir, "settings.json");
    }

    private static string LogPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watermark");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "watermark.log");
    }
}

public class WatermarkSettings
{
    [JsonPropertyName("customText")]
    public string CustomText { get; set; } = "機密文件 - 禁止外流";

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Microsoft JhengHei";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 16;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.65;

    [JsonPropertyName("rotationAngle")]
    public double RotationAngle { get; set; } = -30;

    /// <summary>
    /// Approximate number of watermark tiles visible per screen.
    /// Set &gt; 0 to auto-derive tile size from screen dimensions (recommended).
    /// Set 0 to honor TileWidth / TileHeight explicitly.
    /// </summary>
    [JsonPropertyName("watermarksPerScreen")]
    public int WatermarksPerScreen { get; set; } = 5;

    [JsonPropertyName("tileWidth")]
    public double TileWidth { get; set; } = 360;

    [JsonPropertyName("tileHeight")]
    public double TileHeight { get; set; } = 200;

    [JsonPropertyName("timestampFormat")]
    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

    [JsonPropertyName("timestampRefreshSeconds")]
    public int TimestampRefreshSeconds { get; set; } = 30;

    [JsonPropertyName("showUserAndHost")]
    public bool ShowUserAndHost { get; set; } = true;

    [JsonPropertyName("showClientAddress")]
    public bool ShowClientAddress { get; set; } = true;

    [JsonPropertyName("showTimestamp")]
    public bool ShowTimestamp { get; set; } = true;

    [JsonPropertyName("showCustomText")]
    public bool ShowCustomText { get; set; } = true;
}

public class BehaviorSettings
{
    [JsonPropertyName("showOnRdpOnly")]
    public bool ShowOnRdpOnly { get; set; } = true;

    [JsonPropertyName("hideOnLock")]
    public bool HideOnLock { get; set; } = false;

    [JsonPropertyName("watchdogIntervalMs")]
    public int WatchdogIntervalMs { get; set; } = 2000;
}
