using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;
using RestoreDesktopIcons.Models;
using RestoreDesktopIcons.Services;

using Windows.Win32;

namespace RestoreDesktopIcons;

[SupportedOSPlatform("windows6.0.6000")]
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局未捕获异常守护日志与桌面图标应急还原
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            AppLogger.Error("进程全局未捕获异常 (AppDomain)", args.ExceptionObject as Exception);
            EmergencyRecoveryService.RestoreDesktopIconsIfHidden("AppDomain 未捕获异常");
        };
        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.Error("WPF UI 线程未捕获异常", args.Exception);
            EmergencyRecoveryService.RestoreDesktopIconsIfHidden("WPF UI 线程未捕获异常");
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            AppLogger.Error("异步 Task 未观察异常", args.Exception);
            args.SetObserved();
        };
        Exit += (s, args) =>
        {
            EmergencyRecoveryService.RestoreDesktopIconsIfHidden("WPF Application Exit 退出");
            EmergencyRecoveryService.MarkCleanExit();
        };

        var cmdArgs = Environment.GetCommandLineArgs();
        bool startMinimized = false;

        if (cmdArgs.Length > 1)
        {
            string firstArg = cmdArgs[1].Trim().ToLowerInvariant();
            if (firstArg == "--watchdog" && cmdArgs.Length > 2)
            {
                if (int.TryParse(cmdArgs[2], out int parentPid))
                {
                    EmergencyRecoveryService.RunWatchdogLoop(parentPid);
                }
                Shutdown(0);
                return;
            }

            if (firstArg is "--minimized" or "-min" or "/min")
            {
                startMinimized = true;
            }
            else
            {
                PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS);
                try
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    var stdOut = new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true };
                    Console.SetOut(stdOut);
                    var stdErr = new StreamWriter(Console.OpenStandardError(), System.Text.Encoding.UTF8) { AutoFlush = true };
                    Console.SetError(stdErr);
                }
                catch { }

                // 启用控制台日志输出，终端可实时查看完整进度
                AppLogger.ConsoleOutputEnabled = true;

                string cmd = firstArg;
                if (cmd is "help" or "--help" or "-h" or "/?" or "?")
            {
                AppLogger.Info(@"
Windows 11 桌面图标守护程序 (RestoreDesktopIcons) CLI 命令行说明：
  save, -s, /save         : 保存当前桌面图标布局并生成原生多屏快照
  restore, -r, /restore   : 恢复最新保存的历史快照 (恢复前自动生成现场灾备快照)
  diag                    : 诊断当前屏幕拓扑、分辨率与桌面图标物理坐标
  fix-to-primary, recover : 一键将副屏图标安全平移回主屏幕
  hide                    : 立即隐藏桌面所有图标
  show                    : 立即显示桌面所有图标
  toggle                  : 切换桌面图标显示/隐藏状态
  --minimized, -min       : 启动并最小化至托盘后台守护
  help, -h, /?            : 显示本帮助信息
");
                Shutdown(0);
                return;
            }
            if (cmd is "fix-to-primary" or "recover")
            {
                AppLogger.Info("CLI: 开始执行 fix-to-primary 修复桌面图标位置...");
                int moved = DesktopShellCsWin32Service.MoveIconsFromSecondaryToPrimary(1440);
                string msg = $"[Success] 成功将 {moved} 个图标平移回主屏幕！";
                AppLogger.Info(msg);
                Shutdown(moved > 0 ? 0 : 1);
                return;
            }
            if (cmd is "diag")
            {
                AppLogger.Info("CLI: 开始执行 diag 屏幕拓扑与图标诊断...");
                var curTopo = TopologyService.GetCurrentTopology();
                AppLogger.Info($"VirtualScreen: Left={curTopo.VirtualScreen.Left}, Top={curTopo.VirtualScreen.Top}, Width={curTopo.VirtualScreen.Width}, Height={curTopo.VirtualScreen.Height}");
                foreach (var m in curTopo.Monitors)
                {
                    string mInfo = $"Monitor {m.DeviceName} Primary={m.IsPrimary}: Bounds=({m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Right},{m.Bounds.Bottom}) ClientBounds=({m.ClientBounds.Left},{m.ClientBounds.Top},{m.ClientBounds.Right},{m.ClientBounds.Bottom}) WorkArea=({m.WorkArea.Left},{m.WorkArea.Top},{m.WorkArea.Right},{m.WorkArea.Bottom})";
                    AppLogger.Info(mInfo);
                }
                var (liveIcons, curMode, curSpX, curSpY) = DesktopShellCsWin32Service.CaptureDesktopIcons();
                string iconSummary = $"Total live icons: {liveIcons.Count}, ViewMode={curMode}, Spacing=({curSpX},{curSpY})";
                AppLogger.Info(iconSummary);
                for (int i = 0; i < liveIcons.Count; i++)
                {
                    string iconLine = $"  [{i:D2}] {liveIcons[i].DisplayName}: X={liveIcons[i].X}, Y={liveIcons[i].Y}";
                    if (i < 20) AppLogger.Info(iconLine);
                }
                Shutdown(0);
                return;
            }
            if (cmd is "save" or "/save" or "-s")
            {
                try
                {
                    AppLogger.Info("执行 CLI 命令行保存操作...");
                    var topo = TopologyService.GetCurrentTopology();

                    // 截图安全保障：若桌面图标被隐藏，先临时恢复显示
                    bool wasHidden = DesktopIconVisibilityService.EnsureIconsVisibleForCapture();

                    try
                    {
                        var (icons, viewMode, spX, spY) = DesktopShellCsWin32Service.CaptureDesktopIcons();
                        if (icons.Count == 0)
                        {
                            AppLogger.Warn("[Error] 未能读取到桌面图标。");
                            Shutdown(1);
                            return;
                        }

                        var list = LayoutStorageService.LoadSnapshots();
                        var cliSettings = LayoutStorageService.LoadSettings();
                        var engine = cliSettings.UseRealDesktopCapture ? DesktopCaptureEngine.RealHardwareDwm : DesktopCaptureEngine.SyntheticHologram;
                        string name = $"{DateTime.Now:yyyy-MM-dd HH:mm} ({topo.Summary})";
                        var snapshotId = Guid.NewGuid();
                        var (shotPath, monitorPaths) = ScreenCaptureService.CaptureDesktopMonitors(snapshotId, topo, icons, engine);
                        var snapshot = new LayoutSnapshotModel
                        {
                            Id = snapshotId,
                            Name = name,
                            CreatedAt = DateTimeOffset.Now,
                            Topology = topo,
                            ViewMode = viewMode,
                            GridSpacingX = spX,
                            GridSpacingY = spY,
                            Icons = icons,
                            ScreenshotPath = shotPath,
                            MonitorScreenshots = monitorPaths
                        };
                        list.Insert(0, snapshot);
                        LayoutStorageService.SaveSnapshots(list);
                        string hiddenNote = wasHidden ? " (已自动临时恢复图标显示后截图)" : "";
                        string successMsg = $"[Success] 成功保存快照「{name}」，包含 {icons.Count} 个图标（已生成各分屏纯净截图）。{hiddenNote}";
                        AppLogger.Info(successMsg);
                        Shutdown(0);
                        return;
                    }
                    finally
                    {
                        DesktopIconVisibilityService.RestoreHiddenStateAfterCapture(wasHidden);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[Error] 保存发生异常: {ex.Message}", ex);
                    Shutdown(1);
                    return;
                }
            }
            else if (cmd is "restore" or "/restore" or "-r")
            {
                try
                {
                    AppLogger.Info("执行 CLI 命令行恢复操作...");
                    var list = LayoutStorageService.LoadSnapshots();
                    if (list.Count == 0)
                    {
                        AppLogger.Warn("[Error] layouts.json 中没有找到任何历史快照。");
                        Shutdown(1);
                        return;
                    }

                    // 确保在恢复流程中桌面图标处于显示状态
                    DesktopIconVisibilityService.ShowDesktopIcons();

                    // 恢复前自动安全备份当前现场
                    try
                    {
                        var curTopo = TopologyService.GetCurrentTopology();
                        bool wasHidden = DesktopIconVisibilityService.EnsureIconsVisibleForCapture();
                        try
                        {
                            var (curIcons, curMode, curSpX, curSpY) = DesktopShellCsWin32Service.CaptureDesktopIcons();
                            if (curIcons.Count > 0)
                            {
                                var autoBackupId = Guid.NewGuid();
                                var (autoShot, autoMonitors) = ScreenCaptureService.CaptureDesktopMonitors(autoBackupId, curTopo, curIcons);
                                var autoBackup = new LayoutSnapshotModel
                                {
                                    Id = autoBackupId,
                                    Name = $"[自动现场备份] 恢复前 ({DateTime.Now:yyyy-MM-dd HH:mm:ss})",
                                    CreatedAt = DateTimeOffset.Now,
                                    Topology = curTopo,
                                    ViewMode = curMode,
                                    GridSpacingX = curSpX,
                                    GridSpacingY = curSpY,
                                    Icons = curIcons,
                                    ScreenshotPath = autoShot,
                                    MonitorScreenshots = autoMonitors
                                };
                                list.Insert(0, autoBackup);
                                LayoutStorageService.SaveSnapshots(list);
                                AppLogger.Info($"[CLI] 已自动创建恢复前现场灾备快照: {autoBackup.Name}");
                            }
                        }
                        finally
                        {
                            DesktopIconVisibilityService.RestoreHiddenStateAfterCapture(wasHidden);
                        }
                    }
                    catch (Exception exBak)
                    {
                        AppLogger.Warn("[CLI] 创建现场灾备快照失败 (不中断主恢复流程)", exBak);
                    }

                    // 恢复目标布局前再次确保桌面图标已显示
                    DesktopIconVisibilityService.ShowDesktopIcons();

                    // 重新取第 1 个需要恢复的目标快照（如果是刚插入了 autoBackup，则恢复目标为原 list[0] 即 list[1]）
                    var snapshot = list.Count > 1 ? list[1] : list[0];
                    var topo = TopologyService.GetCurrentTopology();
                    var (restored, clamped, msg) = DesktopShellCsWin32Service.RestoreDesktopIcons(snapshot, topo);
                    AppLogger.Info($"[Result] {msg}");
                    Shutdown(restored > 0 ? 0 : 1);
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[Error] 恢复发生异常: {ex.Message}", ex);
                    Shutdown(1);
                    return;
                }
            }
            else if (cmd is "hide" or "/hide")
            {
                DesktopIconVisibilityService.HideDesktopIcons();
                AppLogger.Info("[CLI] 已向 Windows 桌面发送隐藏图标指令。");
                Shutdown(0);
                return;
            }
            else if (cmd is "show" or "/show")
            {
                DesktopIconVisibilityService.ShowDesktopIcons();
                AppLogger.Info("[CLI] 已向 Windows 桌面发送显示图标指令。");
                Shutdown(0);
                return;
            }
            else if (cmd is "toggle" or "/toggle")
            {
                DesktopIconVisibilityService.ToggleDesktopIcons();
                bool visible = DesktopIconVisibilityService.AreDesktopIconsVisible();
                AppLogger.Info($"[CLI] 桌面图标状态已切换，当前状态: {(visible ? "显示" : "隐藏")}");
                Shutdown(0);
                return;
            }
            else
            {
                AppLogger.Warn($"未知的 CLI 命令参数: '{firstArg}'. 输入 'help' 查看支持的命令。");
                Shutdown(1);
                return;
            }
        }
    }

        // 单实例互斥保护：防止重复多开导致低级鼠标钩子冲突与定时器抢夺
        _singleInstanceMutex = new Mutex(true, "Local\\RestoreDesktopIcons_SingleInstance_Mutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            AppLogger.Warn("检测到 RestoreDesktopIcons 已有实例正在运行，禁止多开。");
            Shutdown(0);
            return;
        }

        // 初始化全局设置、自动隐藏调度器与系统托盘服务
        var settings = LayoutStorageService.LoadSettings();
        AutoHideCoordinator.Initialize(settings);

        // 挂载全局异常与内核级 Watchdog 守护子进程
        EmergencyRecoveryService.Initialize();

        var mainWindow = new Views.MainWindow();
        TrayIconService.Initialize(mainWindow);

        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Hide();
            AppLogger.Info("程序已通过静默托盘模式启动");
        }
        else
        {
            mainWindow.Show();
        }
    }
}
