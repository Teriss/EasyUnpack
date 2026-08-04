using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        var descriptors = await Task.Run(() => ArchiveEngineDiscovery.FindAvailable(settings.EnginePaths));
        var engines = ArchiveEngineFactory.CreateAll(descriptors, settings.PreferredEngine);
        var scan = await ArchiveCandidateDiscovery.DiscoverAsync(_inputPaths, engines);
        var tasks = scan.Select(candidate => new CandidateTaskItem(candidate)).ToList();

        CandidateGrid.ItemsSource = tasks;
        var engine = engines.FirstOrDefault();
        var availableNames = descriptors
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
            case var _ when engine is null && descriptors.Count > 0:
                SetStatus($"发现 {scan.Count} 个压缩包，但检测到的工具暂不具备可用适配器。请安装或配置 7-Zip、WinRAR 或 Bandizip。", StatusTone.Warning);
                break;
            case var _ when engine is null:
                SetStatus($"发现 {scan.Count} 个压缩包，但没有检测到可用解压引擎。请打开引擎设置进行配置。", StatusTone.Error);
                break;
            default:
                SetStatus($"发现 {scan.Count} 个压缩包，正在使用 {engine!.Descriptor.DisplayName} 自动解压。", StatusTone.Info);
                break;
        }

        if (tasks.Count > 0 && engines.Count > 0) await ProcessCandidatesAsync(tasks, engines);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    private void OpenPasswordVault_Click(object sender, RoutedEventArgs e) => new PasswordVaultWindow { Owner = this }.ShowDialog();

    private async Task ProcessCandidatesAsync(IReadOnlyList<CandidateTaskItem> candidates, IReadOnlyList<IArchiveEngine> engines)
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

        var service = new ArchiveExtractionService(engines, new WindowsArchiveSourceRecycler());
        var succeeded = 0;
        var failed = 0;
        var warnings = 0;
        foreach (var task in candidates)
        {
            var candidate = task.Candidate;
            var progress = new Progress<ExtractionProgress>(task.UpdateProgress);
            try
            {
                task.Status = "正在解压";
                task.ResetProgress();
                SetStatus($"正在解压：{candidate.LogicalName}", StatusTone.Info);
                var result = await service.ExtractAsync(candidate, vault.GetCandidates(), progress: progress);
                ApplySuccess(task, result, ref warnings);
                succeeded++;
            }
            catch (ArchivePasswordRequiredException)
            {
                var dialog = new PasswordPromptWindow(candidate.Path) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Password is null)
                {
                    task.Status = "需要密码";
                    task.ClearProgress();
                    failed++;
                    continue;
                }

                try
                {
                    task.Status = "正在解压";
                    task.ResetProgress();
                    var result = await service.ExtractAsync(candidate, vault.GetCandidates().Append(dialog.Password).ToArray(), progress: progress);
                    vault.RecordSuccessfulPassword(dialog.Password);
                    if (maySavePasswords) await PasswordVaultStore.SaveAsync(vault, ApplicationStorage.PasswordVaultPath, masterPassword);
                    ApplySuccess(task, result, ref warnings);
                    succeeded++;
                }
                catch (ArchivePasswordRequiredException)
                {
                    task.Status = "密码不正确";
                    task.ClearProgress();
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
                task.ClearProgress();
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
        task.ClearProgress();
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
        StatusIcon.Visibility = Visibility.Visible;
        ActivityProgress.Visibility = tone == StatusTone.Info ? Visibility.Visible : Visibility.Collapsed;
        if (tone == StatusTone.Info)
        {
            StartStatusAnimation();
        }
        else
        {
            StopStatusAnimation();
        }
    }

    private void StartStatusAnimation()
    {
        if (StatusIcon.RenderTransform is not RotateTransform rotation) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private void StopStatusAnimation()
    {
        if (StatusIcon.RenderTransform is not RotateTransform rotation) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        rotation.Angle = 0;
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
        private double _progressPercent;
        private bool _isProgressIndeterminate;
        private string _progressText = "等待";

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

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetField(ref _progressPercent, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            private set => SetField(ref _isProgressIndeterminate, value);
        }

        public string ProgressText
        {
            get => _progressText;
            private set => SetField(ref _progressText, value);
        }

        public void ResetProgress()
        {
            ProgressPercent = 0;
            IsProgressIndeterminate = true;
            ProgressText = "正在启动引擎…";
        }

        public void UpdateProgress(ExtractionProgress progress)
        {
            IsProgressIndeterminate = true;
            ProgressText = $"已写入 {progress.FileCount:N0} 个文件 · {FormatBytes(progress.BytesWritten)} · {FormatElapsed(progress.Elapsed)}";
        }

        public void ClearProgress()
        {
            IsProgressIndeterminate = false;
            ProgressPercent = 0;
        }

        private static string FormatBytes(long bytes) => bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
            >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
            >= 1024L => $"{bytes / 1024d:0} KB",
            _ => $"{bytes:N0} B",
        };

        private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
