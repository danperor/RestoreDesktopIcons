using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using RestoreDesktopIcons.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.Storage.Xps;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 桌面快照采集引擎类型
/// </summary>
public enum DesktopCaptureEngine
{
    /// <summary>
    /// 引擎 1：真实物理 DWM 快照 (推荐，100% 物理真实尺寸、阴影与壁纸，毫秒级瞬间曝光抓取)
    /// </summary>
    RealHardwareDwm = 1,

    /// <summary>
    /// 引擎 2：纯净全息重构 (保留方案，后台静默重绘壁纸与高清图标，零闪烁、不最小化应用)
    /// </summary>
    SyntheticHologram = 2
}

/// <summary>
/// 屏幕与桌面视觉快照服务：
/// 1. 真实物理 DWM 引擎：100% 原生还原桌面物理像素（图标尺寸、文字阴影、壁纸与真实视觉 1:1 一致）；
/// 2. 纯净全息重构引擎：后台完全静默重绘，零闪烁、不遮挡；
/// 3. 物理多显示器独立采集：按主屏、副屏独立生成高清截图并横向并排缝合全景。
/// </summary>
public static class ScreenCaptureService
{
    private static readonly string ScreenshotsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");

    /// <summary>
    /// 物理多显示器桌面视觉快照主入口（支持双引擎调度）
    /// </summary>
    public static (string? PanoramicPath, Dictionary<string, string> MonitorPaths) CaptureDesktopMonitors(
        Guid snapshotId,
        DisplayTopologyModel topology,
        List<IconItemModel> icons,
        DesktopCaptureEngine engine = DesktopCaptureEngine.RealHardwareDwm)
    {
        if (engine == DesktopCaptureEngine.RealHardwareDwm)
        {
            try
            {
                return CaptureRealDesktopMonitors(snapshotId, topology, icons);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("真实物理 DWM 快照采集异常，平滑降级至纯净全息重构引擎", ex);
                return CaptureSyntheticDesktopMonitors(snapshotId, topology, icons);
            }
        }

        return CaptureSyntheticDesktopMonitors(snapshotId, topology, icons);
    }

    /// <summary>
    /// 【引擎 1】原生桌面图层穿透直取引擎 (Native Desktop Layer Direct Capture)：
    /// 直接请求 Windows 桌面底层视图 (SHELLDLL_DefView / SysListView32) 将原生壁纸与图标渲染至内存 DC。
    /// 无论前台打开了多少全屏应用窗口，100% 呈现纯净原生桌面，零窗口遮挡、零窗口最小化、零屏幕闪烁！
    /// </summary>
    private static (string? PanoramicPath, Dictionary<string, string> MonitorPaths) CaptureRealDesktopMonitors(
        Guid snapshotId,
        DisplayTopologyModel topology,
        List<IconItemModel> icons)
    {
        (string? PanoramicPath, Dictionary<string, string> MonitorPaths) result = (null, new());
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var hWinSta = PInvoke.OpenWindowStation("WinSta0", false, 0x10000000);
                if (!hWinSta.IsInvalid) PInvoke.SetProcessWindowStation(hWinSta);

                using var hDesk = PInvoke.OpenDesktop("Default", 0, false, 0x10000000);
                if (!hDesk.IsInvalid) PInvoke.SetThreadDesktop(hDesk);

                result = DoCaptureRealDesktopMonitors(snapshotId, topology, icons);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        // 严格 3 秒硬超时保护，彻底杜绝任何卡死或无响应风险
        bool finished = thread.Join(TimeSpan.FromSeconds(3));
        if (!finished)
        {
            AppLogger.Warn("【原生图层穿透直取】执行超过 3 秒，强制超时降级至全息重构引擎以防死锁卡顿");
            try { thread.Interrupt(); } catch { }
            return CaptureSyntheticDesktopMonitors(snapshotId, topology, icons);
        }

        if (threadEx != null) throw threadEx;
        return result;
    }


    private static (string? PanoramicPath, Dictionary<string, string> MonitorPaths) DoCaptureRealDesktopMonitors(
        Guid snapshotId,
        DisplayTopologyModel topology,
        List<IconItemModel> icons)
    {
        var monitorPaths = new Dictionary<string, string>();
        string? panoramicPath = null;

        if (!Directory.Exists(ScreenshotsDir))
        {
            Directory.CreateDirectory(ScreenshotsDir);
        }

        if (topology.Monitors.Count == 0)
        {
            panoramicPath = CaptureVirtualScreen(snapshotId, topology);
            return (panoramicPath, monitorPaths);
        }

        HWND hDefView = GetDesktopDefViewHandle();
        if (hDefView.IsNull)
        {
            throw new InvalidOperationException("未能定位到 Windows 桌面底层图层窗口 (SHELLDLL_DefView)");
        }

        PInvoke.GetWindowRect(hDefView, out RECT rDef);
        int totalW = rDef.Width;
        int totalH = rDef.Height;
        if (totalW <= 0 || totalH <= 0)
        {
            totalW = topology.VirtualScreen.Width;
            totalH = topology.VirtualScreen.Height;
        }

        AppLogger.Info($"[原生图层直取] 已定位桌面图层 HWND: {hDefView}, 渲染尺寸: {totalW}x{totalH}");

        // 1. 直接请求 Windows Shell 桌面层将自身渲染至内存高清位图（绝不最小化应用，绝不抓取前台窗口）
        using var fullBmp = new Bitmap(totalW, totalH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(fullBmp))
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT (0x00000002)
                bool pwOk = PInvoke.PrintWindow(hDefView, (HDC)hdc, (PRINT_WINDOW_FLAGS)2);
                int err2 = Marshal.GetLastWin32Error();
                AppLogger.Info($"[原生图层直取] PrintWindow({hDefView}, flag=2) 返回: {pwOk}, LastError={err2}");

                if (!pwOk)
                {
                    pwOk = PInvoke.PrintWindow(hDefView, (HDC)hdc, (PRINT_WINDOW_FLAGS)0);
                    int err0 = Marshal.GetLastWin32Error();
                    AppLogger.Info($"[原生图层直取] PrintWindow({hDefView}, flag=0) 返回: {pwOk}, LastError={err0}");
                }

                if (!pwOk)
                {
                    // 尝试对其父窗口 (WorkerW / Progman) 调用 PrintWindow
                    HWND hParent = PInvoke.GetParent(hDefView);
                    if (!hParent.IsNull)
                    {
                        pwOk = PInvoke.PrintWindow(hParent, (HDC)hdc, (PRINT_WINDOW_FLAGS)2);
                        int errP = Marshal.GetLastWin32Error();
                        AppLogger.Info($"[原生图层直取] PrintWindow 父窗口 ({hParent}, flag=2) 返回: {pwOk}, LastError={errP}");
                    }
                }

                if (!pwOk)
                {
                    throw new InvalidOperationException($"PrintWindow 对 SHELLDLL_DefView ({hDefView}) 调用返回失败 (LastError={err2})");
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        // 验证抓取到的位图是否有效（防止偶发抓取到全黑/全透明表面）
        if (IsBitmapBlank(fullBmp))
        {
            throw new InvalidOperationException("PrintWindow 抓取完成但图像表面为全透明/空白，触发降级至全息重构引擎");
        }

        var monitorBitmaps = new List<(MonitorInfoModel Monitor, Bitmap Bmp)>();

        // 2. 根据各物理显示器的真实拓扑坐标从全景图中独立裁剪
        foreach (var m in topology.Monitors)
        {
            int mW = m.Bounds.Width;
            int mH = m.Bounds.Height;
            if (mW <= 0 || mH <= 0) continue;

            int cropX = m.Bounds.Left - topology.VirtualScreen.Left;
            int cropY = m.Bounds.Top - topology.VirtualScreen.Top;

            cropX = Math.Clamp(cropX, 0, Math.Max(0, totalW - mW));
            cropY = Math.Clamp(cropY, 0, Math.Max(0, totalH - mH));

            var monitorBmp = new Bitmap(mW, mH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(monitorBmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                g.DrawImage(fullBmp,
                    new Rectangle(0, 0, mW, mH),
                    new Rectangle(cropX, cropY, mW, mH),
                    GraphicsUnit.Pixel);
            }

            string safeDev = m.DeviceName.Replace(@"\\.\", "").Replace(@"\", "_").Replace("/", "_");
            string monitorFileName = $"{snapshotId}_{safeDev}.jpg";
            string monitorFullPath = Path.Combine(ScreenshotsDir, monitorFileName);
            SaveJpeg(monitorBmp, monitorFullPath, 88L);

            string relMonitorPath = Path.Combine("screenshots", monitorFileName);
            monitorPaths[m.DeviceName] = relMonitorPath;
            AppLogger.Info($"[原生图层直取] 成功截取物理屏幕: {monitorFullPath} ({m.DeviceName}, {mW}x{mH})");

            monitorBitmaps.Add((m, monitorBmp));
        }

        // 3. 生成多屏幕横向并排无黑边全景图
        panoramicPath = BuildPanoramicImage(snapshotId, monitorBitmaps, monitorPaths);
        return (panoramicPath, monitorPaths);
    }

    /// <summary>
    /// 定位 Windows 桌面底层图层窗口句柄 (SHELLDLL_DefView)
    /// 1. 优先通过官方 COM IShellView::GetWindow 获取；
    /// 2. 次选 Progman -> SHELLDLL_DefView；
    /// 3. 最后遍历 WorkerW 等顶级窗口寻找 SHELLDLL_DefView。
    /// </summary>
    private static HWND GetDesktopDefViewHandle()
    {
        // 1. COM IShellView 权威直取
        try
        {
            IntPtr comHwnd = DesktopShellCsWin32Service.GetDesktopViewHwnd();
            if (comHwnd != IntPtr.Zero)
            {
                return (HWND)comHwnd;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("COM 获取桌面句柄尝试失败，转入 Win32 探测", ex);
        }

        // 2. Progman 直接子窗口
        HWND hProgman = PInvoke.FindWindow("Progman", null);
        if (!hProgman.IsNull)
        {
            HWND def = PInvoke.FindWindowEx(hProgman, HWND.Null, "SHELLDLL_DefView", null);
            if (!def.IsNull) return def;
        }

        // 3. WorkerW 等顶级窗口遍历
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

        return foundDef;
    }


    /// <summary>
    /// 【引擎 2】纯净全息重构引擎（完整保留并升级 DPI 感知）：
    /// 提取系统原生物理壁纸与 Shell PIDL 高清图标，免疫任何前台遮挡，纯后台静默绘制。
    /// </summary>
    private static (string? PanoramicPath, Dictionary<string, string> MonitorPaths) CaptureSyntheticDesktopMonitors(
        Guid snapshotId,
        DisplayTopologyModel topology,
        List<IconItemModel> icons)
    {
        var monitorPaths = new Dictionary<string, string>();
        string? panoramicPath = null;

        try
        {
            if (!Directory.Exists(ScreenshotsDir))
            {
                Directory.CreateDirectory(ScreenshotsDir);
            }

            if (topology.Monitors.Count == 0)
            {
                panoramicPath = CaptureVirtualScreen(snapshotId, topology);
                return (panoramicPath, monitorPaths);
            }

            // 1. 获取当前系统桌面壁纸
            using var wallpaperImage = LoadCurrentWallpaper();

            // 2. 缓存所有图标的位图，避免同一程序或默认图标重复提取
            var iconCache = new Dictionary<string, Bitmap>();
            var defaultAppIcon = SystemIcons.Application.ToBitmap();

            try
            {
                var monitorBitmaps = new List<(MonitorInfoModel Monitor, Bitmap Bmp)>();

                // 3. 针对每个物理显示器分别绘制专属纯净桌面
                foreach (var m in topology.Monitors)
                {
                    int mW = m.Bounds.Width;
                    int mH = m.Bounds.Height;
                    if (mW <= 0 || mH <= 0) continue;

                    var monitorBmp = new Bitmap(mW, mH, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(monitorBmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                        // 绘制壁纸
                        if (wallpaperImage != null)
                        {
                            g.DrawImage(wallpaperImage, new Rectangle(0, 0, mW, mH));
                        }
                        else
                        {
                            g.Clear(Color.FromArgb(0, 120, 215));
                        }

                        // DPI 比例感知计算，改善高分屏下图标与文字过小的视觉差异
                        float dpiScale = Math.Max(1.0f, mH / 1000f);
                        int iconDrawSize = 32;
                        if (dpiScale >= 1.6f) iconDrawSize = 64;
                        else if (dpiScale >= 1.25f) iconDrawSize = 48;

                        float fontSize = Math.Clamp(9.0f * (dpiScale >= 1.25f ? 1.2f : 1.0f), 9.0f, 12.0f);
                        using var font = new Font("Segoe UI", fontSize, FontStyle.Regular);
                        using var textBrush = new SolidBrush(Color.White);
                        using var shadowBrush = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
                        var sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Near,
                            Trimming = StringTrimming.EllipsisCharacter
                        };

                        foreach (var icon in icons)
                        {
                            int relX = icon.X - m.ClientBounds.Left;
                            int relY = icon.Y - m.ClientBounds.Top;

                            if (relX >= -64 && relX < mW && relY >= -64 && relY < mH)
                            {
                                Bitmap iconBmp = GetIconBitmap(icon, iconCache, defaultAppIcon);

                                int iconDrawX = relX + Math.Max(0, (72 - iconDrawSize) / 2);
                                int iconDrawY = relY;
                                g.DrawImage(iconBmp, iconDrawX, iconDrawY, iconDrawSize, iconDrawSize);

                                var textRect = new RectangleF(iconDrawX + iconDrawSize / 2f - 54, relY + iconDrawSize + 4, 108, 48);
                                g.DrawString(icon.DisplayName, font, shadowBrush, new RectangleF(textRect.X - 1, textRect.Y, textRect.Width, textRect.Height), sf);
                                g.DrawString(icon.DisplayName, font, shadowBrush, new RectangleF(textRect.X + 1, textRect.Y, textRect.Width, textRect.Height), sf);
                                g.DrawString(icon.DisplayName, font, shadowBrush, new RectangleF(textRect.X, textRect.Y - 1, textRect.Width, textRect.Height), sf);
                                g.DrawString(icon.DisplayName, font, shadowBrush, new RectangleF(textRect.X, textRect.Y + 1, textRect.Width, textRect.Height), sf);
                                g.DrawString(icon.DisplayName, font, textBrush, textRect, sf);
                            }
                        }
                    }

                    // 保存该物理屏幕独立截图
                    string safeDev = m.DeviceName.Replace(@"\\.\", "").Replace(@"\", "_").Replace("/", "_");
                    string monitorFileName = $"{snapshotId}_{safeDev}.jpg";
                    string monitorFullPath = Path.Combine(ScreenshotsDir, monitorFileName);
                    SaveJpeg(monitorBmp, monitorFullPath, 85L);

                    string relMonitorPath = Path.Combine("screenshots", monitorFileName);
                    monitorPaths[m.DeviceName] = relMonitorPath;
                    AppLogger.Info($"[全息重构] 已生成物理屏幕纯净截图: {monitorFullPath} ({m.DeviceName}, {mW}x{mH})");

                    monitorBitmaps.Add((m, monitorBmp));
                }

                // 4. 生成多屏幕横向并排无黑边全景图
                panoramicPath = BuildPanoramicImage(snapshotId, monitorBitmaps, monitorPaths);
            }
            finally
            {
                defaultAppIcon.Dispose();
                foreach (var bmp in iconCache.Values)
                {
                    bmp.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("纯净全息重构视觉采集异常", ex);
        }

        return (panoramicPath, monitorPaths);
    }

    /// <summary>
    /// 多监视器横向并排缝合全景图（附深色科技质感屏幕标签徽标）
    /// </summary>
    private static string? BuildPanoramicImage(
        Guid snapshotId,
        List<(MonitorInfoModel Monitor, Bitmap Bmp)> monitorBitmaps,
        Dictionary<string, string> monitorPaths)
    {
        string? panoramicPath = null;

        try
        {
            if (monitorBitmaps.Count == 1)
            {
                panoramicPath = monitorPaths.Values.FirstOrDefault();
            }
            else if (monitorBitmaps.Count > 1)
            {
                var sortedList = monitorBitmaps.OrderBy(pair => pair.Monitor.Bounds.Left).ToList();
                int bannerHeight = 36;
                int spacing = 12;
                int totalW = sortedList.Sum(pair => pair.Bmp.Width) + spacing * (sortedList.Count + 1);
                int maxH = sortedList.Max(pair => pair.Bmp.Height) + bannerHeight + spacing * 2;

                using var panoBmp = new Bitmap(totalW, maxH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(panoBmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    // 深色科技质感底色
                    g.Clear(Color.FromArgb(28, 30, 33));

                    using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
                    using var titleBrush = new SolidBrush(Color.White);
                    using var headerBgBrush = new SolidBrush(Color.FromArgb(45, 52, 60));
                    using var borderPen = new Pen(Color.FromArgb(70, 78, 88), 2);

                    int curX = spacing;
                    foreach (var (m, bmp) in sortedList)
                    {
                        string roleTag = m.IsPrimary ? "【主屏幕】" : "【副屏幕】";
                        string screenTitle = $"{roleTag} {m.DeviceName} ({bmp.Width}x{bmp.Height})";

                        int screenY = bannerHeight + spacing;

                        // 绘制屏幕标题栏背景
                        g.FillRectangle(headerBgBrush, curX, spacing, bmp.Width, bannerHeight - 4);
                        g.DrawString(screenTitle, titleFont, titleBrush, curX + 12, spacing + 6);

                        // 绘制屏幕画面
                        g.DrawImage(bmp, curX, screenY, bmp.Width, bmp.Height);
                        g.DrawRectangle(borderPen, curX, screenY, bmp.Width, bmp.Height);

                        curX += bmp.Width + spacing;
                    }
                }

                string panoFileName = $"{snapshotId}.jpg";
                string panoFullPath = Path.Combine(ScreenshotsDir, panoFileName);
                SaveJpeg(panoBmp, panoFullPath, 85L);
                panoramicPath = Path.Combine("screenshots", panoFileName);
                AppLogger.Info($"已生成多屏幕横向并排全景图: {panoFullPath} (总尺寸: {totalW}x{maxH})");
            }
        }
        finally
        {
            foreach (var pair in monitorBitmaps)
            {
                pair.Bmp.Dispose();
            }
        }

        return panoramicPath;
    }

    /// <summary>
    /// 加载当前系统桌面壁纸
    /// </summary>
    private static Image? LoadCurrentWallpaper()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string transcoded = Path.Combine(appData, @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded))
            {
                using var fs = new FileStream(transcoded, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Image.FromStream(fs);
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string? regWp = key?.GetValue("Wallpaper") as string;
            if (!string.IsNullOrEmpty(regWp) && File.Exists(regWp))
            {
                using var fs = new FileStream(regWp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Image.FromStream(fs);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("读取系统桌面壁纸文件发生异常", ex);
        }

        return null;
    }

    /// <summary>
    /// 获取或提取图标对应的高保真图像
    /// </summary>
    private static Bitmap GetIconBitmap(IconItemModel item, Dictionary<string, Bitmap> cache, Bitmap defaultFallback)
    {
        string cacheKey = !string.IsNullOrEmpty(item.ParsingName) ? item.ParsingName : item.DisplayName;
        if (cache.TryGetValue(cacheKey, out var existing))
        {
            return existing;
        }

        Bitmap? extracted = null;

        if (!string.IsNullOrEmpty(item.ParsingName) && (File.Exists(item.ParsingName) || Directory.Exists(item.ParsingName)))
        {
            extracted = ExtractShellIcon(item.ParsingName);
        }

        if (extracted == null && !string.IsNullOrEmpty(item.ShortcutTarget) && File.Exists(item.ShortcutTarget))
        {
            extracted = ExtractShellIcon(item.ShortcutTarget);
        }

        if (extracted == null)
        {
            if (item.DisplayName.Contains("回收站"))
            {
                extracted = ExtractShellIcon(@"C:\Windows\System32\imageres.dll") ?? defaultFallback;
            }
            else if (item.DisplayName.Contains("此电脑"))
            {
                extracted = ExtractShellIcon(@"C:\Windows\System32\imageres.dll") ?? defaultFallback;
            }
        }

        var result = extracted ?? defaultFallback;
        if (extracted != null)
        {
            cache[cacheKey] = extracted;
        }

        return result;
    }

    private static Bitmap? ExtractShellIcon(string path)
    {
        try
        {
            var shinfo = new SHFILEINFOW();
            nuint res = PInvoke.SHGetFileInfo(
                path,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON);
            if (res != 0 && !shinfo.hIcon.IsNull)
            {
                try
                {
                    using var ico = Icon.FromHandle(shinfo.hIcon);
                    return ico.ToBitmap();
                }
                finally
                {
                    PInvoke.DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 保留 GDI 穿透全虚拟屏幕截屏能力作为降级备用
    /// </summary>
    public static unsafe string? CaptureVirtualScreen(Guid snapshotId, DisplayTopologyModel topology)
    {
        try
        {
            if (!Directory.Exists(ScreenshotsDir))
            {
                Directory.CreateDirectory(ScreenshotsDir);
            }

            var vs = topology.VirtualScreen;
            if (vs.Width <= 0 || vs.Height <= 0)
                return null;

            string fileName = $"{snapshotId}.jpg";
            string fullPath = Path.Combine(ScreenshotsDir, fileName);

            HDC hDeskDC = PInvoke.GetDC(HWND.Null);
            if (hDeskDC.IsNull) return null;

            try
            {
                HDC hMemDC = PInvoke.CreateCompatibleDC(hDeskDC);
                if (hMemDC.IsNull) return null;

                try
                {
                    HBITMAP hBmp = PInvoke.CreateCompatibleBitmap(hDeskDC, vs.Width, vs.Height);
                    if (hBmp.IsNull) return null;

                    try
                    {
                        HGDIOBJ hOld = PInvoke.SelectObject(hMemDC, new HGDIOBJ(hBmp.Value));
                        PInvoke.BitBlt(hMemDC, 0, 0, vs.Width, vs.Height, hDeskDC, vs.Left, vs.Top, (ROP_CODE)((uint)ROP_CODE.SRCCOPY | 0x40000000));
                        PInvoke.SelectObject(hMemDC, hOld);

                        using var bitmap = Image.FromHbitmap((IntPtr)hBmp.Value);
                        SaveJpeg(bitmap, fullPath, 85L);
                        return Path.Combine("screenshots", fileName);
                    }
                    finally
                    {
                        PInvoke.DeleteObject(new HGDIOBJ(hBmp.Value));
                    }
                }
                finally
                {
                    PInvoke.DeleteDC(hMemDC);
                }
            }
            finally
            {
                PInvoke.ReleaseDC(HWND.Null, hDeskDC);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("截取桌面全景屏幕失败 (降级备用)", ex);
            return null;
        }
    }

    /// <summary>
    /// 清理快照关联的所有物理分屏与全景截图
    /// </summary>
    public static void DeleteSnapshotScreenshots(LayoutSnapshotModel snapshot)
    {
        foreach (var fullPath in snapshot.GetAllExistingScreenshotFullPaths())
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    AppLogger.Info($"已清理删除快照截图: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"清理快照截图文件失败: {fullPath}", ex);
            }
        }
    }

    public static void DeleteScreenshot(string? relativeOrFullPath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrFullPath)) return;

        try
        {
            string fullPath = Path.IsPathRooted(relativeOrFullPath)
                ? relativeOrFullPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrFullPath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                AppLogger.Info($"已清理删除快照截图: {fullPath}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"清理快照截图失败: {relativeOrFullPath}", ex);
        }
    }

    private static void SaveJpeg(Image image, string path, long quality)
    {
        var encoder = GetEncoder(ImageFormat.Jpeg);
        if (encoder != null)
        {
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            image.Save(path, encoder, encoderParams);
        }
        else
        {
            image.Save(path, ImageFormat.Jpeg);
        }
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
    }

    /// <summary>
    /// 采样检测位图是否为全黑/全透明空白表面（防止 PrintWindow 异常表面存盘）
    /// </summary>
    private static bool IsBitmapBlank(Bitmap bmp)
    {
        try
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return true;

            Color c1 = bmp.GetPixel(Math.Min(10, w - 1), Math.Min(10, h - 1));
            Color c2 = bmp.GetPixel(Math.Max(0, w - 10), Math.Min(10, h - 1));
            Color c3 = bmp.GetPixel(Math.Min(10, w - 1), Math.Max(0, h - 10));
            Color c4 = bmp.GetPixel(Math.Max(0, w - 10), Math.Max(0, h - 10));
            Color c5 = bmp.GetPixel(w / 2, h / 2);

            return c1.ToArgb() == 0 && c2.ToArgb() == 0 && c3.ToArgb() == 0 && c4.ToArgb() == 0 && c5.ToArgb() == 0;
        }
        catch
        {
            return false;
        }
    }
}

