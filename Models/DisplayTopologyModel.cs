using System.Text.Json.Serialization;

namespace RestoreDesktopIcons.Models;

public record DisplayTopologyModel
{
    public RectModel VirtualScreen { get; init; } = new(0, 0, 0, 0);
    public List<MonitorInfoModel> Monitors { get; init; } = [];

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (Monitors.Count == 0) return $"{VirtualScreen.Width}x{VirtualScreen.Height}";
            if (Monitors.Count == 1)
            {
                var m = Monitors[0];
                return $"{m.Bounds.Width}x{m.Bounds.Height} (单屏)";
            }
            return string.Join(" + ", Monitors.Select(m => $"{m.Bounds.Width}x{m.Bounds.Height}{(m.IsPrimary ? "*" : "")}")) + $" (共{Monitors.Count}屏)";
        }
    }
}
