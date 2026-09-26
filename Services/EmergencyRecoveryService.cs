using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Console;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 全生命周期应急容灾与桌面图标保护服务：
/// 1. 拦截所有托管层未捕获异常（AppDomain、WPF UI Dispatcher、TaskScheduler）
/// 2. 拦截系统会话结束（Windows 注销、关机）
/// 3. 拦截进程退出信号（ProcessExit、Application.Exit、控制台 Ctrl+C）
/// 4. 启动内核级外部 Watchdog 守护子进程，无死角防护硬杀进程（taskkill /F、IDE 停止调试、崩溃强退）
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class EmergencyRecoveryService
{
    private static Process? _watchdogProcess;
    private static PHANDLER_ROUTINE? _consoleCtrlHandler;
    private static string? _markerFilePath;
    private static readonly object _lock = new();
    private static bool _isInitialized;

    /// <summary>
    /// 初始化应急容灾恢复体系
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            int currentPid = Environment.ProcessId;

            // 创建专属于本主进程 PID 的“存活中”标识文件
            // 只要进程非正常退场（硬杀、崩溃、异常终止），该标记文件就会保留在磁盘上
            try
            {
                _markerFilePath = Path.Combine(Path.GetTempPath(), $"RestoreDesktopIcons_{currentPid}.active");
                File.WriteAllText(_markerFilePath, $"{DateTime.Now:O}|{currentPid}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("[Emergency] 创建存活标记文件失败", ex);
            }

            // 1. 注册 AppDomain 级异常崩溃
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                RestoreDesktopIconsIfHidden("AppDomain 未捕获异常崩溃");
            };

            // 2. 注册进程正常与意外退出
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                RestoreDesktopIconsIfHidden("进程退出 (ProcessExit)");
                MarkCleanExit();
            };

            // 3. 注册 Windows 会话结束 (注销 / 关机)
            try
            {
                SystemEvents.SessionEnding += (s, e) =>
                {
                    RestoreDesktopIconsIfHidden("Windows 系统会话结束 (注销/关机)");
                    MarkCleanExit();
                };
            }
            catch { }

            // 4. 注册原生控制台中断信号
            try
            {
                _consoleCtrlHandler = OnConsoleCtrl;
                PInvoke.SetConsoleCtrlHandler(_consoleCtrlHandler, true);
            }
            catch { }

            // 5. 启动内核级独立 Watchdog 守护子进程（监控本进程存活）
            StartWatchdogSupervisor();

            AppLogger.Info("[Emergency] 全流程应急恢复与 Watchdog 守护容灾体系已全面就绪");
        }
    }

    /// <summary>
    /// 标记主进程已正常完成退出与善后处理（删除存活标记文件，通知 Watchdog 无需进行异常干预）
    /// </summary>
    public static void MarkCleanExit()
    {
        try
        {
            if (!string.IsNullOrEmpty(_markerFilePath) && File.Exists(_markerFilePath))
            {
                File.Delete(_markerFilePath);
            }
        }
        catch { }
    }

    /// <summary>
    /// 紧急恢复桌面图标：检查桌面图标是否被隐藏，若已被隐藏则立即向 Windows Shell 强制恢复显示
    /// </summary>
    public static void RestoreDesktopIconsIfHidden(string triggerReason)
    {
        try
        {
            if (!DesktopIconVisibilityService.AreDesktopIconsVisible())
            {
                AppLogger.Warn($"[Emergency] 触发桌面图标应急恢复机制 ({triggerReason})，正在将桌面图标还原为显示状态...");
                DesktopIconVisibilityService.ShowDesktopIcons();
                AppLogger.Info("[Emergency] 桌面图标已成功还原为显示状态！");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Emergency] 执行桌面图标应急恢复失败 ({triggerReason})", ex);
        }
    }

    /// <summary>
    /// 启动独立的 Watchdog 进程监控主进程存活 (后台线程池异步执行，彻底避免启动阶段阻塞 UI 主线程)
    /// 使用独立的 powershell.exe 系统宿主监控主进程，彻底杜绝主进程被按映像名 taskkill /IM RestoreDesktopIcons.exe 连带误杀，
    /// 且绝不占用目标 exe 句柄锁，使项目随时可自由重编译构建。
    /// </summary>
    private static void StartWatchdogSupervisor()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                int currentPid = Environment.ProcessId;
                string markerPath = _markerFilePath ?? Path.Combine(Path.GetTempPath(), $"RestoreDesktopIcons_{currentPid}.active");

                // 独立外部宿主监视命令：
                // 1. Wait-Process 等待主进程退出（使用 Windows 内核句柄阻塞，0% CPU 开销）
                // 2. 检查存活标记文件：若标记文件仍在，说明遭遇非正常退出（硬杀/崩溃），立即调用主程序恢复桌面图标
                // 3. 若标记文件已被正常删除，说明为主进程正常退出，静默退出不做干预
                string command = $"Wait-Process -Id {currentPid} -ErrorAction SilentlyContinue; if (Test-Path '{markerPath}') {{ Remove-Item '{markerPath}' -Force -ErrorAction SilentlyContinue; Start-Process -FilePath '{exePath}' -ArgumentList 'show' -WindowStyle Hidden }}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _watchdogProcess = Process.Start(psi);
                if (_watchdogProcess != null)
                {
                    AppLogger.Info($"[Emergency] 独立外部 Watchdog 守护进程已就绪 (PID={_watchdogProcess.Id}, 宿主=powershell.exe, 正在坚守主进程 PID={currentPid})");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("[Emergency] 启动 Watchdog 守护子进程失败", ex);
            }
        });
    }

    /// <summary>
    /// Watchdog 独立子进程执行体：
    /// 当主进程因为任何原因（调试器停止、任务管理器强制杀进程、崩溃退出等）终止时，
    /// 操作系统内核立即唤醒本方法，检测桌面图标是否被遗留在隐藏状态并立即恢复显示！
    /// </summary>
    public static void RunWatchdogLoop(int parentPid)
    {
        AppLogger.Info($"[Watchdog] 独立应急监视进程已就绪，正在等待主进程 PID={parentPid} 信号...");

        try
        {
            EventWaitHandle? cleanExitEvent = null;
            try
            {
                cleanExitEvent = EventWaitHandle.OpenExisting($"Local\\RestoreDesktopIcons_CleanExit_{parentPid}");
            }
            catch { }

            Process? parentProcess = null;
            try
            {
                parentProcess = Process.GetProcessById(parentPid);
            }
            catch
            {
                // 父进程在启动 watchdog 时可能已经退出了
            }

            if (parentProcess != null)
            {
                // 零 CPU 消耗的内核句柄同步等待
                parentProcess.WaitForExit();
            }

            // 1. 检查主进程是否发出了“正常退出完成”信号
            bool isCleanExit = false;
            if (cleanExitEvent != null)
            {
                try
                {
                    isCleanExit = cleanExitEvent.WaitOne(0);
                }
                catch { }
            }

            if (isCleanExit)
            {
                AppLogger.Info($"[Watchdog] 主进程 (PID={parentPid}) 属于正常退出流程，无需外部应急介入，守护完成。");
                return;
            }

            // 2. 到这里说明主进程属于异常猝死（IDE停止调试、任务管理器强杀、未捕获原生崩溃），主进程来不及标记 CleanExit！
            AppLogger.Warn($"[Watchdog] 侦测到主进程 (PID={parentPid}) 异常终止（未收到 CleanExit 信号）！正在检查桌面状态...");

            // 物理权威检查桌面图标状态
            if (!DesktopIconVisibilityService.AreDesktopIconsVisible())
            {
                AppLogger.Warn($"[Watchdog] 桌面图标处于隐藏残留状态，正在执行强制应急还原...");
                DesktopIconVisibilityService.ShowDesktopIcons();
                AppLogger.Info("[Watchdog] 桌面图标已成功强制还原，保护用户桌面正常使用完毕！");
            }
            else
            {
                AppLogger.Info("[Watchdog] 桌面图标当前处于显示状态，无需还原。");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Watchdog] 监视执行异常 (parentPid={parentPid})", ex);
        }
    }

    private static Windows.Win32.Foundation.BOOL OnConsoleCtrl(uint dwCtrlType)
    {
        // 0: CTRL_C_EVENT, 1: CTRL_BREAK_EVENT, 2: CTRL_CLOSE_EVENT, 5: CTRL_LOGOFF_EVENT, 6: CTRL_SHUTDOWN_EVENT
        RestoreDesktopIconsIfHidden($"控制台中断信号: {dwCtrlType}");
        return (Windows.Win32.Foundation.BOOL)false; // 允许默认退出流程继续执行
    }
}
