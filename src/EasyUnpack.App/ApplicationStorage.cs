using System.IO;

namespace EasyUnpack.App;

internal static class ApplicationStorage
{
    private const string DataDirectoryEnvironmentVariable = "EASYUNPACK_DATA_DIRECTORY";

    public static string PasswordVaultPath => Path.Combine(DataDirectory, "passwords.json");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    private static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyUnpack")
                : Path.GetFullPath(overridePath);
        }
    }
}
