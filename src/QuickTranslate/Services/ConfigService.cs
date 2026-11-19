using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuickTranslate.Models;

namespace QuickTranslate.Services;

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

    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly string BackupPath = ConfigPath + ".bak";

    public static AppConfig Load(out string? notice)
    {
        notice = null;

        string? json;
        try
        {
            json = File.ReadAllText(ConfigPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { json = null; }
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
            }
            catch (Exception ex) when (IsSaveFailure(ex))
            {
                notice = $"无法创建配置文件（{Describe(ex)}）。本次使用临时配置，保存设置时可重试。";
                LogService.Error("ConfigService.Load: 创建配置失败", ex);
            }
            return fresh;
        }

        AppConfig config;
        bool encryptOnLoad;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                     ?? throw new JsonException("配置内容为空");
            string secret = config.Secret ?? string.Empty;
            bool encrypted = secret.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
            encryptOnLoad = secret.Length > 0 && !encrypted;
            config.Secret = encrypted ? Decrypt(secret) : secret;
            config.AppId ??= string.Empty;
            config.TakeWordHotkey = Fallback(config.TakeWordHotkey, AppConfig.DefaultTakeWordHotkey);
            config.OcrHotkey = Fallback(config.OcrHotkey, AppConfig.DefaultOcrHotkey);
            config.TargetLanguage = Fallback(config.TargetLanguage, TranslationLanguage.SimplifiedChinese);
            config.MainWindowSourceLanguage = Fallback(config.MainWindowSourceLanguage, TranslationLanguage.Auto);
            config.MainWindowTargetLanguage ??= string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or NotSupportedException)
        {
            string? backup = Backup();
            notice = $"配置文件无法解析或密钥无法解密（{Describe(ex)}）；原文件已保留" +
                     (backup is null ? "" : $"，并备份为 {Path.GetFileName(backup)}") +
                     "。请在设置里重新填写 APPID 与密钥。";
            LogService.Error("配置不可读", ex);
            return new AppConfig();
        }

        if (encryptOnLoad)
        {
            try
            {
                Save(config);
                notice = "已把原有的明文密钥加密保存。";
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
        string secret = string.IsNullOrEmpty(config.Secret) ? string.Empty : Encrypt(config.Secret);
        string json = JsonSerializer.Serialize(config with { Secret = secret }, JsonOptions);

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

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    internal static bool IsSaveFailure(Exception ex) =>
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
