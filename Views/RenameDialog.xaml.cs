using System.Windows;
using System.Windows.Input;

namespace RestoreDesktopIcons.Views;

public partial class RenameDialog : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameDialog(string currentName, Window? owner = null)
    {
        InitializeComponent();

        if (owner != null && owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else if (System.Windows.Application.Current?.MainWindow?.IsVisible == true)
        {
            Owner = System.Windows.Application.Current.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        TxtName.Text = currentName;
        TxtName.SelectAll();
        Loaded += (_, _) => TxtName.Focus();
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
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        NewName = TxtName.Text.Trim();
        DialogResult = true;
        Close();
    }
}
