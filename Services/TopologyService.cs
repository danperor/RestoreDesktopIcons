using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RestoreDesktopIcons.Models;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RestoreDesktopIcons.Services;

[SupportedOSPlatform("windows5.1.2600")]
public static class TopologyService
{
    public static unsafe DisplayTopologyModel GetCurrentTopology()
    {
        int vx = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        int vy = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        int vcx = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        int vcy = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        var virtualScreen = new RectModel(vx, vy, vx + vcx, vy + vcy);
        var monitors = new List<MonitorInfoModel>();

        try
        {
            PInvoke.EnumDisplayMonitors(default, null, (hMonitor, hdc, lprc, lParam) =>
            {
                var mi = new MONITORINFOEXW();
                mi.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

                if (PInvoke.GetMonitorInfo(hMonitor, (MONITORINFO*)&mi))
                {
                    string devName = mi.szDevice.ToString();
                    bool isPrimary = (mi.monitorInfo.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY = 1

                    var bounds = new RectModel(
                        mi.monitorInfo.rcMonitor.left,
                        mi.monitorInfo.rcMonitor.top,
                        mi.monitorInfo.rcMonitor.right,
                        mi.monitorInfo.rcMonitor.bottom);

                    var workArea = new RectModel(
                        mi.monitorInfo.rcWork.left,
                        mi.monitorInfo.rcWork.top,
                        mi.monitorInfo.rcWork.right,
                        mi.monitorInfo.rcWork.bottom);

                    // 核心关键转换：将屏幕全局坐标转化为 Explorer 桌面视图客户区坐标 (Client Coordinates)
                    // 桌面客户区原点 (0,0) 对应于虚拟屏幕 (vx, vy)
                    var clientBounds = new RectModel(
                        bounds.Left - vx,
                        bounds.Top - vy,
                        bounds.Right - vx,
                        bounds.Bottom - vy);

                    var clientWorkArea = new RectModel(
                        workArea.Left - vx,
                        workArea.Top - vy,
                        workArea.Right - vx,
                        workArea.Bottom - vy);

                    monitors.Add(new MonitorInfoModel
                    {
                        DeviceName = devName,
                        IsPrimary = isPrimary,
                        Bounds = bounds,
                        WorkArea = workArea,
                        ClientBounds = clientBounds,
                        ClientWorkArea = clientWorkArea
                    });
                }
                return true;
            }, 0);

            monitors.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        }
        catch (Exception ex)
        {
            AppLogger.Error("获取显示器拓扑结构发生异常", ex);
        }

        return new DisplayTopologyModel
        {
            VirtualScreen = virtualScreen,
            Monitors = monitors
        };
    }
}
