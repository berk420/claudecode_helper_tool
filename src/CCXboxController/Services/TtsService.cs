using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;

namespace CCXboxController.Services;

public class TtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private readonly object _lock = new();
    private bool _disposed;

    public TtsService(string preferredCulture = "tr-TR")
    {
        _synth = new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        TrySelectVoice(preferredCulture);
    }

    public string CurrentVoice => _synth.Voice?.Name ?? "(default)";

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                Logger.Error("Tts.Speak", ex);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed) return;
            try { _synth.SpeakAsyncCancelAll(); }
            catch (Exception ex) { Logger.Error("Tts.Stop", ex); }
        }
    }

    private void TrySelectVoice(string preferredCulture)
    {
        try
        {
            var voices = _synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .ToList();
            if (voices.Count == 0) return;

            var match = voices.FirstOrDefault(v =>
                string.Equals(v.Culture?.Name, preferredCulture, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                var lang = preferredCulture.Split('-')[0];
                match = voices.FirstOrDefault(v =>
                    v.Culture?.TwoLetterISOLanguageName?.Equals(lang, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (match != null) _synth.SelectVoice(match.Name);
        }
        catch (Exception ex)
        {
            Logger.Error("Tts.TrySelectVoice", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _synth.SpeakAsyncCancelAll(); } catch { }
            try { _synth.Dispose(); } catch { }
        }
    }
}
