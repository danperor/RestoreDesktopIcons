namespace RestoreDesktopIcons.Models;

/// <summary>
/// 应用程序全局用户设置
/// </summary>
public class AppSettingsModel
{
    /// <summary>
    /// 是否使用真实物理硬件 DWM 快照 (默认 true: 推荐，100% 物理真实画面；false: 后台静默全息重构)
    /// </summary>
    public bool UseRealDesktopCapture { get; set; } = true;

    /// <summary>
    /// 是否开启桌面图标自动隐藏功能 (默认 false)
    /// </summary>
    public bool AutoHideEnabled { get; set; } = false;

    /// <summary>
    /// 自动隐藏空闲等待时间（秒，默认 10 秒）
    /// </summary>
    public int AutoHideDelaySeconds { get; set; } = 10;

    /// <summary>
    /// 是否在点击桌面时恢复显示图标 (默认 true)
    /// </summary>
    public bool ShowIconsOnDesktopClick { get; set; } = true;

    /// <summary>
    /// 恢复桌面图标显示的鼠标触发手势 (默认 LeftDoubleClick: 鼠标左键双击防误触)
    /// </summary>
    public DesktopTriggerMode TriggerMode { get; set; } = DesktopTriggerMode.LeftDoubleClick;

    /// <summary>
    /// 关闭或最小化窗口时是否缩小到系统托盘 (默认 true)
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// 开机是否自启动 (默认 false)
    /// </summary>
    public bool StartWithWindows { get; set; } = false;
}
