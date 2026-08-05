using System.IO;
using System.Windows;

namespace EasyUnpack.App;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string archivePath, bool previousAttemptFailed = false)
    {
        InitializeComponent();
        ArchiveNameText.Text = Path.GetFileName(archivePath);
        RetryMessage.Visibility = previousAttemptFailed ? Visibility.Visible : Visibility.Collapsed;
    }

    public string? Password { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e) => PasswordInput.Focus();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordInput.Password)) return;
        Password = PasswordInput.Password;
        DialogResult = true;
    }
}
