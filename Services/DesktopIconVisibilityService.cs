using System.Runtime.Versioning;
using Microsoft.Win32;
using RestoreDesktopIcons.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 桌面图标显示/隐藏控制服务：
/// 通过向 SHELLDLL_DefView 发送 WM_COMMAND (0x7402) 切换桌面图标显隐。
/// 具备高频调用纳秒级内存缓存、HWND 有效性自动检测，以及截图前现场临时保护机制。
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal static class DesktopIconVisibilityService
{
    private const uint WM_COMMAND = 0x0111;
    private const int TOGGLE_DESKTOP_ICONS_CMD = 0x7402;

    private static HWND _cachedDefView = HWND.Null;
    private static bool _cachedVisibility = true;
    private static bool _hasInitializedCache = false;

    /// <summary>
    /// 桌面图标可见性改变时通知（布尔值表示是否可见）
    /// </summary>
    public static event Action<bool>? VisibilityChanged;

    /// <summary>
    /// 获取当前内存缓存的可见性（纳秒级内存直读，供低级鼠标钩子与实时轮询使用，彻底避免频繁注册表/COM I/O）
    /// </summary>
    public static bool CachedVisibility
    {
        get
        {
            if (!_hasInitializedCache)
            {
                _cachedVisibility = AreDesktopIconsVisible();
                _hasInitializedCache = true;
            }
            return _cachedVisibility;
        }
    }

    /// <summary>
    /// 启动预热：在主线程预先解析并缓存 SHELLDLL_DefView 句柄与桌面图标当前状态
    /// </summary>
    public static void WarmUp()
    {
        try
        {
            _ = CachedVisibility;
            _ = FindDefView();
        }
        catch { }
    }

    /// <summary>
    /// 权威物理检测：检测桌面图标当前是否可见
    /// 优先通过注册表 HideIcons 判断，降级通过 SysListView32 窗口可见性判断，并自动同步更新内存缓存
    /// </summary>
    public static bool AreDesktopIconsVisible()
    {
        bool isVisible = true;

        // 方案1: 优先检查 SysListView32 真实窗口物理可见性（毫秒级内核直取，绝无注册表写回延迟）
        try
        {
            HWND hListView = FindDesktopListView();
            if (!hListView.IsNull)
            {
                isVisible = PInvoke.IsWindowVisible(hListView);
                UpdateCache(isVisible);
                return isVisible;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("通过窗口可见性检查桌面图标状态失败", ex);
        }

        // 方案2: 降级通过注册表 HideIcons 检查
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (key != null)
            {
                object? val = key.GetValue("HideIcons");
                if (val is int hideIcons)
                {
                    isVisible = (hideIcons == 0); // 0=显示, 1=隐藏
                    UpdateCache(isVisible);
                    return isVisible;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("通过注册表检查桌面图标可见性失败", ex);
        }

        UpdateCache(isVisible);
        return isVisible;
    }

    private static void UpdateCache(bool isVisible)
    {
        if (!_hasInitializedCache || _cachedVisibility != isVisible)
        {
            _cachedVisibility = isVisible;
            _hasInitializedCache = true;
            VisibilityChanged?.Invoke(isVisible);
        }
    }

    /// <summary>
    /// 切换桌面图标显示/隐藏（等效于桌面右键菜单 -> 查看 -> 显示桌面图标）
    /// </summary>
    public static void ToggleDesktopIcons()
    {
        HWND hDefView = FindDefView();
        if (hDefView.IsNull)
        {
            AppLogger.Warn("ToggleDesktopIcons: 未能定位 SHELLDLL_DefView 句柄");
            return;
        }

        bool oldState = CachedVisibility;
        bool newState = !oldState;

        PInvoke.SendMessage(hDefView, WM_COMMAND, (WPARAM)(nuint)TOGGLE_DESKTOP_ICONS_CMD, (LPARAM)0);

        // 立即同步内存缓存为切换后的新状态，杜绝注册表异步延迟导致的重复翻转竞态
        UpdateCache(newState);
        AppLogger.Info($"已向 SHELLDLL_DefView 发送 WM_COMMAND(0x7402)，桌面图标显隐切换为: {(newState ? "显示" : "隐藏")}");
    }

    /// <summary>
    /// 强制显示桌面图标（如果当前已隐藏）
    /// </summary>
    public static void ShowDesktopIcons()
    {
        if (!CachedVisibility)
        {
            ToggleDesktopIcons();
            AppLogger.Info("桌面图标已从隐藏状态恢复为显示");
        }
    }

    /// <summary>
    /// 强制隐藏桌面图标（如果当前已显示）
    /// </summary>
    public static void HideDesktopIcons()
    {
        if (CachedVisibility)
        {
            ToggleDesktopIcons();
            AppLogger.Info("桌面图标已从显示状态切换为隐藏");
        }
    }

    /// <summary>
    /// 截图安全保障：若桌面图标被隐藏，则先恢复显示并等待 Shell 刷新完毕，
    /// 返回 true 表示做了临时恢复（调用方截完图后应调用 RestoreHiddenStateAfterCapture 恢复原状）
    /// </summary>
    public static bool EnsureIconsVisibleForCapture()
    {
        if (CachedVisibility)
        {
            return false; // 本来就可见，无需干预
        }

        AppLogger.Info("[截图安全] 检测到桌面图标处于隐藏状态，临时恢复显示以确保截图包含图标...");
        ToggleDesktopIcons();

        // 等待 Shell 完成图标渲染刷新 (给 Explorer 足够的重绘时间)
        Thread.Sleep(500);

        return true; // 返回 true 表示做了临时恢复
    }

    /// <summary>
    /// 截图完成后恢复隐藏状态（与 EnsureIconsVisibleForCapture 配对使用）
    /// </summary>
    public static void RestoreHiddenStateAfterCapture(bool wasHidden)
    {
        if (wasHidden)
        {
            AppLogger.Info("[截图安全] 截图完成，恢复桌面图标为隐藏状态");
            HideDesktopIcons();
        }
    }

    /// <summary>
    /// 定位 SHELLDLL_DefView 句柄（带 IsWindow 有效性校验的高速内存缓存）
    /// </summary>
    private static unsafe HWND FindDefView()
    {
        // 快速路径：若缓存句柄依然有效，直接返回（毫秒级变为纳秒级，免去重复 COM 开销）
        if (_cachedDefView != HWND.Null && PInvoke.IsWindow(_cachedDefView))
        {
            return _cachedDefView;
        }

        EnsureDesktopAccess();

        // 1. 优先使用原生 Win32 超高速内存探测 (Progman -> DefView，耗时 < 0.01ms)
        HWND hProgman = PInvoke.FindWindow("Progman", null);
        AppLogger.Info($"[FindDefView] Step1 hProgman: 0x{(IntPtr)hProgman.Value:X}");
        if (!hProgman.IsNull)
        {
            HWND def = PInvoke.FindWindowEx(hProgman, HWND.Null, "SHELLDLL_DefView", null);
            AppLogger.Info($"[FindDefView] Step1 def in Progman: 0x{(IntPtr)def.Value:X}");
            if (!def.IsNull)
            {
                _cachedDefView = def;
                return _cachedDefView;
            }
        }

        // 2. WorkerW 遍历 (耗时 < 0.05ms)
        HWND foundDef = HWND.Null;
        PInvoke.EnumWindows((hWnd, lParam) =>
        {
            HWND def = PInvoke.FindWindowEx(hWnd, HWND.Null, "SHELLDLL_DefView", null);
            if (!def.IsNull)
            {
                foundDef = def;
                return false;
            }
            return true;
        }, 0);
        AppLogger.Info($"[FindDefView] Step2 foundDef in WorkerW: 0x{(IntPtr)foundDef.Value:X}");

        if (!foundDef.IsNull)
        {
            _cachedDefView = foundDef;
            return _cachedDefView;
        }

        // 3. 兜底使用 COM 官方接口 (仅在原生探测不到的极端环境下激活，免去启动阶段跨进程 COM 阻塞)
        try
        {
            IntPtr comHwnd = DesktopShellCsWin32Service.GetDesktopViewHwnd();
            AppLogger.Info($"[FindDefView] Step3 comHwnd: 0x{comHwnd:X}");
            if (comHwnd != IntPtr.Zero)
            {
                _cachedDefView = (HWND)comHwnd;
                return _cachedDefView;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("[FindDefView] Step3 COM failed", ex);
        }

        _cachedDefView = foundDef;
        return _cachedDefView;
    }

    /// <summary>
    /// 定位桌面 SysListView32 句柄 (SHELLDLL_DefView 的子窗口)
    /// </summary>
    private static unsafe HWND FindDesktopListView()
    {
        HWND hDefView = FindDefView();
        if (hDefView.IsNull) return HWND.Null;

        return PInvoke.FindWindowEx(hDefView, HWND.Null, "SysListView32", null);
    }

    private static void EnsureDesktopAccess()
    {
        try
        {
            var hWinSta = PInvoke.OpenWindowStation("WinSta0", false, 0x10000000);
            if (!hWinSta.IsInvalid) PInvoke.SetProcessWindowStation(hWinSta);

            var hDesk = PInvoke.OpenDesktop("Default", 0, false, 0x10000000);
            if (!hDesk.IsInvalid) PInvoke.SetThreadDesktop(hDesk);
        }
        catch { }
    }
}
