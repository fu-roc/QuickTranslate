using System.Reflection;

namespace QuickTranslate.Services;

public static class AppVersion
{
    /// <summary>程序集版本，与 csproj 里统一的 &lt;Version&gt; 保持一致，用于设置页与日志定位问题。</summary>
    public static string Display =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
}
