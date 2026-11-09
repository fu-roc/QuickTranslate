using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuickTranslate.Models;

namespace QuickTranslate.Services;

public static class ConfigService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickTranslate");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load(out bool createdNew)
    {
        createdNew = false;
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch (IOException)
        {
            // 配置损坏或不可读时回退到默认值，避免阻止程序启动
        }
        catch (JsonException)
        {
            // 同上
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }

        var fresh = new AppConfig();
        Save(fresh);
        createdNew = true;
        return fresh;
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
    }
}
