using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuickTranslate.Models;

namespace QuickTranslate.Services;

/// <summary>
/// 配置读写。三条硬约束：
/// 1) 写盘用「临时文件 + File.Move(overwrite)」原子替换，写一半崩了不会留下截断的 JSON；
/// 2) 配置损坏或密钥无法解密时，先备份 .bak 并返回中文提示，绝不静默覆盖可能含密钥的原文件；
/// 3) 任何读写异常都在本类内消化，Load 永远不会把异常抛到 OnStartup 把程序崩在启动阶段。
/// 密钥用 DPAPI(CurrentUser) 加密后存盘，内存中仍是明文供签名使用。
/// </summary>
public static class ConfigService
{
    private const string EncryptedPrefix = "dpapi:v1:";

    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickTranslate");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static string BackupPath => ConfigPath + ".bak";

    /// <summary>notice 是需要展示给用户的中文提示（正常时为 null）；createdNew 表示首次生成了配置文件。</summary>
    public static AppConfig Load(out bool createdNew, out string? notice)
    {
        createdNew = false;
        notice = null;

        string? json;
        try
        {
            json = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            notice = $"配置文件读取失败（{Describe(ex)}）。本次使用临时配置，原文件不会被覆盖。";
            LogService.Error("ConfigService.Load: 读取配置失败", ex);
            return new AppConfig();
        }

        if (json is null)
        {
            var fresh = new AppConfig();
            try
            {
                Save(fresh);
                createdNew = true;
            }
            catch (Exception ex) when (IsSaveFailure(ex))
            {
                notice = $"无法创建配置文件（{Describe(ex)}）。本次使用临时配置，保存设置时可重试。";
                LogService.Error("ConfigService.Load: 创建配置失败", ex);
            }
            return fresh;
        }

        StoredConfig stored;
        AppConfig config;
        try
        {
            stored = JsonSerializer.Deserialize<StoredConfig>(json, JsonOptions)
                     ?? throw new JsonException("配置内容为空");
            config = FromStored(stored);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or NotSupportedException)
        {
            string? backup = Backup();
            notice = $"配置文件无法解析或密钥无法解密（{Describe(ex)}）；原文件已保留" +
                     (backup is null ? "" : $"，并备份为 {Path.GetFileName(backup)}") +
                     "。请在设置里重新填写 APPID 与密钥。";
            LogService.Error("ConfigService.Load: 配置不可读，已备份", ex);
            return new AppConfig();
        }

        // 一次性迁移：旧版明文配置在首次启动时就地加密，之后不再保留双格式读取逻辑。
        if (!string.IsNullOrEmpty(config.Secret) && !IsEncrypted(stored.Secret))
        {
            try
            {
                Save(config);
                notice = "已把原有的明文密钥加密保存。";
                LogService.Info("ConfigService: 明文密钥已一次性迁移为 DPAPI 加密");
            }
            catch (Exception ex) when (IsSaveFailure(ex))
            {
                notice = $"密钥加密保存失败（{Describe(ex)}）；原文件保持明文，请检查目录权限。";
                LogService.Error("ConfigService.Load: 迁移加密失败", ex);
            }
        }

        return config;
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(ToStored(config), JsonOptions);

        // 同目录下的临时文件保证 File.Move 是同一卷内的原子替换，不会跨卷拷贝。
        string temp = Path.Combine(ConfigDirectory, $"config.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, ConfigPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) { LogService.Warn("ConfigService.Save: 临时文件清理失败", ex); }
            throw;
        }
    }

    private static string? Backup()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            if (!File.Exists(BackupPath)) File.Copy(ConfigPath, BackupPath, overwrite: false);
            return BackupPath;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            LogService.Error("ConfigService.Backup: 备份失败", ex);
            return null;
        }
    }

    private static AppConfig FromStored(StoredConfig stored) => new()
    {
        AppId = stored.AppId ?? string.Empty,
        Secret = IsEncrypted(stored.Secret)
            ? Decrypt(stored.Secret!)
            : stored.Secret ?? string.Empty,
        TakeWordHotkey = Fallback(stored.TakeWordHotkey, "Alt+T"),
        OcrHotkey = Fallback(stored.OcrHotkey, "Alt+S"),
        TargetLanguage = Fallback(stored.TargetLanguage, "zh"),
    };

    private static StoredConfig ToStored(AppConfig config) => new()
    {
        AppId = config.AppId,
        Secret = string.IsNullOrEmpty(config.Secret) ? string.Empty : Encrypt(config.Secret),
        TakeWordHotkey = config.TakeWordHotkey,
        OcrHotkey = config.OcrHotkey,
        TargetLanguage = config.TargetLanguage,
    };

    private static string Encrypt(string plain)
    {
        byte[] data = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return EncryptedPrefix + Convert.ToBase64String(data);
    }

    private static string Decrypt(string value)
    {
        byte[] data = Convert.FromBase64String(value[EncryptedPrefix.Length..]);
        return Encoding.UTF8.GetString(
            ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
    }

    private static bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static bool IsSaveFailure(Exception ex) =>
        IsStorageFailure(ex) || ex is CryptographicException or NotSupportedException;

    private static string Describe(Exception ex) => ex switch
    {
        JsonException => "不是有效的 JSON 或字段类型不对",
        CryptographicException => "DPAPI 加密不可用或密文已损坏",
        FormatException => "加密内容格式损坏",
        UnauthorizedAccessException => "没有读写权限",
        IOException => "磁盘或文件读写错误",
        _ => "未知错误",
    };

}

/// <summary>落盘格式。Secret 字段存的是 DPAPI 密文，不是明文。</summary>
internal sealed class StoredConfig
{
    public StoredConfig() { }

    public string? AppId { get; set; }
    public string? Secret { get; set; }
    public string? TakeWordHotkey { get; set; }
    public string? OcrHotkey { get; set; }
    public string? TargetLanguage { get; set; }
}
