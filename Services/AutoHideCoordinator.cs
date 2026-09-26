using System.Runtime.Versioning;
using System.Windows.Threading;
using RestoreDesktopIcons.Models;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 桌面图标自动隐藏总调度器 (AutoHide Coordinator)：
/// 协调系统空闲时间巡检、桌面点击唤醒钩子、业务流程安全暂停/互斥，与配置持久化。
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public static class AutoHideCoordinator
{
    private static DispatcherTimer? _idleTimer;
    private static bool _isEnabled;
    private static int _delaySeconds = 10;
    private static bool _showOnDesktopClick = true;
    private static int _pauseCount;
    private static readonly object _pauseLock = new();

    /// <summary>
    /// 状态发生变化时触发（便于 UI 和托盘图标实时刷新勾选态）
    /// </summary>
    public static event Action? StateChanged;

    /// <summary>
    /// 是否开启自动隐藏
    /// </summary>
    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                ApplyState();
                SaveCurrentSettings();
                StateChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 自动隐藏空闲延迟（秒）
    /// </summary>
    public static int DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            int clamped = Math.Max(2, Math.Min(3600, value));
            if (_delaySeconds != clamped)
            {
                _delaySeconds = clamped;
                SaveCurrentSettings();
                StateChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 点击桌面是否恢复显示
    /// </summary>
    public static bool ShowOnDesktopClick
    {
        get => _showOnDesktopClick;
        set
        {
            if (_showOnDesktopClick != value)
            {
                _showOnDesktopClick = value;
                UpdateHookState();
                SaveCurrentSettings();
                StateChanged?.Invoke();
            }
        }
    }

    private static DesktopTriggerMode _triggerMode = DesktopTriggerMode.LeftDoubleClick;

    /// <summary>
    /// 恢复桌面图标的手势触发方式（默认左键双击防误触）
    /// </summary>
    public static DesktopTriggerMode TriggerMode
    {
        get => _triggerMode;
        set
        {
            if (_triggerMode != value)
            {
                _triggerMode = value;
                DesktopInteractionHook.TriggerMode = value;
                SaveCurrentSettings();
                StateChanged?.Invoke();
                AppLogger.Info($"[AutoHide] 已修改桌面唤醒触发手势为: {value}");
            }
        }
    }

    /// <summary>
    /// 当前是否处于业务互斥暂停状态（例如正在执行快照保存或恢复）
    /// </summary>
    public static bool IsPaused
    {
        get
        {
            lock (_pauseLock)
            {
                return _pauseCount > 0;
            }
        }
    }

    /// <summary>
    /// 初始化并加载配置
    /// </summary>
    public static void Initialize(AppSettingsModel settings)
    {
        _isEnabled = settings.AutoHideEnabled;
        _delaySeconds = settings.AutoHideDelaySeconds > 0 ? settings.AutoHideDelaySeconds : 10;
        _showOnDesktopClick = settings.ShowIconsOnDesktopClick;
        _triggerMode = settings.TriggerMode;
        DesktopInteractionHook.TriggerMode = _triggerMode;

        DesktopInteractionHook.DesktopClicked += OnDesktopClicked;

        // 订阅桌面图标显隐物理状态变更，实现按需动态挂载/卸载钩子
        DesktopIconVisibilityService.VisibilityChanged += _ => UpdateHookState();

        // 初始化 500ms 空闲轮询定时器
        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _idleTimer.Tick += OnIdleTimerTick;

        // 预热桌面窗口句柄缓存与当前显隐态
        DesktopIconVisibilityService.WarmUp();

        ApplyState();
    }

    /// <summary>
    /// 动态按需钩子管理：
    /// 核心准则：仅当桌面图标当前“处于隐藏状态”并且“启用了自动隐藏 + 点击桌面恢复”时，才在独立线程安装钩子！
    /// 当图标正常显示时（99.9%的日常使用时间，以及程序冷启动时），全局鼠标钩子完全处于卸载状态，
    /// 确保 0 CPU 消耗、0 鼠标消息拦截，彻底根绝程序启动和日常使用中的任何鼠标卡顿、漂移或干扰！
    /// </summary>
    private static void UpdateHookState()
    {
        bool shouldHook = _isEnabled && _showOnDesktopClick && !DesktopIconVisibilityService.CachedVisibility;
        if (shouldHook)
        {
            if (!DesktopInteractionHook.IsRunning)
            {
                DesktopInteractionHook.Start();
            }
        }
        else
        {
            if (DesktopInteractionHook.IsRunning)
            {
                DesktopInteractionHook.Stop();
            }
        }
    }

    /// <summary>
    /// 业务互斥临时暂停（如恢复图标或保存快照时，暂停自动隐藏防止冲突）
    /// </summary>
    public static void Pause(string reason)
    {
        lock (_pauseLock)
        {
            _pauseCount++;
            if (_pauseCount == 1)
            {
                AppLogger.Info($"[AutoHide] 自动隐藏已暂停，原因: {reason}");
            }
        }
    }

    /// <summary>
    /// 恢复自动隐藏
    /// </summary>
    public static void Resume()
    {
        lock (_pauseLock)
        {
            if (_pauseCount > 0)
            {
                _pauseCount--;
                if (_pauseCount == 0)
                {
                    AppLogger.Info("[AutoHide] 自动隐藏已恢复巡检");
                }
            }
        }
    }

    private static void ApplyState()
    {
        if (_isEnabled)
        {
            _idleTimer?.Start();
            UpdateHookState();
            AppLogger.Info($"[AutoHide] 自动隐藏功能已启用 (超时: {_delaySeconds} 秒, 桌面点击恢复: {_showOnDesktopClick})");
        }
        else
        {
            _idleTimer?.Stop();
            DesktopInteractionHook.Stop();
            AppLogger.Info("[AutoHide] 自动隐藏功能已停用");
        }
    }

    private static void OnIdleTimerTick(object? sender, EventArgs e)
    {
        if (!_isEnabled || IsPaused) return;

        uint idleSeconds = IdleDetectionService.GetIdleTimeSeconds();
        if (idleSeconds >= (uint)_delaySeconds)
        {
            // 空闲时间达标，先查纳秒级内存缓存，若图标已处于隐藏状态则直接略过，杜绝空闲时高频注册表 I/O
            if (DesktopIconVisibilityService.CachedVisibility)
            {
                AppLogger.Info($"[AutoHide] 系统空闲时间达 {idleSeconds} 秒 (>= 阈值 {_delaySeconds} 秒)，触发自动隐藏桌面图标");
                DesktopIconVisibilityService.HideDesktopIcons();
            }
        }
    }

    private static void OnDesktopClicked()
    {
        if (!_isEnabled || !_showOnDesktopClick) return;

        // 点击桌面后，立即显示图标
        DesktopIconVisibilityService.ShowDesktopIcons();
    }

    private static void SaveCurrentSettings()
    {
        try
        {
            var settings = LayoutStorageService.LoadSettings();
            settings.AutoHideEnabled = _isEnabled;
            settings.AutoHideDelaySeconds = _delaySeconds;
            settings.ShowIconsOnDesktopClick = _showOnDesktopClick;
            settings.TriggerMode = _triggerMode;
            LayoutStorageService.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("[AutoHide] 保存设置异常", ex);
        }
    }
}
