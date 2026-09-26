using System.Windows;
using System.Windows.Input;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;

namespace RestoreDesktopIcons.Views;

public enum ModernDialogType
{
    Information,
    Warning,
    Error,
    Success,
    Question,
    DangerConfirm
}

public partial class ModernMessageBox : Window
{
    private static readonly System.Windows.Media.Geometry InfoIconPath = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
    private static readonly System.Windows.Media.Geometry WarningIconPath = System.Windows.Media.Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
    private static readonly System.Windows.Media.Geometry ErrorIconPath = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z");
    private static readonly System.Windows.Media.Geometry SuccessIconPath = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z");
    private static readonly System.Windows.Media.Geometry QuestionIconPath = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.67z");
    private static readonly System.Windows.Media.Geometry TrashIconPath = System.Windows.Media.Geometry.Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z");

    public bool UserConfirmed { get; private set; }

    public ModernMessageBox(
        string message,
        string title,
        ModernDialogType type,
        string confirmText = "确定",
        string cancelText = "取消")
    {
        InitializeComponent();

        TxtTitle.Text = title;
        Title = title;
        TxtMessage.Text = message;
        BtnConfirm.Content = confirmText;
        BtnCancel.Content = cancelText;

        ApplyDialogType(type);
    }

    private static MediaBrush ParseBrush(string hex)
    {
        return (MediaBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
    }

    private void ApplyDialogType(ModernDialogType type)
    {
        switch (type)
        {
            case ModernDialogType.DangerConfirm:
                IconBadge.Background = ParseBrush("#FEE2E2");
                IconBadge.BorderBrush = ParseBrush("#FECACA");
                IconPath.Fill = ParseBrush("#DC2626");
                IconPath.Data = TrashIconPath;
                BtnConfirm.Style = (Style)Resources["DialogDangerButtonStyle"];
                BtnCancel.Visibility = Visibility.Visible;
                break;

            case ModernDialogType.Question:
                IconBadge.Background = ParseBrush("#EFF6FF");
                IconBadge.BorderBrush = ParseBrush("#DBEAFE");
                IconPath.Fill = ParseBrush("#2563EB");
                IconPath.Data = QuestionIconPath;
                BtnConfirm.Style = (Style)Resources["DialogPrimaryButtonStyle"];
                BtnCancel.Visibility = Visibility.Visible;
                break;

            case ModernDialogType.Warning:
                IconBadge.Background = ParseBrush("#FFFBEB");
                IconBadge.BorderBrush = ParseBrush("#FDE68A");
                IconPath.Fill = ParseBrush("#D97706");
                IconPath.Data = WarningIconPath;
                BtnConfirm.Style = (Style)Resources["DialogPrimaryButtonStyle"];
                BtnCancel.Visibility = Visibility.Collapsed;
                break;

            case ModernDialogType.Error:
                IconBadge.Background = ParseBrush("#FEE2E2");
                IconBadge.BorderBrush = ParseBrush("#FECACA");
                IconPath.Fill = ParseBrush("#DC2626");
                IconPath.Data = ErrorIconPath;
                BtnConfirm.Style = (Style)Resources["DialogDangerButtonStyle"];
                BtnCancel.Visibility = Visibility.Collapsed;
                break;

            case ModernDialogType.Success:
                IconBadge.Background = ParseBrush("#DCFCE7");
                IconBadge.BorderBrush = ParseBrush("#BBF7D0");
                IconPath.Fill = ParseBrush("#16A34A");
                IconPath.Data = SuccessIconPath;
                BtnConfirm.Style = (Style)Resources["DialogPrimaryButtonStyle"];
                BtnCancel.Visibility = Visibility.Collapsed;
                break;

            case ModernDialogType.Information:
            default:
                IconBadge.Background = ParseBrush("#EFF6FF");
                IconBadge.BorderBrush = ParseBrush("#DBEAFE");
                IconPath.Fill = ParseBrush("#2563EB");
                IconPath.Data = InfoIconPath;
                BtnConfirm.Style = (Style)Resources["DialogPrimaryButtonStyle"];
                BtnCancel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = true;
        DialogResult = true;
        Close();
    }

    // ==================== 静态便捷调用方法 ====================

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner != null && owner.IsVisible) return owner;
        if (WpfApplication.Current != null)
        {
            foreach (Window w in WpfApplication.Current.Windows)
            {
                if (w.IsActive && w.IsVisible) return w;
            }
            if (WpfApplication.Current.MainWindow != null && WpfApplication.Current.MainWindow.IsVisible)
            {
                return WpfApplication.Current.MainWindow;
            }
        }
        return null;
    }

    /// <summary>
    /// 弹出危险操作确认框（如删除快照），确认按钮为红色危险样式
    /// </summary>
    public static bool ConfirmDelete(string message, string title = "确认删除", string confirmText = "确认删除", Window? owner = null)
    {
        if (WpfApplication.Current != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            return WpfApplication.Current.Dispatcher.Invoke(() => ConfirmDelete(message, title, confirmText, owner));
        }

        var dialog = new ModernMessageBox(message, title, ModernDialogType.DangerConfirm, confirmText, "取消");
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner != null)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出通用询问/确认框
    /// </summary>
    public static bool Confirm(string message, string title = "请确认", string confirmText = "确定", string cancelText = "取消", Window? owner = null)
    {
        if (WpfApplication.Current != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            return WpfApplication.Current.Dispatcher.Invoke(() => Confirm(message, title, confirmText, cancelText, owner));
        }

        var dialog = new ModernMessageBox(message, title, ModernDialogType.Question, confirmText, cancelText);
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner != null)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 弹出普通提示框 (Information)
    /// </summary>
    public static void ShowInfo(string message, string title = "提示", Window? owner = null)
    {
        if (WpfApplication.Current != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => ShowInfo(message, title, owner));
            return;
        }

        var dialog = new ModernMessageBox(message, title, ModernDialogType.Information, "我知道了");
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner != null)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }

    /// <summary>
    /// 弹出警告框 (Warning)
    /// </summary>
    public static void ShowWarning(string message, string title = "警告", Window? owner = null)
    {
        if (WpfApplication.Current != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => ShowWarning(message, title, owner));
            return;
        }

        var dialog = new ModernMessageBox(message, title, ModernDialogType.Warning, "我知道了");
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner != null)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }

    /// <summary>
    /// 弹出错误框 (Error)
    /// </summary>
    public static void ShowError(string message, string title = "错误", Window? owner = null)
    {
        if (WpfApplication.Current != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => ShowError(message, title, owner));
            return;
        }

        var dialog = new ModernMessageBox(message, title, ModernDialogType.Error, "确定");
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner != null)
        {
            dialog.Owner = resolvedOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }
}
