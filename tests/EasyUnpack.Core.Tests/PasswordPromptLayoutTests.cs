using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyUnpack.App;

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
}
