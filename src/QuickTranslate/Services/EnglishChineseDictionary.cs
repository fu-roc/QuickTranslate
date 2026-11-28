using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace QuickTranslate.Services;

internal static partial class EnglishChineseDictionary
{
    private const string ResourceName = "QuickTranslate.Assets.ecdict.tsv.gz";
    private const string SurroundingPunctuation = "\"'“”‘’()[]{}<>.,?!:;，。？！：；、（）【】《》「」『』";

    private static readonly Lazy<Task<IReadOnlyDictionary<string, string>>> Entries =
        new(() => Task.Run(LoadEntries));

    public static async Task<string[]?> FindAsync(string text, CancellationToken cancellationToken)
    {
        string word = NormalizeWord(text);
        if (word.Length > 80 || !WordPattern().IsMatch(word)) return null;

        var entries = await GetEntriesAsync(cancellationToken);
        return entries.TryGetValue(word, out string? definitions) ? definitions.Split('\t') : null;
    }

    internal static string NormalizeWord(string text) =>
        text.AsSpan().Trim().Trim(SurroundingPunctuation).Trim().ToString().Replace('’', '\'');

    internal static Task<IReadOnlyDictionary<string, string>> GetEntriesAsync(CancellationToken token) =>
        Entries.Value.WaitAsync(token);

    [GeneratedRegex(@"\A[A-Za-z]+(?:['-][A-Za-z]+)*\z")]
    private static partial Regex WordPattern();

    private static IReadOnlyDictionary<string, string> LoadEntries()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using Stream resource = typeof(EnglishChineseDictionary).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new FileNotFoundException("找不到内置英汉词库。", ResourceName);
            using var reader = new StreamReader(new GZipStream(resource, CompressionMode.Decompress));
            while (reader.ReadLine() is { } line)
            {
                int separator = line.IndexOf('\t');
                if (separator > 0 && separator < line.Length - 1)
                    entries[line[..separator]] = line[(separator + 1)..];
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            LogService.Warn("读取内置英汉词库失败，将使用百度翻译", ex);
            entries.Clear();
        }
        return entries;
    }
}
