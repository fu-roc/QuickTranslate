using System.IO;

namespace QuickTranslate.Services;

/// <summary>
/// 极简文件日志：写在 %LOCALAPPDATA%\QuickTranslate\app.log，超过 1 MB 时轮转成 .old。
/// 只记录异常与关键状态，绝不记录 APPID / 密钥 / 翻译原文。日志本身失败时静默吞掉，
/// 不能因为「写不了日志」把主流程弄崩。
/// </summary>
public static class LogService
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickTranslate", "app.log");

    public static string LogFilePath => LogPath;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    File.Copy(LogPath, LogPath + ".old", overwrite: true);
                    File.WriteAllText(LogPath, string.Empty);
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null) line += Environment.NewLine + "    " + ex;
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // 日志是诊断手段，不是业务依赖
        }
    }
}
