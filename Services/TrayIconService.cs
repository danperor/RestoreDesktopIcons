using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Forms;
using RestoreDesktopIcons.Models;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 系统托盘图标与上下文菜单服务：
/// 提供任务栏右下角托盘常驻、状态展示、快捷菜单控制及后台静默守护。
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class TrayIconService
{
    private static NotifyIcon? _notifyIcon;
    private static ContextMenuStrip? _contextMenu;
    private static ToolStripMenuItem? _autoHideItem;
    private static ToolStripMenuItem? _desktopClickItem;
    private static ToolStripMenuItem? _triggerMenu;
    private static ToolStripMenuItem? _delayMenu;
    private static ToolStripMenuItem? _autoStartupItem;
    private static System.Windows.Window? _mainWindow;

    public static void Initialize(System.Windows.Window mainWindow)
    {
        _mainWindow = mainWindow;

        _contextMenu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        // 1. 显示主界面
        var showMainItem = new ToolStripMenuItem("🪟 显示主界面", null, (_, _) => ShowMainWindow())
        {
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
        };
        _contextMenu.Items.Add(showMainItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 2. 启用自动隐藏图标开关
        _autoHideItem = new ToolStripMenuItem("⏱ 启用自动隐藏图标", null, (_, _) =>
        {
            AutoHideCoordinator.IsEnabled = !AutoHideCoordinator.IsEnabled;
            UpdateMenuStates();
        });
        _contextMenu.Items.Add(_autoHideItem);

        // 3. 等待时间子菜单
        _delayMenu = new ToolStripMenuItem("⏳ 隐藏等待时间");
        int[] delayOptions = [3, 5, 10, 30, 60, 300];
        foreach (int sec in delayOptions)
        {
            string label = sec >= 60 ? $"{sec / 60} 分钟" : $"{sec} 秒";
            int currentSec = sec;
            var subItem = new ToolStripMenuItem(label, null, (_, _) =>
            {
                AutoHideCoordinator.DelaySeconds = currentSec;
                UpdateMenuStates();
            })
            {
                Tag = currentSec
            };
            _delayMenu.DropDownItems.Add(subItem);
        }
        _contextMenu.Items.Add(_delayMenu);

        // 4. 点击桌面恢复图标
        _desktopClickItem = new ToolStripMenuItem("🖱 点击桌面恢复显示", null, (_, _) =>
        {
            AutoHideCoordinator.ShowOnDesktopClick = !AutoHideCoordinator.ShowOnDesktopClick;
            UpdateMenuStates();
        });
        _contextMenu.Items.Add(_desktopClickItem);

        // 5. 唤醒触发手势子菜单
        _triggerMenu = new ToolStripMenuItem("🎯 唤醒触发手势");

        var doubleClickItem = new ToolStripMenuItem("鼠标左键双击 (推荐防误触)", null, (_, _) =>
        {
            AutoHideCoordinator.TriggerMode = DesktopTriggerMode.LeftDoubleClick;
            UpdateMenuStates();
        }) { Tag = DesktopTriggerMode.LeftDoubleClick };
        _triggerMenu.DropDownItems.Add(doubleClickItem);

        var singleClickItem = new ToolStripMenuItem("鼠标左键单击 (原版行为)", null, (_, _) =>
        {
            AutoHideCoordinator.TriggerMode = DesktopTriggerMode.LeftSingleClick;
            UpdateMenuStates();
        }) { Tag = DesktopTriggerMode.LeftSingleClick };
        _triggerMenu.DropDownItems.Add(singleClickItem);

        var anyClickItem = new ToolStripMenuItem("任意按键单击 (左/中/右键)", null, (_, _) =>
        {
            AutoHideCoordinator.TriggerMode = DesktopTriggerMode.AnyClick;
            UpdateMenuStates();
        }) { Tag = DesktopTriggerMode.AnyClick };
        _triggerMenu.DropDownItems.Add(anyClickItem);

        _contextMenu.Items.Add(_triggerMenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 6. 手动显示/隐藏图标
        _contextMenu.Items.Add(new ToolStripMenuItem("👁 立即显示桌面图标", null, (_, _) =>
        {
            DesktopIconVisibilityService.ShowDesktopIcons();
        }));
        _contextMenu.Items.Add(new ToolStripMenuItem("🙈 立即隐藏桌面图标", null, (_, _) =>
        {
            DesktopIconVisibilityService.HideDesktopIcons();
        }));

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 7. 开机自启动
        _autoStartupItem = new ToolStripMenuItem("🚀 开机自动启动", null, (_, _) =>
        {
            bool current = AutoStartupHelper.IsAutoStartupEnabled();
            AutoStartupHelper.SetAutoStartup(!current);
            UpdateMenuStates();
        });
        _contextMenu.Items.Add(_autoStartupItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 8. 退出程序
        _contextMenu.Items.Add(new ToolStripMenuItem("❌ 退出守护程序", null, (_, _) =>
        {
            ExitApplication();
        }));

        // 提取程序主图标
        Icon appIcon = ExtractApplicationIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "Windows 桌面图标守护程序",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        AutoHideCoordinator.StateChanged += UpdateMenuStates;
        UpdateMenuStates();

        AppLogger.Info("[Tray] 系统托盘图标服务初始化完成");
    }

    public static void UpdateMenuStates()
    {
        if (_autoHideItem != null)
            _autoHideItem.Checked = AutoHideCoordinator.IsEnabled;

        if (_desktopClickItem != null)
            _desktopClickItem.Checked = AutoHideCoordinator.ShowOnDesktopClick;

        if (_triggerMenu != null)
        {
            _triggerMenu.Enabled = AutoHideCoordinator.ShowOnDesktopClick;
            foreach (ToolStripItem item in _triggerMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi && mi.Tag is DesktopTriggerMode mode)
                {
                    mi.Checked = (mode == AutoHideCoordinator.TriggerMode);
                }
            }
        }

        if (_delayMenu != null)
        {
            foreach (ToolStripItem item in _delayMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi && mi.Tag is int sec)
                {
                    mi.Checked = (sec == AutoHideCoordinator.DelaySeconds);
                }
            }
        }

        if (_autoStartupItem != null)
            _autoStartupItem.Checked = AutoStartupHelper.IsAutoStartupEnabled();

        if (_notifyIcon != null)
        {
            string delayText = AutoHideCoordinator.DelaySeconds >= 60
                ? $"{AutoHideCoordinator.DelaySeconds / 60}分钟"
                : $"{AutoHideCoordinator.DelaySeconds}秒";

            string triggerDesc = AutoHideCoordinator.TriggerMode switch
            {
                DesktopTriggerMode.LeftDoubleClick => "左双击",
                DesktopTriggerMode.LeftSingleClick => "左单击",
                _ => "任意点击"
            };

            _notifyIcon.Text = AutoHideCoordinator.IsEnabled
                ? $"桌面图标守护程序\n自动隐藏: 开启 ({delayText}, {triggerDesc})"
                : "桌面图标守护程序\n自动隐藏: 已停用";
        }
    }

    public static void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                    _mainWindow.WindowState = System.Windows.WindowState.Normal;

                _mainWindow.Show();
                _mainWindow.Activate();
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
                _mainWindow.Focus();
            });
        }
    }

    public static void ExitApplication()
    {
        AppLogger.Info("[Tray] 用户通过托盘右键菜单选择退出程序");
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        DesktopInteractionHook.Stop();

        // 显式确保退出前将隐藏的桌面图标强制还原
        EmergencyRecoveryService.RestoreDesktopIconsIfHidden("托盘菜单退出");
        EmergencyRecoveryService.MarkCleanExit();

        if (_mainWindow is Views.MainWindow mw)
        {
            mw.RequestExplicitExit();
        }

        System.Windows.Application.Current?.Shutdown(0);
    }

    private static Icon ExtractApplicationIcon()
    {
        try
        {
            // 1. 优先加载本地专属设计的 Fluent 风格高清 app.ico (根据系统 DPI 自动适配 16x16 / 24x24 / 32x32)
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(localIco))
            {
                return new Icon(localIco, SystemInformation.SmallIconSize);
            }

            // 2. 尝试从嵌入式资源中提取
            var resStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (resStream != null)
            {
                using (resStream)
                {
                    return new Icon(resStream, SystemInformation.SmallIconSize);
                }
            }

            // 3. 兜底提取 exe 内嵌图标
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("[Tray] 加载高清托盘图标异常，降级使用系统默认图标", ex);
        }

        return SystemIcons.Application;
    }
}
