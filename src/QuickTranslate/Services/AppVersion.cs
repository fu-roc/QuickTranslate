namespace QuickTranslate.Services;

public static class AppVersion
{
    public static Version Value { get; } = typeof(AppVersion).Assembly.GetName().Version!;

    public static string Display => Value.ToString(3);
}
