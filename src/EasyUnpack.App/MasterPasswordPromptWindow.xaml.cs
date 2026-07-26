using System.Windows;

namespace EasyUnpack.App;

public partial class MasterPasswordPromptWindow : Window
{
    public MasterPasswordPromptWindow() => InitializeComponent();

    public string? MasterPassword { get; private set; }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordInput.Password)) return;
        MasterPassword = PasswordInput.Password;
        DialogResult = true;
    }
}
