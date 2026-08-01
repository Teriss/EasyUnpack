using System.Windows;

namespace EasyUnpack.App;

public partial class MasterPasswordPromptWindow : Window
{
    public MasterPasswordPromptWindow() => InitializeComponent();

    public string? MasterPassword { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e) => PasswordInput.Focus();

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordInput.Password)) return;
        MasterPassword = PasswordInput.Password;
        DialogResult = true;
    }
}
