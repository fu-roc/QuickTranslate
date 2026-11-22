using System.Windows.Automation;
using QuickTranslate.Services;
using WpfButton = System.Windows.Controls.Button;

namespace QuickTranslate.Windows;

public sealed class PronunciationButton : WpfButton
{
    private CancellationTokenSource? _playback;
    private string? _text;

    public PronunciationButton()
    {
        IsEnabled = false;
        SetPlaying(false);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopPlayback();
        };
    }

    internal event Action<string?>? FeedbackChanged;

    internal string? SpeechText
    {
        get => _text;
        set
        {
            if (_text == value) return;
            StopPlayback();
            _text = value;
            IsEnabled = value is not null;
            FeedbackChanged?.Invoke(null);
        }
    }

    protected override async void OnClick()
    {
        base.OnClick();
        if (_playback is not null)
        {
            StopPlayback();
            return;
        }
        if (_text is not { } text) return;

        using var playback = new CancellationTokenSource();
        _playback = playback;
        SetPlaying(true);
        FeedbackChanged?.Invoke(null);
        try
        {
            string? error = await PronunciationService.SpeakEnglishAsync(text, playback.Token);
            if (ReferenceEquals(_playback, playback) && error is not null) FeedbackChanged?.Invoke(error);
        }
        finally
        {
            if (ReferenceEquals(_playback, playback))
            {
                _playback = null;
                SetPlaying(false);
            }
        }
    }

    private void StopPlayback()
    {
        var playback = _playback;
        if (playback is null) return;
        _playback = null;
        playback.Cancel();
        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        Content = playing ? "停止" : "发音";
        AutomationProperties.SetName(this, playing ? "停止发音" : "播放英语发音");
    }
}
