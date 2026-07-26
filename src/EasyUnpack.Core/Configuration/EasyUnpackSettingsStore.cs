using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyUnpack.Core.Configuration;

public static class EasyUnpackSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<EasyUnpackSettings> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new EasyUnpackSettings();
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EasyUnpackSettings>(json, Options) ?? new EasyUnpackSettings();
    }

    public static async Task SaveAsync(EasyUnpackSettings settings, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(settings, Options), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
