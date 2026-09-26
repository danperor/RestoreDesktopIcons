using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RestoreDesktopIcons.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace RestoreDesktopIcons.Services;

[SupportedOSPlatform("windows6.0.6000")]
internal static class DesktopShellCsWin32Service
{
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private const uint FWF_AUTOARRANGE = 0x00000001;

    /// <summary>
    /// 获取当前桌面活动视图接口 IFolderView 及底层 HWND
    /// </summary>
    public static unsafe bool TryGetDesktopFolderView(out IFolderView? folderView, out IFolderView2? folderView2, out int desktopHwnd)
    {
        folderView = null;
        folderView2 = null;
        desktopHwnd = 0;

        try
        {
            Type? shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (shellWindowsType == null) return false;

            var shellWindows = (IShellWindows)Activator.CreateInstance(shellWindowsType)!;
            object loc = 0; // CSIDL_DESKTOP
            object locRoot = Type.Missing;

            object dispObj = shellWindows.FindWindowSW(
                in loc,
                in locRoot,
                ShellWindowTypeConstants.SWC_DESKTOP,
                out desktopHwnd,
                ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);

            if (dispObj is not Windows.Win32.System.Com.IServiceProvider sp) return false;

            sp.QueryService<IShellBrowser>(in SID_STopLevelBrowser, out var browser);
            if (browser == null) return false;

            browser.QueryActiveShellView(out var viewObj);
            if (viewObj == null) return false;

            folderView = viewObj as IFolderView;
            folderView2 = viewObj as IFolderView2;

            return folderView != null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("TryGetDesktopFolderView 连接 Windows 桌面 Shell COM 视图失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 获取 Windows 桌面原生视图层 (SHELLDLL_DefView) 权威窗口句柄
    /// 优先通过官方 COM IShellView::GetWindow 获取，若失败则降级为 0
    /// </summary>
    public static unsafe IntPtr GetDesktopViewHwnd()
    {
        try
        {
            Type? shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (shellWindowsType == null) return IntPtr.Zero;

            var shellWindows = (IShellWindows)Activator.CreateInstance(shellWindowsType)!;
            object loc = 0; // CSIDL_DESKTOP
            object locRoot = Type.Missing;

            object dispObj = shellWindows.FindWindowSW(
                in loc,
                in locRoot,
                ShellWindowTypeConstants.SWC_DESKTOP,
                out int dtHwnd,
                ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);

            if (dispObj is Windows.Win32.System.Com.IServiceProvider sp)
            {
                sp.QueryService<IShellBrowser>(in SID_STopLevelBrowser, out var browser);
                if (browser != null)
                {
                    browser.QueryActiveShellView(out var viewObj);
                    if (viewObj != null)
                    {
                        HWND hwnd = default;
                        viewObj.GetWindow(&hwnd);
                        if (hwnd != HWND.Null)
                        {
                            AppLogger.Info($"[COM] 成功通过 IShellView::GetWindow 获取桌面句柄: 0x{(IntPtr)hwnd.Value:X}");
                            return (IntPtr)hwnd.Value;
                        }
                    }
                }
            }
            if (dtHwnd != 0)
            {
                AppLogger.Info($"[COM] 成功通过 FindWindowSW 获取桌面句柄: 0x{dtHwnd:X}");
                return (IntPtr)dtHwnd;
            }
        }
        catch (COMException comEx) when ((uint)comEx.HResult == 0x8001010D)
        {
            // RPC_E_CANTCALLOUT_ININPUTSYNCCALL: 当前线程正在处理输入同步调用，平滑降级至原生 Win32 探测
        }
        catch (Exception ex)
        {
            AppLogger.Warn("通过 COM 获取桌面 ShellView HWND 异常", ex);
        }
        return IntPtr.Zero;
    }


    /// <summary>
    /// 检测桌面是否勾选了“自动排列图标”
    /// </summary>
    public static bool IsAutoArrangeEnabled()
    {
        if (!TryGetDesktopFolderView(out _, out var fv2, out _))
            return false;

        if (fv2 != null)
        {
            fv2.GetCurrentFolderFlags(out uint flags);
            return (flags & FWF_AUTOARRANGE) != 0;
        }

        return false;
    }

    /// <summary>
    /// 一键将副屏上的所有图标直接平移回主屏幕对应网格位置
    /// </summary>
    public static unsafe int MoveIconsFromSecondaryToPrimary(int offsetY = 1440)
    {
        if (!TryGetDesktopFolderView(out var fv, out _, out _) || fv == null)
        {
            AppLogger.Error("MoveIconsFromSecondaryToPrimary: 无法获取 Desktop FolderView");
            return 0;
        }

        fv.ItemCount(_SVGIO.SVGIO_BACKGROUND, out int count);
        AppLogger.Info($"MoveIconsFromSecondaryToPrimary: 找到 {count} 个桌面项目，目标平移 offsetY={offsetY}");
        if (count == 0) return 0;

        var apidlList = new ITEMIDLIST*[count];
        var aptList = new Point[count];
        int valid = 0;

        for (int i = 0; i < count; i++)
        {
            ITEMIDLIST* pidl = null;
            fv.Item(i, &pidl);
            if (pidl == null) continue;

            Point pt = default;
            fv.GetItemPosition(pidl, &pt);

            int targetY = pt.Y < offsetY ? pt.Y + offsetY : pt.Y;
            int targetX = pt.X;

            AppLogger.Info($"  Icon[{i}]: 原始 (X={pt.X}, Y={pt.Y}) -> 目标 (X={targetX}, Y={targetY})");

            apidlList[valid] = pidl;
            aptList[valid] = new Point(targetX, targetY);
            valid++;
        }

        fixed (ITEMIDLIST** pApidl = apidlList)
        fixed (Point* pApt = aptList)
        {
            fv.SelectAndPositionItems((uint)valid, pApidl, pApt, 0);
        }

        for (int i = 0; i < valid; i++)
        {
            PInvoke.CoTaskMemFree(apidlList[i]);
        }

        PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_FLUSHNOWAIT, null, null);
        AppLogger.Info($"MoveIconsFromSecondaryToPrimary: 完成 SelectAndPositionItems 批处理，共移动 {valid} 个图标");
        return valid;
    }

    /// <summary>
    /// 读取当前桌面所有图标的全息指纹与物理绝对坐标
    /// </summary>
    public static unsafe (List<IconItemModel> Icons, uint ViewMode, int SpacingX, int SpacingY) CaptureDesktopIcons()
    {
        var result = new List<IconItemModel>();
        uint viewMode = 1;
        int spacingX = 0;
        int spacingY = 0;

        if (!TryGetDesktopFolderView(out var folderView, out _, out _) || folderView == null)
            return (result, viewMode, spacingX, spacingY);

        try
        {
            folderView.GetCurrentViewMode(out viewMode);

            Point spacingPt = default;
            folderView.GetSpacing(&spacingPt);
            spacingX = spacingPt.X;
            spacingY = spacingPt.Y;

            folderView.ItemCount(_SVGIO.SVGIO_BACKGROUND, out int count);

            for (int i = 0; i < count; i++)
            {
                ITEMIDLIST* pidl = null;
                folderView.Item(i, &pidl);
                if (pidl == null) continue;

                try
                {
                    Point pt = default;
                    folderView.GetItemPosition(pidl, &pt);

                    string displayName = string.Empty;
                    string parsingName = string.Empty;
                    string? pidlBase64 = null;
                    string? shortcutTarget = null;

                    // 1. 抓取 ShellItem 名字
                    Guid shellItemGuid = typeof(IShellItem).GUID;
                    if (PInvoke.SHCreateItemFromIDList(pidl, &shellItemGuid, out object shellItemObj) == 0 &&
                        shellItemObj is IShellItem shellItem)
                    {
                        shellItem.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out PWSTR pszDisplay);
                        if (pszDisplay.Value != null)
                        {
                            displayName = pszDisplay.ToString();
                            PInvoke.CoTaskMemFree(pszDisplay.Value);
                        }

                        shellItem.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out PWSTR pszParsing);
                        if (pszParsing.Value != null)
                        {
                            parsingName = pszParsing.ToString();
                            PInvoke.CoTaskMemFree(pszParsing.Value);
                        }
                    }

                    // 2. 二进制 PIDL Base64
                    uint pidlSize = PInvoke.ILGetSize(pidl);
                    if (pidlSize > 0)
                    {
                        byte[] bytes = new byte[pidlSize];
                        Marshal.Copy((IntPtr)pidl, bytes, 0, (int)pidlSize);
                        pidlBase64 = Convert.ToBase64String(bytes);
                    }

                    // 3. 快捷方式真实物理目标解析
                    if (!string.IsNullOrEmpty(parsingName) && parsingName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        shortcutTarget = ShortcutResolver.ResolveShortcutTarget(parsingName);
                    }

                    result.Add(new IconItemModel
                    {
                        DisplayName = displayName,
                        ParsingName = parsingName,
                        PidlBase64 = pidlBase64,
                        ShortcutTarget = shortcutTarget,
                        X = pt.X,
                        Y = pt.Y
                    });
                }
                finally
                {
                    PInvoke.CoTaskMemFree(pidl);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("CaptureDesktopIcons 抓取桌面图标发生异常", ex);
        }

        return (result, viewMode, spacingX, spacingY);
    }

    /// <summary>
    /// 执行四维全息恢复：多重指纹比对、引力吸附防幽灵、双通道原子写入、状态树持久化冲刷
    /// </summary>
    public static unsafe (int RestoredCount, int ClampedCount, string Message) RestoreDesktopIcons(
        LayoutSnapshotModel snapshot,
        DisplayTopologyModel currentTopology)
    {
        if (!TryGetDesktopFolderView(out var folderView, out var fv2, out _) || folderView == null)
            return (0, 0, "无法连接到 Windows 桌面 Shell 视图。");

        // 1. 检查自动排列状态
        if (fv2 != null)
        {
            fv2.GetCurrentFolderFlags(out uint flags);
            if ((flags & FWF_AUTOARRANGE) != 0)
            {
                return (0, 0, "桌面已开启【自动排列图标】！请先右键桌面 -> 查看 -> 取消勾选【自动排列图标】再执行恢复。");
            }
        }

        // 同步桌面图标视图模式（大图标/中等图标/小图标）
        if (snapshot.ViewMode > 0)
        {
            try
            {
                folderView.GetCurrentViewMode(out uint currentMode);
                if (currentMode != snapshot.ViewMode)
                {
                    folderView.SetCurrentViewMode(snapshot.ViewMode);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"同步桌面图标视图模式失败 (ViewMode: {snapshot.ViewMode})", ex);
            }
        }

        // 2. 遍历当前桌面所有实时图标并建立四级指纹映射
        folderView.ItemCount(_SVGIO.SVGIO_BACKGROUND, out int currentCount);
        var liveItems = new List<(IntPtr Pidl, string Display, string Parsing, string? PidlBase64, string? Target)>();

        for (int i = 0; i < currentCount; i++)
        {
            ITEMIDLIST* pidl = null;
            folderView.Item(i, &pidl);
            if (pidl == null) continue;

            string display = string.Empty;
            string parsing = string.Empty;
            string? base64 = null;
            string? target = null;

            Guid shellItemGuid = typeof(IShellItem).GUID;
            if (PInvoke.SHCreateItemFromIDList(pidl, &shellItemGuid, out object shellItemObj) == 0 &&
                shellItemObj is IShellItem shellItem)
            {
                shellItem.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out PWSTR pszDisplay);
                if (pszDisplay.Value != null)
                {
                    display = pszDisplay.ToString();
                    PInvoke.CoTaskMemFree(pszDisplay.Value);
                }

                shellItem.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out PWSTR pszParsing);
                if (pszParsing.Value != null)
                {
                    parsing = pszParsing.ToString();
                    PInvoke.CoTaskMemFree(pszParsing.Value);
                }
            }

            uint size = PInvoke.ILGetSize(pidl);
            if (size > 0)
            {
                byte[] bytes = new byte[size];
                Marshal.Copy((IntPtr)pidl, bytes, 0, (int)size);
                base64 = Convert.ToBase64String(bytes);
            }

            if (!string.IsNullOrEmpty(parsing) && parsing.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                target = ShortcutResolver.ResolveShortcutTarget(parsing);
            }

            liveItems.Add(((IntPtr)pidl, display, parsing, base64, target));
        }

        try
        {
            // 3. 将快照记录与实时项目进行多重降级匹配
            var matchedPairs = new List<(IntPtr Pidl, int TargetX, int TargetY)>();
            var usedPidl = new HashSet<IntPtr>();
            var occupiedCoordinates = new HashSet<(int X, int Y)>();
            int clampedCount = 0;

            foreach (var savedIcon in snapshot.Icons)
            {
                // 优先级 1: 绝对 ParsingName (GUID / 绝对路径)
                var match = liveItems.FirstOrDefault(l => !usedPidl.Contains(l.Pidl) &&
                    !string.IsNullOrEmpty(savedIcon.ParsingName) &&
                    string.Equals(l.Parsing, savedIcon.ParsingName, StringComparison.OrdinalIgnoreCase));

                // 优先级 2: 二进制 PIDL Base64
                if (match.Pidl == IntPtr.Zero && !string.IsNullOrEmpty(savedIcon.PidlBase64))
                {
                    match = liveItems.FirstOrDefault(l => !usedPidl.Contains(l.Pidl) &&
                        string.Equals(l.PidlBase64, savedIcon.PidlBase64, StringComparison.Ordinal));
                }

                // 优先级 3: 快捷方式真实物理目标
                if (match.Pidl == IntPtr.Zero && !string.IsNullOrEmpty(savedIcon.ShortcutTarget))
                {
                    match = liveItems.FirstOrDefault(l => !usedPidl.Contains(l.Pidl) &&
                        string.Equals(l.Target, savedIcon.ShortcutTarget, StringComparison.OrdinalIgnoreCase));
                }

                // 优先级 4: 友好 DisplayName 兜底
                if (match.Pidl == IntPtr.Zero && !string.IsNullOrEmpty(savedIcon.DisplayName))
                {
                    match = liveItems.FirstOrDefault(l => !usedPidl.Contains(l.Pidl) &&
                        string.Equals(l.Display, savedIcon.DisplayName, StringComparison.OrdinalIgnoreCase));
                }

                if (match.Pidl != IntPtr.Zero)
                {
                    usedPidl.Add(match.Pidl);

                    // 4. 第二维：智能引力锚定防幽灵处理
                    var (clampedX, clampedY, wasClamped) = GravityClampingEngine.ClampToVisibleWorkArea(
                        savedIcon.X,
                        savedIcon.Y,
                        currentTopology,
                        snapshot.GridSpacingX,
                        snapshot.GridSpacingY);

                    if (wasClamped) clampedCount++;

                    // 5. 坐标防重叠碰撞规避 (Anti-Collision Slotting)
                    int stepY = snapshot.GridSpacingY > 20 ? snapshot.GridSpacingY : 80;
                    int stepX = snapshot.GridSpacingX > 20 ? snapshot.GridSpacingX : 80;
                    int collisionGuard = 0;
                    while (occupiedCoordinates.Contains((clampedX, clampedY)) && collisionGuard++ < 100)
                    {
                        var (nextX, nextY, _) = GravityClampingEngine.ClampToVisibleWorkArea(
                            clampedX,
                            clampedY + stepY,
                            currentTopology,
                            snapshot.GridSpacingX,
                            snapshot.GridSpacingY);

                        if (nextX == clampedX && nextY == clampedY)
                        {
                            clampedX += stepX;
                            clampedY = 16;
                        }
                        else
                        {
                            clampedX = nextX;
                            clampedY = nextY;
                        }
                    }

                    occupiedCoordinates.Add((clampedX, clampedY));
                    matchedPairs.Add((match.Pidl, clampedX, clampedY));
                }
            }

            if (matchedPairs.Count == 0)
            {
                return (0, 0, "未在当前桌面上找到快照中记录的任何图标。");
            }

            // 5. 第三维：双通道原子级批量写入
            int total = matchedPairs.Count;
            var apidlList = new ITEMIDLIST*[total];
            var aptList = new Point[total];

            for (int i = 0; i < total; i++)
            {
                apidlList[i] = (ITEMIDLIST*)matchedPairs[i].Pidl;
                aptList[i] = new Point(matchedPairs[i].TargetX, matchedPairs[i].TargetY);
            }

            fixed (ITEMIDLIST** pApidl = apidlList)
            fixed (Point* pApt = aptList)
            {
                folderView.SelectAndPositionItems((uint)total, pApidl, pApt, 0);
            }

            // 6. 触发 Shell 全局持久化冲刷，促使 Explorer 立即固化至注册表/流
            PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_FLUSHNOWAIT, null, null);

            string msg = $"成功恢复 {total} 个桌面图标！";
            if (clampedCount > 0)
            {
                msg += $" (由于屏幕分辨率或多屏变动，智能引力保护吸附了 {clampedCount} 个越界幽灵图标)";
            }

            return (total, clampedCount, msg);
        }
        catch (Exception ex)
        {
            AppLogger.Error("RestoreDesktopIcons 恢复桌面图标坐标时发生异常", ex);
            return (0, 0, $"恢复桌面图标发生异常: {ex.Message}");
        }
        finally
        {
            // 7. 严格释放所有分配的实时 PIDL
            foreach (var item in liveItems)
            {
                PInvoke.CoTaskMemFree((void*)item.Pidl);
            }
        }
    }
}
