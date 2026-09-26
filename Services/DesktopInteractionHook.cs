using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Threading;
using RestoreDesktopIcons.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 桌面用户交互全局低级钩子：
/// 运行在独立的专用高优先级后台 STA 线程中，自带纯净极简 Windows 消息泵，彻底与主 UI 线程解耦。
/// 仅当桌面图标处于隐藏状态时被动态挂载，杜绝任何鼠标卡顿、漂移或对系统日常操作的干扰。
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class DesktopInteractionHook
{
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;

    private static Thread? _workerThread;
    private static Dispatcher? _workerDispatcher;
    private static HHOOK _hookHandle;
    private static HOOKPROC? _hookProc;
    private static bool _isHookDesired;
    private static readonly object _lock = new();

    private static ulong _lastLeftClickTick;
    private static Point _lastLeftClickPoint = Point.Empty;

    /// <summary>
    /// 触发模式（默认 LeftDoubleClick: 鼠标左键双击防误触）
    /// </summary>
    public static DesktopTriggerMode TriggerMode { get; set; } = DesktopTriggerMode.LeftDoubleClick;

    /// <summary>
    /// 当用户触发桌面恢复动作时激活
    /// </summary>
    public static event Action? DesktopClicked;

    /// <summary>
    /// 是否已安装或正在等待安装钩子
    /// </summary>
    public static bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isHookDesired || _hookHandle != HHOOK.Null;
            }
        }
    }

    /// <summary>
    /// 确保常驻独立高优先级后台 STA 线程就绪
    /// </summary>
    private static void EnsureWorkerThread()
    {
        if (_workerThread != null && _workerDispatcher != null && !_workerDispatcher.HasShutdownStarted)
            return;

        using var readyEvent = new ManualResetEventSlim(false);

        _workerThread = new Thread(() =>
        {
            try
            {
                _workerDispatcher = Dispatcher.CurrentDispatcher;
                _hookProc = HookCallback;
            }
            finally
            {
                readyEvent.Set();
            }

            // 专用极简 Windows 消息泵：专职响应 WH_MOUSE_LL，0 业务阻塞，微秒级即时调度
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "DesktopHookWorker_Thread"
        };
        _workerThread.SetApartmentState(ApartmentState.STA);
        _workerThread.Start();

        readyEvent.Wait(1000);
    }

    /// <summary>
    /// 启动低级鼠标监听钩子（在常驻独立高优先级线程中极速挂载，纯非阻塞，耗时小于 0.05ms）
    /// </summary>
    public static void Start()
    {
        lock (_lock)
        {
            if (_isHookDesired) return;
            _isHookDesired = true;

            _lastLeftClickTick = 0;
            _lastLeftClickPoint = Point.Empty;

            EnsureWorkerThread();

            // 派发至常驻独立线程执行 SetWindowsHookEx，主线程 0 阻塞无感切换
            _workerDispatcher?.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (!_isHookDesired) return; // 若在排队期间已被要求注销，则终止安装

                    if (_hookHandle == HHOOK.Null && _hookProc != null)
                    {
                        _hookHandle = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookProc, HINSTANCE.Null, 0);

                        if (_hookHandle == HHOOK.Null)
                        {
                            _isHookDesired = false;
                            int error = Marshal.GetLastWin32Error();
                            AppLogger.Error($"[AutoHide] 独立线程安装低级鼠标钩子失败，Win32错误码: {error}");
                        }
                        else
                        {
                            AppLogger.Info($"[AutoHide] 独立高优先级线程成功按需挂载低级鼠标钩子 (手势: {TriggerMode})");
                        }
                    }
                }
            }, DispatcherPriority.Send);
        }
    }

    /// <summary>
    /// 卸载低级鼠标监听钩子（独立线程极速注销，后台线程继续常驻休眠等待下次唤醒，0 线程反复创建销毁抖动）
    /// </summary>
    public static void Stop()
    {
        lock (_lock)
        {
            if (!_isHookDesired && _hookHandle == HHOOK.Null) return;
            _isHookDesired = false;

            if (_workerDispatcher != null && !_workerDispatcher.HasShutdownStarted)
            {
                _workerDispatcher.InvokeAsync(() =>
                {
                    lock (_lock)
                    {
                        if (_isHookDesired) return; // 若在排队期间又有新 Start 请求，取消本次卸载

                        if (_hookHandle != HHOOK.Null)
                        {
                            PInvoke.UnhookWindowsHookEx(_hookHandle);
                            _hookHandle = default;
                            AppLogger.Info("[AutoHide] 桌面交互低级鼠标钩子已在独立线程安全卸载（按需休眠）");
                        }
                    }
                }, DispatcherPriority.Send);
            }
            else if (_hookHandle != HHOOK.Null)
            {
                PInvoke.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = default;
            }

            _lastLeftClickTick = 0;
            _lastLeftClickPoint = Point.Empty;
        }
    }

    private static unsafe LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam.Value;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                try
                {
                    // 仅当桌面图标当前处于隐藏状态时，才做桌面点击识别（利用纳秒级内存缓存，严禁在此处做注册表/磁盘 I/O）
                    if (!DesktopIconVisibilityService.CachedVisibility)
                    {
                        var hookStruct = *(MSLLHOOKSTRUCT*)(void*)lParam.Value;
                        Point pt = hookStruct.pt;

                        HWND clickedHwnd = PInvoke.WindowFromPoint(pt);
                        if (!clickedHwnd.IsNull && IsDesktopWindow(clickedHwnd))
                        {
                            if (msg == WM_LBUTTONDOWN)
                            {
                                if (TriggerMode == DesktopTriggerMode.LeftDoubleClick)
                                {
                                    ulong currentTick = (ulong)Environment.TickCount64;
                                    int doubleClickTime = System.Windows.Forms.SystemInformation.DoubleClickTime;
                                    var doubleClickSize = System.Windows.Forms.SystemInformation.DoubleClickSize;

                                    ulong elapsed = currentTick - _lastLeftClickTick;
                                    bool inTime = _lastLeftClickTick != 0 && elapsed <= (ulong)doubleClickTime;
                                    bool inArea = Math.Abs(pt.X - _lastLeftClickPoint.X) <= Math.Max(8, doubleClickSize.Width) &&
                                                  Math.Abs(pt.Y - _lastLeftClickPoint.Y) <= Math.Max(8, doubleClickSize.Height);

                                    if (inTime && inArea)
                                    {
                                        _lastLeftClickTick = 0;
                                        _lastLeftClickPoint = Point.Empty;
                                        AppLogger.Info($"[AutoHide] 检测到桌面空白处【鼠标左键双击】(间隔={elapsed}ms)，触发恢复桌面图标");
                                        TriggerDesktopClickedAsync();
                                    }
                                    else
                                    {
                                        _lastLeftClickTick = currentTick;
                                        _lastLeftClickPoint = pt;
                                    }
                                }
                                else
                                {
                                    AppLogger.Info($"[AutoHide] 检测到桌面空白处【鼠标左键单击】，触发恢复桌面图标");
                                    TriggerDesktopClickedAsync();
                                }
                            }
                            else if (TriggerMode == DesktopTriggerMode.AnyClick)
                            {
                                AppLogger.Info($"[AutoHide] 检测到桌面鼠标按键点击 (msg=0x{msg:X4})，触发恢复桌面图标");
                                TriggerDesktopClickedAsync();
                            }
                            else
                            {
                                _lastLeftClickTick = 0;
                                _lastLeftClickPoint = Point.Empty;
                            }
                        }
                        else
                        {
                            // 点击了非桌面窗口（如其他应用程序），立即打断并重置双击判定序列
                            _lastLeftClickTick = 0;
                            _lastLeftClickPoint = Point.Empty;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("[AutoHide] 鼠标钩子处理桌面点击异常", ex);
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private static void TriggerDesktopClickedAsync()
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () =>
            {
                DesktopClicked?.Invoke();
            });
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => DesktopClicked?.Invoke());
        }
    }

    /// <summary>
    /// 识别目标窗口句柄是否为 Windows 桌面（Progman / WorkerW / SHELLDLL_DefView / SysListView32）
    /// </summary>
    private static bool IsDesktopWindow(HWND hwnd)
    {
        if (hwnd.IsNull) return false;

        HWND shellWindow = PInvoke.GetShellWindow();
        if (hwnd == shellWindow) return true;

        Span<char> buf = stackalloc char[128];
        int len = PInvoke.GetClassName(hwnd, buf);
        if (len > 0)
        {
            string cls = new(buf[..len]);
            if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")
                return true;
        }

        // 检查顶级祖先窗口
        HWND root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (!root.IsNull)
        {
            if (root == shellWindow) return true;

            len = PInvoke.GetClassName(root, buf);
            if (len > 0)
            {
                string rootCls = new(buf[..len]);
                if (rootCls is "Progman" or "WorkerW")
                    return true;
            }
        }

        return false;
    }
}
