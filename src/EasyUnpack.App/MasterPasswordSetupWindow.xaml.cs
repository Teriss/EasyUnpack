using System.Windows;

namespace EasyUnpack.App;

public partial class MasterPasswordSetupWindow : Window
{
    public MasterPasswordSetupWindow() => InitializeComponent();

    public string? MasterPassword { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordInput.Password) || PasswordInput.Password != ConfirmationInput.Password)
        {
            MessageBox.Show(this, "请两次输入相同且非空的主密码。", "EasyUnpack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MasterPassword = PasswordInput.Password;
        DialogResult = true;
    }
}
