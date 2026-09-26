namespace RestoreDesktopIcons.Models;

/// <summary>
/// 恢复桌面图标显示的鼠标触发方式
/// </summary>
public enum DesktopTriggerMode
{
    /// <summary>
    /// 鼠标左键双击桌面恢复 (推荐防误触，类似 Fences 体验)
    /// </summary>
    LeftDoubleClick = 0,

    /// <summary>
    /// 鼠标左键单击桌面恢复 (类似原版 AutoHideDesktopIcons 默认行为)
    /// </summary>
    LeftSingleClick = 1,

    /// <summary>
    /// 鼠标任意键单击恢复 (左键、右键或中键)
    /// </summary>
    AnyClick = 2
}
