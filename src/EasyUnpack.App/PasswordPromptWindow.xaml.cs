using System.IO;
using System.Windows;

namespace EasyUnpack.App;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string archivePath)
    {
        InitializeComponent();
        ArchiveNameText.Text = Path.GetFileName(archivePath);
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
