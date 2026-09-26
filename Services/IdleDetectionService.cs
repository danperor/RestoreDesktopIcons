using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 系统空闲时间检测服务：
/// 调用 Win32 GetLastInputInfo 实时获取用户键鼠最后一次活动后的空闲毫秒数。
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class IdleDetectionService
{
    /// <summary>
    /// 获取自上次用户键盘或鼠标输入以来经历的空闲时间（毫秒）
    /// </summary>
    public static unsafe uint GetIdleTimeMilliseconds()
    {
        LASTINPUTINFO lii = default;
        lii.cbSize = (uint)sizeof(LASTINPUTINFO);

        if (PInvoke.GetLastInputInfo(ref lii))
        {
            uint currentTick = (uint)Environment.TickCount;
            // 使用模 2^32 无符号溢出算术，天然免疫系统连续开机 49.7 天后的 GetTickCount 溢出翻转
            return unchecked(currentTick - lii.dwTime);
        }

        return 0;
    }

    /// <summary>
    /// 获取系统空闲时间（秒）
    /// </summary>
    public static uint GetIdleTimeSeconds()
    {
        return GetIdleTimeMilliseconds() / 1000;
    }
}
