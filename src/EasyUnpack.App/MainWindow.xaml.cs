using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<TaskRow> _rows = [];
    private readonly CancellationTokenSource _windowCancellation = new();
    private CancellationTokenSource? _currentTaskCancellation;
    private Task? _processingTask;
    private bool _allowClose;

    public MainWindow(IReadOnlyList<string> inputPaths)
    {
        InitializeComponent();
        _inputPaths = inputPaths;
        CandidateGrid.ItemsSource = _rows;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await EasyUnpackSettingsStore.LoadAsync(ApplicationStorage.SettingsPath);
        var descriptors = await Task.Run(() => ArchiveEngineDiscovery.FindAvailable(settings.EnginePaths));
        var engines = ArchiveEngineFactory.CreateAll(descriptors, settings.PreferredEngine);
        var scan = await ArchiveCandidateDiscovery.DiscoverAsync(_inputPaths, engines, _windowCancellation.Token);
        var tasks = scan.Select(candidate => new CandidateTaskItem(candidate)).ToList();
        foreach (var task in tasks) _rows.Add(task);

        var engine = engines.FirstOrDefault();
        var availableNames = descriptors.Where(descriptor => ArchiveEngineFactory.IsSupported(descriptor.Kind)).Select(descriptor => descriptor.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        EngineInfoText.Text = $"当前引擎：{engine?.Descriptor.DisplayName ?? "无"}    可用引擎：{(availableNames.Length == 0 ? "未检测到" : string.Join("、", availableNames))}";
        if (tasks.Count == 0) SetStatus("没有发现可处理的压缩包。", StatusTone.Warning);
        else if (engine is null) SetStatus("没有可用的解压引擎，请打开引擎设置。", StatusTone.Error);
        else SetStatus($"发现 {tasks.Count} 个压缩包，正在按顺序处理。", StatusTone.Info);

        if (tasks.Count > 0 && engines.Count > 0)
        {
            _processingTask = ProcessCandidatesAsync(tasks, engines);
            await _processingTask;
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void OpenPasswordVault_Click(object sender, RoutedEventArgs e) => new PasswordVaultWindow { Owner = this }.ShowDialog();
    private void CancelCurrentTask_Click(object sender, RoutedEventArgs e) => _currentTaskCancellation?.Cancel();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        _windowCancellation.Cancel();
        _currentTaskCancellation?.Cancel();
        if (_processingTask is { IsCompleted: false })
        {
            e.Cancel = true;
            SetStatus("正在停止解压进程并保留未完成文件...", StatusTone.Info);
            try { await _processingTask; } catch (OperationCanceledException) { }
            _allowClose = true;
            Close();
        }
    }

    private async Task ProcessCandidatesAsync(IReadOnlyList<CandidateTaskItem> candidates, IReadOnlyList<IArchiveEngine> engines)
    {
        var (vault, masterPassword, maySavePasswords) = await LoadVaultAsync();
        var service = new ArchiveExtractionService(engines, new WindowsArchiveSourceRecycler());
        var succeeded = 0; var failed = 0; var canceled = 0; var warnings = 0;
        foreach (var task in candidates)
        {
            if (_windowCancellation.IsCancellationRequested) break;
            using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowCancellation.Token);
            _currentTaskCancellation = taskCancellation;
            CancelCurrentTaskButton.IsEnabled = true;
            task.Status = "正在处理";
            SetStatus($"正在处理：{task.LogicalName}", StatusTone.Info);
            var progress = new Progress<ExtractionProgress>(task.UpdateLegacyProgress);
            var operations = new Progress<ArchiveOperationUpdate>(update => AddOperation(task, update));
            string? acceptedPassword = null;
            try
            {
                var result = await service.ExtractAsync(task.Candidate, vault.GetCandidates(), taskCancellation.Token, progress, operations,
                    (request, _) => PromptPasswordAsync(request, taskCancellation.Token), password => acceptedPassword = password);
                if (acceptedPassword is not null)
                {
                    vault.RecordSuccessfulPassword(acceptedPassword);
                    if (maySavePasswords) await PasswordVaultStore.SaveAsync(vault, ApplicationStorage.PasswordVaultPath, masterPassword);
                }
                ApplySuccess(task, result, ref warnings); succeeded++;
            }
            catch (OperationCanceledException)
            {
                task.Status = "已取消，已保留未完成文件"; task.StopProgress(); canceled++;
            }
            catch (ArchivePasswordRequiredException)
            {
                task.Status = "需要密码"; task.StopProgress(); failed++;
            }
            catch (Exception)
            {
                task.Status = "解压失败，源文件已保留"; task.StopProgress(); failed++;
            }
            finally
            {
                _currentTaskCancellation = null;
                CancelCurrentTaskButton.IsEnabled = false;
            }
        }

        if (_windowCancellation.IsCancellationRequested) return;
        if (failed > 0) SetStatus($"已完成 {succeeded} 个任务，失败 {failed} 个，取消 {canceled} 个。", StatusTone.Error);
        else if (canceled > 0) SetStatus($"已完成 {succeeded} 个任务，取消 {canceled} 个。", StatusTone.Warning);
        else if (warnings > 0) SetStatus($"已完成 {succeeded} 个任务，其中 {warnings} 个源文件未回收。", StatusTone.Warning);
        else SetStatus($"已完成 {succeeded} 个解压任务。", StatusTone.Success);
    }

    private async Task<(PasswordVault Vault, string? MasterPassword, bool MaySave)> LoadVaultAsync()
    {
        try
        {
            var protection = await PasswordVaultStore.GetProtectionAsync(ApplicationStorage.PasswordVaultPath);
            if (protection != PasswordVaultProtection.MasterPassword) return (await PasswordVaultStore.LoadAsync(ApplicationStorage.PasswordVaultPath, null), null, true);
            var dialog = new MasterPasswordPromptWindow { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.MasterPassword)) return (new PasswordVault(), null, false);
            return (await PasswordVaultStore.LoadAsync(ApplicationStorage.PasswordVaultPath, dialog.MasterPassword), dialog.MasterPassword, true);
        }
        catch
        {
            SetStatus("密码库无法打开，本次输入的密码不会保存。", StatusTone.Warning);
            return (new PasswordVault(), null, false);
        }
    }

    private async Task<string?> PromptPasswordAsync(ArchivePasswordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested) return null;
            var dialog = new PasswordPromptWindow(request.ArchivePath) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Password : null;
        });
    }

    private void AddOperation(CandidateTaskItem root, ArchiveOperationUpdate update)
    {
        var archive = root.GetOrCreateArchive(update, _rows);
        var operation = archive.GetOrCreateOperation(update, _rows);
        operation.Apply(update);
        root.UpdateFromOperation(update);
    }

    private static void ApplySuccess(CandidateTaskItem task, ExtractionResult result, ref int warnings)
    {
        task.OutputDirectory = result.OutputDirectory; task.StopProgress();
        if (result.SourceRecycled) { task.Status = "已完成"; return; }
        task.Status = "已完成，源文件未回收"; warnings++;
    }

    private void SetStatus(string text, StatusTone tone)
    {
        StatusText.Text = text;
        var (brushKey, icon) = tone switch { StatusTone.Success => ("SuccessBrush", "\uE73E"), StatusTone.Warning => ("WarningBrush", "\uE7BA"), StatusTone.Error => ("ErrorBrush", "\uEA39"), _ => ("InfoBrush", "\uE895") };
        var brush = (Brush)FindResource(brushKey); StatusPanel.BorderBrush = brush; StatusIcon.Foreground = brush; StatusIcon.Text = icon;
        ActivityProgress.Visibility = tone == StatusTone.Info ? Visibility.Visible : Visibility.Collapsed;
        if (tone == StatusTone.Info) StartStatusAnimation(); else StopStatusAnimation();
    }
    private void StartStatusAnimation()
    {
        if (StatusIcon.RenderTransform is not RotateTransform rotation) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }
    private void StopStatusAnimation()
    {
        if (StatusIcon.RenderTransform is not RotateTransform rotation) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null); rotation.Angle = 0;
    }
    private enum StatusTone { Info, Success, Warning, Error }

    private abstract class TaskRow : INotifyPropertyChanged
    {
        private double _progressPercent; private bool _isProgressIndeterminate; private string _progressText = "等待"; private string _status = "等待处理"; private string? _outputDirectory;
        public event PropertyChangedEventHandler? PropertyChanged;
        public abstract string LogicalName { get; } public abstract string Format { get; } public abstract string Path { get; }
        public string? OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public double ProgressPercent { get => _progressPercent; protected set => SetField(ref _progressPercent, value); }
        public bool IsProgressIndeterminate { get => _isProgressIndeterminate; protected set => SetField(ref _isProgressIndeterminate, value); }
        public string ProgressText { get => _progressText; protected set => SetField(ref _progressText, value); }
        protected void SetProgress(double percent, bool indeterminate, string text) { ProgressPercent = percent; IsProgressIndeterminate = indeterminate; ProgressText = text; }
        public void StopProgress() => IsProgressIndeterminate = false;
        protected void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    private sealed class CandidateTaskItem(ArchiveCandidate candidate) : TaskRow
    {
        private readonly Dictionary<Guid, ArchiveTaskItem> _archives = [];
        public ArchiveCandidate Candidate { get; } = candidate;
        public override string LogicalName => Candidate.LogicalName; public override string Format => Candidate.Format.ToString(); public override string Path => Candidate.Path;
        public ArchiveTaskItem GetOrCreateArchive(ArchiveOperationUpdate update, ObservableCollection<TaskRow> rows)
        {
            if (_archives.TryGetValue(update.ArchiveId, out var existing)) return existing;
            var nested = update.ParentArchiveId is not null;
            var created = new ArchiveTaskItem(update.ArchiveId, update.ArchiveName, update.ArchivePath, nested ? "嵌套归档" : Format, nested ? 1 : 0);
            _archives.Add(update.ArchiveId, created);
            if (nested) rows.Add(created);
            return created;
        }
        public void UpdateLegacyProgress(ExtractionProgress progress) => SetProgress(ProgressPercent, true, $"已写入 {progress.FileCount:N0} 个文件 · {FormatBytes(progress.BytesWritten)} · {FormatElapsed(progress.Elapsed)}");
        public void UpdateFromOperation(ArchiveOperationUpdate update)
        {
            Status = update.State switch { ArchiveOperationState.WaitingForPassword => "等待密码", ArchiveOperationState.Failed => "失败", ArchiveOperationState.Canceled => "已取消", ArchiveOperationState.Completed => "处理中", _ => "正在处理" };
            if (update.Percent is double percent) SetProgress(percent, false, FormatOperation(update));
            else SetProgress(ProgressPercent, update.State == ArchiveOperationState.Running, FormatOperation(update));
        }
    }

    private sealed class ArchiveTaskItem(Guid id, string name, string path, string format, int level) : TaskRow
    {
        private readonly Dictionary<Guid, OperationTaskItem> _operations = [];
        public Guid Id { get; } = id; public int Level { get; } = level;
        public override string LogicalName => new string(' ', Level * 3) + (Level > 0 ? "└ " : string.Empty) + name;
        public override string Format => format; public override string Path => path;
        public OperationTaskItem GetOrCreateOperation(ArchiveOperationUpdate update, ObservableCollection<TaskRow> rows)
        {
            if (!_operations.TryGetValue(update.OperationId, out var result))
            {
                _operations[update.OperationId] = result = new OperationTaskItem(update, Level + 1);
                rows.Add(result);
            }
            return result;
        }
    }

    private sealed class OperationTaskItem : TaskRow
    {
        private readonly string _name; private readonly string _path; private readonly string _engine; private readonly int _level;
        public OperationTaskItem(ArchiveOperationUpdate update, int level) { _name = OperationName(update.Kind); _path = update.ArchivePath; _engine = update.EngineName ?? ""; _level = level; }
        public override string LogicalName => new string(' ', _level * 3) + "└ " + _name; public override string Format => _engine; public override string Path => _path;
        public void Apply(ArchiveOperationUpdate update)
        {
            Status = update.State switch { ArchiveOperationState.WaitingForPassword => "等待密码", ArchiveOperationState.Completed => "完成", ArchiveOperationState.Failed => "失败", ArchiveOperationState.Canceled => "已取消", _ => "进行中" };
            var indeterminate = update.Precision == ArchiveProgressPrecision.Indeterminate && update.State == ArchiveOperationState.Running;
            SetProgress(update.Percent ?? ProgressPercent, indeterminate, FormatOperation(update));
        }
    }

    private static string OperationName(ArchiveOperationKind kind) => kind switch { ArchiveOperationKind.Recognize => "识别压缩包", ArchiveOperationKind.PrepareInput => "准备引擎文件", ArchiveOperationKind.Validate => "验证归档", ArchiveOperationKind.Password => "验证或等待密码", ArchiveOperationKind.Extract => "解压", ArchiveOperationKind.ScanNested => "扫描嵌套归档", ArchiveOperationKind.Normalize => "整理输出", ArchiveOperationKind.Publish => "发布输出", _ => kind.ToString() };
    private static string FormatOperation(ArchiveOperationUpdate update)
    {
        var progress = update.Percent is double percent ? (update.Precision == ArchiveProgressPrecision.Estimated ? $"约 {percent:0}%" : $"{percent:0}%") : (update.State == ArchiveOperationState.WaitingForPassword ? "等待密码" : "进行中");
        var size = update.BytesWritten > 0 ? $" · {FormatBytes(update.BytesWritten)}" : string.Empty;
        return $"{progress}{size} · {FormatElapsed(update.Elapsed)}";
    }
    private static string FormatBytes(long bytes) => bytes switch { >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB", >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB", >= 1024L => $"{bytes / 1024d:0} KB", _ => $"{bytes:N0} B" };
    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
}
