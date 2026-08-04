using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using EasyUnpack.App;
using EasyUnpack.Core.Passwords;

namespace EasyUnpack.Core.Tests;

public sealed class PasswordPromptLayoutTests
{
    [Fact]
    public void Long_archive_name_does_not_collapse_the_password_input_or_clip_actions()
    {
        RunInSta(() =>
        {
            var application = new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/EasyUnpack.App;component/Themes/DesignTokens.xaml"),
            });

            PasswordPromptWindow? dialog = null;
            try
            {
                dialog = new PasswordPromptWindow(new string('a', 120) + ".mp4");
                dialog.Show();
                dialog.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                dialog.UpdateLayout();

                var passwordInput = Assert.IsType<PasswordBox>(dialog.FindName("PasswordInput"));
                var continueButton = Assert.IsType<Button>(dialog.FindName("ContinueButton"));
                var buttonBottom = continueButton.TransformToAncestor(dialog)
                    .Transform(new Point(continueButton.ActualWidth, continueButton.ActualHeight)).Y;

                Assert.True(passwordInput.IsVisible);
                Assert.True(passwordInput.ActualHeight >= 36);
                Assert.True(continueButton.IsVisible);
                Assert.True(buttonBottom <= dialog.ActualHeight);

                AssertPasswordVaultReveal();
                AssertMainWindowProgressBindings();
            }
            finally
            {
                dialog?.Close();
                application.Shutdown();
            }
        });
    }

    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The WPF layout test did not finish in time.");
        failure?.Throw();
    }

    private static void AssertPasswordVaultReveal()
    {
        var secret = "vault-" + Guid.NewGuid().ToString("N");
        var window = CreateVaultWindow(secret);
        try
        {
            var list = Assert.IsType<DataGrid>(window.FindName("EntryList"));
            var row = GetSingleRow(list);
            var masked = GetStringProperty(row, "MaskedPassword");
            Assert.True(GetStringProperty(row, "PasswordDisplay") == masked);
            Assert.True(GetStringProperty(row, "PasswordDisplay") != secret);

            var revealAll = Assert.IsType<Button>(window.FindName("RevealAllButton"));
            revealAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetStringProperty(GetSingleRow(list), "PasswordDisplay") == secret);

            revealAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetStringProperty(GetSingleRow(list), "PasswordDisplay") == masked);

            list.SelectedItem = GetSingleRow(list);
            var passwordInput = Assert.IsType<PasswordBox>(window.FindName("PasswordInput"));
            Assert.True(passwordInput.Password == secret);
        }
        finally
        {
            window.Close();
        }

        var reopened = CreateVaultWindow(secret);
        try
        {
            var row = GetSingleRow(Assert.IsType<DataGrid>(reopened.FindName("EntryList")));
            Assert.True(GetStringProperty(row, "PasswordDisplay") != secret);
        }
        finally
        {
            reopened.Close();
        }
    }

    private static void AssertMainWindowProgressBindings()
    {
        var window = new MainWindow([]);
        var loadedHandler = (RoutedEventHandler)Delegate.CreateDelegate(
            typeof(RoutedEventHandler),
            window,
            typeof(MainWindow).GetMethod("Window_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
        window.Loaded -= loadedHandler;

        var host = new Window();
        try
        {
            var grid = Assert.IsType<DataGrid>(window.FindName("CandidateGrid"));
            var progressColumn = Assert.IsType<DataGridTemplateColumn>(grid.Columns[^2]);
            var content = Assert.IsAssignableFrom<FrameworkElement>(progressColumn.CellTemplate!.LoadContent());
            content.DataContext = new ProgressBindingRow();
            host.Content = content;
            host.Show();
            host.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            host.UpdateLayout();

            var progress = Assert.IsType<ProgressBar>(FindVisualDescendant<ProgressBar>(content));
            var text = Assert.IsType<TextBlock>(FindVisualDescendant<TextBlock>(content, control =>
                BindingOperations.GetBinding(control, TextBlock.TextProperty)?.Path?.Path == "ProgressText"));

            Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(progress, RangeBase.ValueProperty)!.Mode);
            Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(progress, ProgressBar.IsIndeterminateProperty)!.Mode);
            Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(text, TextBlock.TextProperty)!.Mode);
        }
        finally
        {
            host.Close();
            window.Close();
        }
    }

    private static PasswordVaultWindow CreateVaultWindow(string secret)
    {
        var window = new PasswordVaultWindow();
        SetPrivateField(window, "_vault", new PasswordVault([new PasswordEntry(Guid.NewGuid(), secret, "test", null, 0)]));
        SetPrivateField(window, "_loaded", true);
        InvokePrivate(window, "RefreshEntries", (object?)null);
        window.UpdateLayout();
        return window;
    }

    private static object GetSingleRow(DataGrid list) =>
        Assert.Single(((System.Collections.IEnumerable)list.ItemsSource!).Cast<object>());

    private static string GetStringProperty(object value, string name) =>
        Assert.IsType<string>(value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(value));

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void InvokePrivate(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, arguments);

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && (predicate is null || predicate(match))) return match;
            var nested = FindVisualDescendant(child, predicate);
            if (nested is not null) return nested;
        }

        return null;
    }

    private sealed class ProgressBindingRow
    {
        public double ProgressPercent => 0;

        public bool IsProgressIndeterminate => true;

        public string ProgressText => "Testing progress";
    }
}
