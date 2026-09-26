using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using RestoreDesktopIcons.Models;
using RestoreDesktopIcons.Services;

namespace RestoreDesktopIcons.ViewModels;

public class MainViewModel : ViewModelBase
{
    private LayoutSnapshotModel? _selectedSnapshot;
    private bool _isAutoArrangeActive;
    private bool _areIconsVisible = true;
    private string _topologySummary = string.Empty;
    private string _statusMessage = "就绪";
    private string _clampedNotice = string.Empty;
    private readonly DispatcherTimer _statusTimer;

    public ObservableCollection<LayoutSnapshotModel> Snapshots { get; } = [];

    public LayoutSnapshotModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (SetProperty(ref _selectedSnapshot, value))
            {
                OnPropertyChanged(nameof(CanRestoreOrDelete));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRestoreOrDelete));
                (SaveLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RestoreLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DeleteLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RenameLayoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RefreshStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRestoreOrDelete => SelectedSnapshot != null && !IsBusy;

    public bool IsAutoArrangeActive
    {
        get => _isAutoArrangeActive;
        set => SetProperty(ref _isAutoArrangeActive, value);
    }

    public bool AreIconsVisible
    {
        get => _areIconsVisible;
        set => SetProperty(ref _areIconsVisible, value);
    }

    public bool IsAutoHideEnabled
    {
        get => AutoHideCoordinator.IsEnabled;
        set
        {
            if (AutoHideCoordinator.IsEnabled != value)
            {
                AutoHideCoordinator.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string TopologySummary
    {
        get => _topologySummary;
        set => SetProperty(ref _topologySummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ClampedNotice
    {
        get => _clampedNotice;
        set
        {
            if (SetProperty(ref _clampedNotice, value))
            {
                OnPropertyChanged(nameof(HasClampedNotice));
            }
        }
    }

    public bool HasClampedNotice => !string.IsNullOrEmpty(ClampedNotice);

    private bool _useRealDesktopCapture = true;
    public bool UseRealDesktopCapture
    {
        get => _useRealDesktopCapture;
        set
        {
            if (_useRealDesktopCapture != value)
            {
                _useRealDesktopCapture = value;
                OnPropertyChanged();
                LayoutStorageService.SaveSettings(new AppSettingsModel { UseRealDesktopCapture = value });
            }
        }
    }

    public ICommand SaveLayoutCommand { get; }
    public ICommand RestoreLayoutCommand { get; }
    public ICommand DeleteLayoutCommand { get; }
    public ICommand RenameLayoutCommand { get; }
    public ICommand ViewScreenshotCommand { get; }
    public ICommand ViewSpecificScreenshotCommand { get; }
    public ICommand OpenScreenshotFolderCommand { get; }
    public ICommand ViewLogFileCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public MainViewModel()
    {
        SaveLayoutCommand = new RelayCommand(SaveLayout, () => !IsBusy);
        RestoreLayoutCommand = new RelayCommand(RestoreLayout, () => CanRestoreOrDelete);
        DeleteLayoutCommand = new RelayCommand(DeleteLayout, () => CanRestoreOrDelete);
        RenameLayoutCommand = new RelayCommand(RenameLayout, () => CanRestoreOrDelete);
        ViewScreenshotCommand = new RelayCommand(ViewScreenshot, () => CanRestoreOrDelete);
        ViewSpecificScreenshotCommand = new RelayCommand(param => ViewSpecificScreenshot(param), _ => CanRestoreOrDelete);
        OpenScreenshotFolderCommand = new RelayCommand(OpenScreenshotFolder);
        ViewLogFileCommand = new RelayCommand(ViewLogFile);
        RefreshStatusCommand = new RelayCommand(RefreshStatus, () => !IsBusy);

        // 加载用户设置偏好
        var userSettings = LayoutStorageService.LoadSettings();
        _useRealDesktopCapture = userSettings.UseRealDesktopCapture;

        // 加载历史快照
        var savedList = LayoutStorageService.LoadSnapshots();
        foreach (var item in savedList)
        {
            Snapshots.Add(item);
        }

        if (Snapshots.Count > 0)
        {
            SelectedSnapshot = Snapshots[0];
        }

        RefreshStatus();

        // 订阅自动隐藏状态变更以实时刷新 UI
        AutoHideCoordinator.StateChanged += () =>
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IsAutoHideEnabled));
            });
        };

        // 订阅桌面图标显隐实时变更事件，实现 UI 指示灯与状态 0 延迟秒级响应
        DesktopIconVisibilityService.VisibilityChanged += (visible) =>
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AreIconsVisible = visible;
            });
        };

        // 启动定时巡检（每 3 秒刷新自动排列指示灯与屏幕分辨率状态）
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
    }

    public void RefreshStatus()
    {
        Task.Run(() =>
        {
            try
            {
                bool autoArrange = DesktopShellCsWin32Service.IsAutoArrangeEnabled();
                bool iconsVisible = DesktopIconVisibilityService.AreDesktopIconsVisible();
                var topo = TopologyService.GetCurrentTopology();
                string summary = $"当前屏幕: {topo.Summary}";

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    IsAutoArrangeActive = autoArrange;
                    AreIconsVisible = iconsVisible;
                    TopologySummary = summary;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn("定时刷新自动排列或屏幕拓扑状态异常", ex);
            }
        });
    }


    private async void SaveLayout()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在读取桌面图标并生成原生快照...";
        AutoHideCoordinator.Pause("保存快照");

        try
        {
            await Task.Run(() =>
            {
                var topo = TopologyService.GetCurrentTopology();

                // 截图安全保障：若桌面图标被隐藏，先临时恢复显示再截图
                bool wasHidden = DesktopIconVisibilityService.EnsureIconsVisibleForCapture();

                try
                {
                    var (icons, viewMode, spX, spY) = DesktopShellCsWin32Service.CaptureDesktopIcons();

                    if (icons.Count == 0)
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = "保存失败：未能抓取到桌面图标，请确认桌面是否正常显示。";
                        });
                        AppLogger.Warn("抓取桌面图标返回空列表。");
                        return;
                    }

                    string defaultName = $"{DateTime.Now:yyyy-MM-dd HH:mm} ({topo.Summary})";
                    var snapshotId = Guid.NewGuid();

                    var engine = UseRealDesktopCapture ? DesktopCaptureEngine.RealHardwareDwm : DesktopCaptureEngine.SyntheticHologram;
                    var (panoramicPath, monitorPaths) = ScreenCaptureService.CaptureDesktopMonitors(snapshotId, topo, icons, engine);

                    var snapshot = new LayoutSnapshotModel
                    {
                        Id = snapshotId,
                        Name = defaultName,
                        CreatedAt = DateTimeOffset.Now,
                        Topology = topo,
                        ViewMode = viewMode,
                        GridSpacingX = spX,
                        GridSpacingY = spY,
                        Icons = icons,
                        ScreenshotPath = panoramicPath,
                        MonitorScreenshots = monitorPaths
                    };

                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        Snapshots.Insert(0, snapshot);
                        SelectedSnapshot = snapshot;
                        LayoutStorageService.SaveSnapshots(Snapshots.ToList());
                        string engineDesc = UseRealDesktopCapture ? "原生图层穿透直取" : "纯净全息重构";
                        string hiddenNote = wasHidden ? "（已自动临时恢复图标显示后截图）" : "";
                        StatusMessage = $"成功保存布局快照「{snapshot.Name}」，包含 {icons.Count} 个图标（已使用【{engineDesc}】引擎生成截图）。{hiddenNote}";
                        ClampedNotice = string.Empty;
                    });
                }
                finally
                {
                    // 截图完成后恢复图标隐藏状态
                    DesktopIconVisibilityService.RestoreHiddenStateAfterCapture(wasHidden);
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("保存桌面图标布局发生异常", ex);
            StatusMessage = $"保存出错: {ex.Message}";
        }
        finally
        {
            AutoHideCoordinator.Resume();
            IsBusy = false;
        }
    }

    private async void RestoreLayout()
    {
        if (SelectedSnapshot == null || IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在执行四维全息恢复...";
        AutoHideCoordinator.Pause("恢复布局");

        try
        {
            // 确保在执行图标恢复时，桌面图标已被显示，防止在隐藏模式下恢复位置异常
            DesktopIconVisibilityService.ShowDesktopIcons();

            var targetSnapshot = SelectedSnapshot;
            await Task.Run(() =>
            {
                // 工业级灾备：恢复前自动保存当前现场快照，保障用户拥有 100% 无损撤销/回退后路
                try
                {
                    var currentTopo = TopologyService.GetCurrentTopology();
                    var (currentIcons, currentViewMode, currentSpX, currentSpY) = DesktopShellCsWin32Service.CaptureDesktopIcons();
                    if (currentIcons.Count > 0)
                    {
                        var autoBackupId = Guid.NewGuid();
                        var (autoShot, autoMonitors) = ScreenCaptureService.CaptureDesktopMonitors(autoBackupId, currentTopo, currentIcons);
                        var autoBackup = new LayoutSnapshotModel
                        {
                            Id = autoBackupId,
                            Name = $"[自动现场备份] 恢复前 ({DateTime.Now:yyyy-MM-dd HH:mm:ss})",
                            CreatedAt = DateTimeOffset.Now,
                            Topology = currentTopo,
                            ViewMode = currentViewMode,
                            GridSpacingX = currentSpX,
                            GridSpacingY = currentSpY,
                            Icons = currentIcons,
                            ScreenshotPath = autoShot,
                            MonitorScreenshots = autoMonitors
                        };
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            Snapshots.Insert(0, autoBackup);
                            LayoutStorageService.SaveSnapshots(Snapshots.ToList());
                        });
                        AppLogger.Info($"已自动创建恢复前现场灾备快照: {autoBackup.Name}");
                    }
                }
                catch (Exception exBackup)
                {
                    AppLogger.Warn("创建现场灾备快照失败 (不中断主恢复流程)", exBackup);
                }

                var topo = TopologyService.GetCurrentTopology();
                var (restoredCount, clampedCount, message) = DesktopShellCsWin32Service.RestoreDesktopIcons(targetSnapshot, topo);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = message;
                    if (clampedCount > 0)
                    {
                        ClampedNotice = $"[引力吸附防护生效: {clampedCount} 个幽灵图标已安全投影至屏幕工作区边缘]";
                    }
                    else
                    {
                        ClampedNotice = string.Empty;
                    }
                });
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("恢复桌面图标布局发生异常", ex);
            StatusMessage = $"恢复异常: {ex.Message}";
        }
        finally
        {
            AutoHideCoordinator.Resume();
            IsBusy = false;
        }
    }


    private void DeleteLayout()
    {
        if (SelectedSnapshot == null) return;

        try
        {
            var snapshotToDelete = SelectedSnapshot;
            var result = MessageBox.Show(
                $"确定要删除快照「{snapshotToDelete.Name}」吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Snapshots.Remove(snapshotToDelete);
                SelectedSnapshot = Snapshots.FirstOrDefault();
                LayoutStorageService.SaveSnapshots(Snapshots.ToList());
                ScreenCaptureService.DeleteSnapshotScreenshots(snapshotToDelete);
                StatusMessage = $"已删除快照「{snapshotToDelete.Name}」。";
                AppLogger.Info($"用户删除了快照「{snapshotToDelete.Name}」。");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("删除快照失败", ex);
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void RenameLayout()
    {
        if (SelectedSnapshot == null) return;

        try
        {
            // 简易直接的重命名输入框 (或者弹出对话框)
            var inputDialog = new Views.RenameDialog(SelectedSnapshot.Name);
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.NewName))
            {
                string oldName = SelectedSnapshot.Name;
                SelectedSnapshot.Name = inputDialog.NewName.Trim();
                // 触发列表刷新
                int idx = Snapshots.IndexOf(SelectedSnapshot);
                if (idx >= 0)
                {
                    var temp = SelectedSnapshot;
                    Snapshots.RemoveAt(idx);
                    Snapshots.Insert(idx, temp);
                    SelectedSnapshot = temp;
                }
                LayoutStorageService.SaveSnapshots(Snapshots.ToList());
                StatusMessage = $"已重命名为「{SelectedSnapshot.Name}」。";
                AppLogger.Info($"快照重命名:「{oldName}」->「{SelectedSnapshot.Name}」。");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("重命名快照失败", ex);
            StatusMessage = $"重命名失败: {ex.Message}";
        }
    }

    private void ViewScreenshot()
    {
        ViewSpecificScreenshot(null);
    }

    private void ViewSpecificScreenshot(object? param)
    {
        if (SelectedSnapshot == null) return;

        string? targetPath = null;
        string tag = param as string ?? string.Empty;

        if (!string.IsNullOrEmpty(tag))
        {
            // 如果指定了屏幕参数（例如 DISPLAY1, DISPLAY2 或完整设备名）
            foreach (var kv in SelectedSnapshot.MonitorScreenshots)
            {
                if (kv.Key.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    string full = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, kv.Value);
                    if (System.IO.File.Exists(full))
                    {
                        targetPath = full;
                        break;
                    }
                }
            }
        }

        // 若未指定或未找到特定屏幕，则优先使用全景图或主图
        if (string.IsNullOrEmpty(targetPath))
        {
            targetPath = SelectedSnapshot.FullScreenshotPath;
        }

        // 若全景图不存在但有分屏截图，打开第 1 个分屏截图
        if (string.IsNullOrEmpty(targetPath) || !System.IO.File.Exists(targetPath))
        {
            var all = SelectedSnapshot.GetAllExistingScreenshotFullPaths();
            targetPath = all.FirstOrDefault();
        }

        if (!string.IsNullOrEmpty(targetPath) && System.IO.File.Exists(targetPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"打开桌面截图失败: {targetPath}", ex);
                MessageBox.Show($"无法打开截图文件: {ex.Message}", "查看截图", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            MessageBox.Show("该快照尚未包含截图文件（可能是旧版本生成的快照）。\n重新点击【保存当前布局】即可生成带独立分屏及无遮挡纯净截图的新快照。", "查看截图", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenScreenshotFolder()
    {
        try
        {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            if (!System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("打开截图目录失败", ex);
        }
    }

    private void ViewLogFile()
    {
        try
        {
            string path = AppLogger.LogFilePath;
            if (System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"运行日志文件尚未生成: {path}", "查看运行日志", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("打开运行日志文件失败", ex);
            MessageBox.Show($"打开运行日志失败: {ex.Message}", "查看运行日志", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

