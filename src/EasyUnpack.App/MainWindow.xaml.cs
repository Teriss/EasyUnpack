using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Configuration;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;
using EasyUnpack.Core.Passwords;

namespace EasyUnpack.App;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<string> _inputPaths;

    public MainWindow(IReadOnlyList<string> inputPaths)
    {
        InitializeComponent();
        _inputPaths = inputPaths;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await EasyUnpackSettingsStore.LoadAsync(ApplicationStorage.SettingsPath);
        var scan = await Task.Run(() => ArchiveCandidateDiscovery.Discover(_inputPaths));
        var engines = await Task.Run(() => ArchiveEngineDiscovery.FindAvailable(settings.EnginePaths));
        var tasks = scan.Select(candidate => new CandidateTaskItem(candidate)).ToList();

        CandidateGrid.ItemsSource = tasks;
        var engine = ArchiveEngineFactory.CreatePreferred(engines, settings.PreferredEngine);
        var availableNames = engines
            .Where(descriptor => ArchiveEngineFactory.IsSupported(descriptor.Kind))
            .Select(descriptor => descriptor.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EngineInfoText.Text = $"当前引擎：{engine?.Descriptor.DisplayName ?? "无"}　　可用引擎：{(availableNames.Length == 0 ? "未检测到" : string.Join("、", availableNames))}";

        switch (scan.Count)
        {
            case 0:
                SetStatus("没有发现可处理的压缩包。请检查文件是否完整，或在引擎设置中确认解压工具。", StatusTone.Warning);
                break;
            case var _ when engine is null && engines.Count > 0:
                SetStatus($"发现 {scan.Count} 个压缩包，但检测到的工具暂不具备可用适配器。请安装或配置 7-Zip、WinRAR 或 Bandizip。", StatusTone.Warning);
                break;
            case var _ when engine is null:
                SetStatus($"发现 {scan.Count} 个压缩包，但没有检测到可用解压引擎。请打开引擎设置进行配置。", StatusTone.Error);
                break;
            default:
                SetStatus($"发现 {scan.Count} 个压缩包，正在使用 {engine!.Descriptor.DisplayName} 自动解压。", StatusTone.Info);
                break;
        }

        if (tasks.Count > 0 && engine is not null) await ProcessCandidatesAsync(tasks, engine);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    private void OpenPasswordVault_Click(object sender, RoutedEventArgs e) => new PasswordVaultWindow { Owner = this }.ShowDialog();

    private async Task ProcessCandidatesAsync(IReadOnlyList<CandidateTaskItem> candidates, IArchiveEngine engine)
    {
        PasswordVault vault;
        string? masterPassword = null;
        var maySavePasswords = true;
        try
        {
            var protection = await PasswordVaultStore.GetProtectionAsync(ApplicationStorage.PasswordVaultPath);
            if (protection == PasswordVaultProtection.MasterPassword)
            {
                var unlockDialog = new MasterPasswordPromptWindow { Owner = this };
                if (unlockDialog.ShowDialog() != true || unlockDialog.MasterPassword is null)
                {
                    vault = new PasswordVault();
                    maySavePasswords = false;
                }
                else
                {
                    masterPassword = unlockDialog.MasterPassword;
                    vault = await PasswordVaultStore.LoadAsync(ApplicationStorage.PasswordVaultPath, masterPassword);
                }
            }
            else
            {
                vault = await PasswordVaultStore.LoadAsync(ApplicationStorage.PasswordVaultPath, null);
            }
        }
        catch (Exception)
        {
            vault = new PasswordVault();
            maySavePasswords = false;
            SetStatus("密码库无法打开，本次运行中输入的新密码不会被保存。", StatusTone.Warning);
        }

        var service = new ArchiveExtractionService(engine, new WindowsArchiveSourceRecycler());
        var succeeded = 0;
        var failed = 0;
        var warnings = 0;
        foreach (var task in candidates)
        {
            var candidate = task.Candidate;
            try
            {
                task.Status = "正在解压";
                SetStatus($"正在解压：{candidate.LogicalName}", StatusTone.Info);
                var result = await service.ExtractAsync(candidate, vault.GetCandidates());
                ApplySuccess(task, result, ref warnings);
                succeeded++;
            }
            catch (ArchivePasswordRequiredException)
            {
                var dialog = new PasswordPromptWindow(candidate.Path) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Password is null)
                {
                    task.Status = "需要密码";
                    failed++;
                    continue;
                }

                try
                {
                    var result = await service.ExtractAsync(candidate, vault.GetCandidates().Append(dialog.Password).ToArray());
                    vault.RecordSuccessfulPassword(dialog.Password);
                    if (maySavePasswords) await PasswordVaultStore.SaveAsync(vault, ApplicationStorage.PasswordVaultPath, masterPassword);
                    ApplySuccess(task, result, ref warnings);
                    succeeded++;
                }
                catch (ArchivePasswordRequiredException)
                {
                    task.Status = "密码不正确";
                    failed++;
                }
                catch (Exception)
                {
                    task.Status = "解压失败，源文件已保留";
                    failed++;
                }
            }
            catch (Exception)
            {
                task.Status = "解压失败，源文件已保留";
                failed++;
            }
        }

        if (failed > 0)
        {
            SetStatus($"已完成 {succeeded} 个任务，{failed} 个任务未完成；失败任务的源压缩包已保留。", StatusTone.Error);
        }
        else if (warnings > 0)
        {
            SetStatus($"已完成 {succeeded} 个解压任务，其中 {warnings} 个任务的源文件未能移入回收站。", StatusTone.Warning);
        }
        else
        {
            SetStatus($"已完成 {succeeded} 个解压任务。", StatusTone.Success);
        }
    }

    private static void ApplySuccess(CandidateTaskItem task, ExtractionResult result, ref int warnings)
    {
        task.OutputDirectory = result.OutputDirectory;
        if (result.SourceRecycled)
        {
            task.Status = "已完成";
            return;
        }

        task.Status = "已完成，源文件未回收";
        warnings++;
    }

    private void SetStatus(string text, StatusTone tone)
    {
        StatusText.Text = text;
        var (brushKey, icon) = tone switch
        {
            StatusTone.Success => ("SuccessBrush", "\uE73E"),
            StatusTone.Warning => ("WarningBrush", "\uE7BA"),
            StatusTone.Error => ("ErrorBrush", "\uEA39"),
            _ => ("InfoBrush", "\uE895"),
        };
        var brush = (Brush)FindResource(brushKey);
        StatusPanel.BorderBrush = brush;
        StatusIcon.Foreground = brush;
        StatusIcon.Text = icon;
    }

    private enum StatusTone
    {
        Info,
        Success,
        Warning,
        Error,
    }

    private sealed class CandidateTaskItem(ArchiveCandidate candidate) : INotifyPropertyChanged
    {
        private string _status = "等待处理";
        private string? _outputDirectory;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ArchiveCandidate Candidate { get; } = candidate;
        public string LogicalName => Candidate.LogicalName;
        public string Format => Candidate.Format.ToString();
        public string Path => Candidate.Path;

        public string? OutputDirectory
        {
            get => _outputDirectory;
            set => SetField(ref _outputDirectory, value);
        }

        public string Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
