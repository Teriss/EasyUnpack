using System.IO;
using System.Windows;

namespace EasyUnpack.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = ReadInputPaths(e.Args);
        Window window = paths.Length == 0 ? new SettingsWindow() : new MainWindow(paths);
        MainWindow = window;
        window.Show();
    }

    private static string[] ReadInputPaths(string[] arguments)
    {
        var pipeIndex = Array.FindIndex(arguments, static argument => string.Equals(argument, "--pipe", StringComparison.Ordinal));
        if (pipeIndex >= 0 && pipeIndex + 1 < arguments.Length)
        {
            try
            {
                return SelectionPipeReader.ReadAsync(arguments[pipeIndex + 1]).GetAwaiter().GetResult().ToArray();
            }
            catch
            {
                return [];
            }
        }

        return arguments.Where(File.Exists).Concat(arguments.Where(Directory.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
