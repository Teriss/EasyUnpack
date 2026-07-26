using System.IO;
using System.Windows;
using EasyUnpack.Core.Configuration;
using EasyUnpack.Core.Engines;
using Microsoft.Win32;

namespace EasyUnpack.App;

public partial class SettingsWindow : Window
{
    private EasyUnpackSettings _settings = new();
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();
        EngineKindPicker.ItemsSource = Enum.GetValues<ArchiveEngineKind>();
        EngineKindPicker.SelectedItem = ArchiveEngineKind.SevenZip;
        EngineKindPicker.SelectionChanged += (_, _) => LoadManualPath();
        PreferredEnginePicker.ItemsSource = new object[] { "自动选择" }
            .Concat(Enum.GetValues<ArchiveEngineKind>().Where(ArchiveEngineFactory.IsSupported).Cast<object>())
            .ToArray();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await EasyUnpackSettingsStore.LoadAsync(ApplicationStorage.SettingsPath);
        PreferredEnginePicker.SelectedItem = (object?)_settings.PreferredEngine ?? "自动选择";
        LoadManualPath();
        await RefreshEnginesAsync();
        _loaded = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ManualPathBox.Text = dialog.FileName;
    }

    private async void SavePath_Click(object sender, RoutedEventArgs e)
    {
        if (EngineKindPicker.SelectedItem is not ArchiveEngineKind kind) return;
        if (!File.Exists(ManualPathBox.Text))
        {
            MessageBox.Show(this, "请选择有效的解压工具可执行文件。", "EasyUnpack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.EnginePaths[kind] = Path.GetFullPath(ManualPathBox.Text);
        await EasyUnpackSettingsStore.SaveAsync(_settings, ApplicationStorage.SettingsPath);
        await RefreshEnginesAsync();
    }

    private async void RemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (EngineKindPicker.SelectedItem is not ArchiveEngineKind kind) return;
        _settings.EnginePaths.Remove(kind);
        ManualPathBox.Clear();
        await EasyUnpackSettingsStore.SaveAsync(_settings, ApplicationStorage.SettingsPath);
        await RefreshEnginesAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshEnginesAsync();

    private async void PreferredEnginePicker_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings = _settings with { PreferredEngine = PreferredEnginePicker.SelectedItem as ArchiveEngineKind? };
        await EasyUnpackSettingsStore.SaveAsync(_settings, ApplicationStorage.SettingsPath);
    }

    private void OpenPasswordVault_Click(object sender, RoutedEventArgs e) => new PasswordVaultWindow { Owner = this }.ShowDialog();

    private void LoadManualPath()
    {
        if (EngineKindPicker.SelectedItem is ArchiveEngineKind kind && _settings.EnginePaths.TryGetValue(kind, out var path)) ManualPathBox.Text = path;
        else ManualPathBox.Clear();
    }

    private async Task RefreshEnginesAsync()
    {
        EngineList.ItemsSource = await Task.Run(() => ArchiveEngineDiscovery.FindAvailable(_settings.EnginePaths));
    }
}
