namespace RestoreDesktopIcons.Models;

public record MonitorInfoModel
{
    public string DeviceName { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }

    /// <summary>
    /// 物理屏幕绝对坐标 (Screen Coordinates)
    /// </summary>
    public RectModel Bounds { get; init; } = new(0, 0, 0, 0);

    /// <summary>
    /// 物理工作区绝对坐标 (Screen Coordinates)
    /// </summary>
    public RectModel WorkArea { get; init; } = new(0, 0, 0, 0);

    /// <summary>
    /// 映射到桌面窗口客户区的物理范围 (Desktop Window Client Coordinates)
    /// </summary>
    public RectModel ClientBounds { get; init; } = new(0, 0, 0, 0);

    /// <summary>
    /// 映射到桌面窗口客户区的工作区范围 (Desktop Window Client Coordinates)
    /// </summary>
    public RectModel ClientWorkArea { get; init; } = new(0, 0, 0, 0);

    public override string ToString() =>
        $"{DeviceName} {(IsPrimary ? "[Primary] " : "")}{Bounds.Width}x{Bounds.Height} ClientWorkArea:{ClientWorkArea}";
}
