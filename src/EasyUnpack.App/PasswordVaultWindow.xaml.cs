using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using EasyUnpack.Core.Passwords;

namespace EasyUnpack.App;

public partial class PasswordVaultWindow : Window
{
    private PasswordVault _vault = new();
    private string? _masterPassword;
    private bool _loaded;
    private bool _syncingPassword;
    private bool _passwordRevealed;
    private bool _passwordListRevealed;
    private Point _dragStart;
    private Guid? _pendingDragId;

    public PasswordVaultWindow()
    {
        InitializeComponent();
        ((DataGridTextColumn)EntryList.Columns[3]).Binding = new Binding(nameof(VaultEntryRow.PasswordDisplay));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var protection = await PasswordVaultStore.GetProtectionAsync(ApplicationStorage.PasswordVaultPath);
            if (protection == PasswordVaultProtection.MasterPassword)
            {
                var dialog = new MasterPasswordPromptWindow { Owner = this };
                if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.MasterPassword))
                {
                    Close();
                    return;
                }

                _masterPassword = dialog.MasterPassword;
            }

            _vault = await PasswordVaultStore.LoadAsync(ApplicationStorage.PasswordVaultPath, _masterPassword);
            _loaded = true;
            RefreshEntries();
            UpdateProtectionActions();
            BeginNewEntry();
        }
        catch (CryptographicException)
        {
            MessageBox.Show(this, "主密码不正确，或者密码库已经损坏。", "EasyUnpack", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, "密码库无法打开。", "EasyUnpack", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void NewEntry_Click(object sender, RoutedEventArgs e) => BeginNewEntry();

    private void RevealAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _passwordListRevealed = !_passwordListRevealed;
        RefreshEntries(EntryList.SelectedItem is VaultEntryRow selected ? selected.Entry.Id : null);
        UpdateRevealAllButton();
    }

    private async void SaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var password = CurrentPassword;
        if (string.IsNullOrEmpty(password))
        {
            ShowValidation("密码不能为空。");
            return;
        }

        Guid? selectedId = null;
        if (EntryList.SelectedItem is VaultEntryRow selected)
        {
            selectedId = selected.Entry.Id;
            if (!_vault.Update(selected.Entry.Id, password, LabelInput.Text))
            {
                ShowValidation("该密码已经存在，请修改后再保存。");
                return;
            }
        }
        else if (!_vault.Add(password, LabelInput.Text))
        {
            ShowValidation("该密码已经存在，请直接编辑现有条目。");
            return;
        }

        await SaveVaultAsync();
        RefreshEntries(selectedId);
        if (selectedId is null) BeginNewEntry();
        HideValidation();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || EntryList.SelectedItem is not VaultEntryRow selected) return;
        var confirmation = MessageBox.Show(this, "确定删除选中的密码吗？", "EasyUnpack", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        if (_vault.Remove(selected.Entry.Id))
        {
            await SaveVaultAsync();
            RefreshEntries();
            BeginNewEntry();
        }
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is not VaultEntryRow selected)
        {
            DeleteButton.IsEnabled = false;
            SaveEntryButton.Content = "添加密码";
            return;
        }

        DeleteButton.IsEnabled = true;
        SaveEntryButton.Content = "保存修改";
        LabelInput.Text = selected.Entry.Label ?? string.Empty;
        SetPassword(selected.Entry.Value);
        HideValidation();
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        _passwordRevealed = !_passwordRevealed;
        if (_passwordRevealed)
        {
            RevealedPasswordInput.Text = PasswordInput.Password;
            PasswordInput.Visibility = Visibility.Collapsed;
            RevealedPasswordInput.Visibility = Visibility.Visible;
            RevealedPasswordInput.Focus();
            RevealedPasswordInput.CaretIndex = RevealedPasswordInput.Text.Length;
            RevealButton.Content = "\uED1A";
            RevealButton.ToolTip = "隐藏密码";
        }
        else
        {
            PasswordInput.Password = RevealedPasswordInput.Text;
            RevealedPasswordInput.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordInput.Focus();
            RevealButton.Content = "\uE890";
            RevealButton.ToolTip = "显示密码";
        }
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword || !_passwordRevealed) return;
        _syncingPassword = true;
        RevealedPasswordInput.Text = PasswordInput.Password;
        _syncingPassword = false;
    }

    private void RevealedPasswordInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword || !_passwordRevealed) return;
        _syncingPassword = true;
        PasswordInput.Password = RevealedPasswordInput.Text;
        _syncingPassword = false;
    }

    private void EntryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(EntryList);
        _pendingDragId = HasDragHandle((DependencyObject)e.OriginalSource)
            ? FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource)?.Item is VaultEntryRow row ? row.Entry.Id : null
            : null;
    }

    private void EntryList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragId is not Guid id) return;
        var position = e.GetPosition(EntryList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pendingDragId = null;
        DragDrop.DoDragDrop(EntryList, new DataObject(typeof(Guid), id), DragDropEffects.Move);
    }

    private void EntryList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Guid)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void EntryList_Drop(object sender, DragEventArgs e)
    {
        if (!_loaded || !e.Data.GetDataPresent(typeof(Guid)) || e.Data.GetData(typeof(Guid)) is not Guid id) return;
        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        var targetIndex = row?.Item is VaultEntryRow target ? target.Priority - 1 : _vault.Entries.Count - 1;
        if (_vault.Move(id, targetIndex))
        {
            await SaveVaultAsync();
            RefreshEntries(id);
        }
    }

    private async void SetProtection_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var dialog = new MasterPasswordSetupWindow { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.MasterPassword)) return;

        _masterPassword = dialog.MasterPassword;
        await SaveVaultAsync();
        UpdateProtectionActions();
    }

    private async void RemoveProtection_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _masterPassword is null) return;
        var confirmation = MessageBox.Show(this, "确定移除密码库的主密码保护吗？", "EasyUnpack", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        _masterPassword = null;
        await SaveVaultAsync();
        UpdateProtectionActions();
    }

    private string CurrentPassword => _passwordRevealed ? RevealedPasswordInput.Text : PasswordInput.Password;

    private void BeginNewEntry()
    {
        EntryList.SelectedItem = null;
        LabelInput.Clear();
        SetPassword(string.Empty);
        DeleteButton.IsEnabled = false;
        SaveEntryButton.Content = "添加密码";
        HideValidation();
        LabelInput.Focus();
    }

    private void SetPassword(string password)
    {
        _syncingPassword = true;
        PasswordInput.Password = password;
        RevealedPasswordInput.Text = password;
        _syncingPassword = false;
    }

    private void RefreshEntries(Guid? selectedId = null)
    {
        var rows = _vault.Entries.Select((entry, index) => new VaultEntryRow(entry, index + 1) { IsRevealed = _passwordListRevealed }).ToArray();
        EntryList.ItemsSource = rows;
        if (selectedId is Guid id) EntryList.SelectedItem = rows.FirstOrDefault(row => row.Entry.Id == id);
    }

    private void UpdateRevealAllButton()
    {
        RevealAllButton.Content = _passwordListRevealed ? "\uED1A" : "\uE890";
        RevealAllButton.ToolTip = _passwordListRevealed ? "隐藏列表中的密码" : "显示列表中的密码";
    }

    private async Task SaveVaultAsync()
    {
        try
        {
            await PasswordVaultStore.SaveAsync(_vault, ApplicationStorage.PasswordVaultPath, _masterPassword);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "密码库保存失败。", "EasyUnpack", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateProtectionActions()
    {
        var protectedVault = _masterPassword is not null;
        SetProtectionButton.Content = protectedVault ? "修改主密码" : "设置主密码";
        RemoveProtectionButton.Visibility = protectedVault ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation() => ValidationText.Visibility = Visibility.Collapsed;

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static bool HasDragHandle(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "PasswordDragHandle" }) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private sealed record VaultEntryRow(PasswordEntry Entry, int Priority)
    {
        public bool IsRevealed { get; init; }
        public string PasswordDisplay => IsRevealed ? Entry.Value : MaskedPassword;
        public string Label => string.IsNullOrWhiteSpace(Entry.Label) ? "未命名" : Entry.Label;
        public string MaskedPassword => new('●', Math.Clamp(Entry.Value.Length, 6, 12));
        public int SuccessCount => Entry.SuccessCount;
        public string LastSuccessfulText => Entry.LastSuccessfulAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "尚未使用";

        public override string ToString() => $"优先级 {Priority}，{Label}，密码 {MaskedPassword}";
    }
}
