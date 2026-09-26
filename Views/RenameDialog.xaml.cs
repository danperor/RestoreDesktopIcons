using System.Windows;

namespace RestoreDesktopIcons.Views;

public partial class RenameDialog : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        TxtName.Text = currentName;
        TxtName.SelectAll();
        Loaded += (_, _) => TxtName.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        NewName = TxtName.Text;
        DialogResult = true;
        Close();
    }
}
