using System.Speech.Synthesis;
using System.Text;

namespace QuickTranslate.Services;

internal static class PronunciationService
{
    private static readonly SemaphoreSlim PlaybackGate = new(1, 1);
    private static CancellationTokenSource? _activeRequest;

    public static bool UsesEnglishAlphabet(string text)
    {
        bool hasEnglishLetter = false;
        foreach (Rune character in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(character)) continue;
            if (!character.IsAscii) return false;
            hasEnglishLetter = true;
        }
        return hasEnglishLetter;
    }

    // 两个窗口从同一 Dispatcher 调用。新请求取消旧请求，等待旧的播放资源释放后再发声。
    public static async Task<string?> SpeakEnglishAsync(string text, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRequest?.Cancel();
        _activeRequest = request;
        try
        {
            await PlaybackGate.WaitAsync(request.Token);
            try
            {
                return await Task.Run(() => PlayAsync(text, request.Token), request.Token);
            }
            finally
            {
                PlaybackGate.Release();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogService.Warn($"英语发音失败：{ex.GetType().Name} ({ex.HResult:X8})");
            return "无法播放发音，请检查 Windows 的语音和音频输出设置。";
        }
        finally
        {
            if (ReferenceEquals(_activeRequest, request)) _activeRequest = null;
        }
    }

    private static async Task<string?> PlayAsync(string text, CancellationToken token)
    {
        using var synthesizer = new SpeechSynthesizer();
        var englishVoices = synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
            .Select(voice => voice.VoiceInfo)
            .ToArray();
        if (englishVoices.Length == 0)
            return "系统缺少英语语音，请在 Windows「设置 → 时间和语言 → 语音」中添加英语语音。";

        var defaultVoice = synthesizer.Voice;
        var voice = englishVoices.FirstOrDefault(voice => voice.Id == defaultVoice.Id)
            ?? englishVoices.FirstOrDefault(voice => voice.Culture.Name == "en-US")
            ?? englishVoices[0];
        synthesizer.SelectVoice(voice.Name);
        synthesizer.SetOutputToDefaultAudioDevice();

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synthesizer.SpeakCompleted += (_, e) =>
        {
            if (e.Error is { } error) completed.TrySetException(error);
            else if (e.Cancelled) completed.TrySetCanceled();
            else completed.TrySetResult();
        };

        token.ThrowIfCancellationRequested();
        synthesizer.SpeakAsync(text);
        try
        {
            await completed.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            synthesizer.SpeakAsyncCancelAll();
            throw;
        }
        return null;
    }
}
