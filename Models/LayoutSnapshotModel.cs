using System.Text.Json.Serialization;

namespace RestoreDesktopIcons.Models;

public class LayoutSnapshotModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DisplayTopologyModel Topology { get; set; } = new();

    public uint ViewMode { get; set; } = 1;

    public int GridSpacingX { get; set; } = 0;

    public int GridSpacingY { get; set; } = 0;

    public List<IconItemModel> Icons { get; set; } = [];

    /// <summary>
    /// 桌面截图相对存储路径 (如 screenshots/{Id}.jpg，单屏或多屏拼接全景)
    /// </summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>
    /// 针对各个物理显示器的独立高清纯净截图相对存储路径 (键: 屏幕设备名如 \\.\DISPLAY1, 值: 相对路径)
    /// </summary>
    public Dictionary<string, string> MonitorScreenshots { get; set; } = [];

    [JsonIgnore]
    public int IconCount => Icons.Count;

    [JsonIgnore]
    public string FormattedTime => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string TopologySummary => Topology.Summary;

    [JsonIgnore]
    public bool HasScreenshot => (!string.IsNullOrEmpty(ScreenshotPath) &&
        System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ScreenshotPath))) ||
        (MonitorScreenshots.Count > 0 && MonitorScreenshots.Values.Any(p => System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, p))));

    [JsonIgnore]
    public string? FullScreenshotPath => !string.IsNullOrEmpty(ScreenshotPath)
        ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ScreenshotPath)
        : null;

    public string? GetFullMonitorScreenshotPath(string deviceName)
    {
        if (MonitorScreenshots.TryGetValue(deviceName, out var relPath) && !string.IsNullOrEmpty(relPath))
        {
            var full = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath);
            if (System.IO.File.Exists(full)) return full;
        }
        return null;
    }

    public List<string> GetAllExistingScreenshotFullPaths()
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(FullScreenshotPath) && System.IO.File.Exists(FullScreenshotPath))
        {
            list.Add(FullScreenshotPath);
        }
        foreach (var rel in MonitorScreenshots.Values)
        {
            var full = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel);
            if (System.IO.File.Exists(full) && !list.Contains(full))
            {
                list.Add(full);
            }
        }
        return list;
    }
}
