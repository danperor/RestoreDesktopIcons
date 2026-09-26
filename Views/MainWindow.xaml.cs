using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RestoreDesktopIcons.Services;
using RestoreDesktopIcons.ViewModels;

namespace RestoreDesktopIcons.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void RequestExplicitExit()
    {
        _isExplicitExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitExit && System.Windows.Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            var settings = LayoutStorageService.LoadSettings();
            if (settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                AppLogger.Info("[MainWindow] 主窗口已最小化到系统托盘");
                return;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        EmergencyRecoveryService.RestoreDesktopIconsIfHidden("主窗口销毁退出");
        EmergencyRecoveryService.MarkCleanExit();
        base.OnClosed(e);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CanRestoreOrDelete)
        {
            if (vm.RestoreLayoutCommand.CanExecute(null))
            {
                vm.RestoreLayoutCommand.Execute(null);
            }
        }
    }
}
