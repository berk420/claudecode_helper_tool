using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;

namespace CCXboxController.Services;

public class TtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private readonly object _lock = new();
    private readonly Dictionary<string, string?> _voiceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultCulture;
    private bool _disposed;

    public TtsService(string defaultCulture = "tr-TR")
    {
        _synth = new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        _defaultCulture = defaultCulture;
        var initial = ResolveVoiceForCulture(defaultCulture);
        if (initial != null)
        {
            try { _synth.SelectVoice(initial); } catch (Exception ex) { Logger.Error("Tts.SelectVoice.init", ex); }
        }
    }

    public string CurrentVoice => _synth.Voice?.Name ?? "(default)";

    // 1.0 = normal. SAPI Rate (-10..+10) sapıtmış log-skala — hissedilen oranlara denk bir tablo kullanıyoruz.
    public void SetRateMultiplier(double multiplier)
    {
        int rate;
        if (multiplier <= 0.55) rate = -7;
        else if (multiplier <= 0.80) rate = -3;
        else if (multiplier <= 1.05) rate = 0;
        else if (multiplier <= 1.30) rate = 2;
        else if (multiplier <= 1.55) rate = 4;
        else if (multiplier <= 1.80) rate = 6;
        else rate = 8;
        lock (_lock)
        {
            if (_disposed) return;
            try { _synth.Rate = rate; }
            catch (Exception ex) { Logger.Error($"Tts.SetRate({multiplier}->{rate})", ex); }
        }
    }

    public void SetVolume(int volume)
    {
        var clamped = Math.Max(0, Math.Min(100, volume));
        lock (_lock)
        {
            if (_disposed) return;
            try { _synth.Volume = clamped; }
            catch (Exception ex) { Logger.Error($"Tts.SetVolume({clamped})", ex); }
        }
    }

    public void Speak(string text, string? culture = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var target = string.IsNullOrWhiteSpace(culture) ? _defaultCulture : culture!;
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                var voiceName = ResolveVoiceForCulture(target);
                if (voiceName != null && _synth.Voice?.Name != voiceName)
                {
                    try { _synth.SelectVoice(voiceName); }
                    catch (Exception ex) { Logger.Error($"Tts.SelectVoice({voiceName})", ex); }
                }
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

    private string? ResolveVoiceForCulture(string culture)
    {
        if (_voiceCache.TryGetValue(culture, out var cached)) return cached;
        string? picked = null;
        try
        {
            var voices = _synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .ToList();
            if (voices.Count > 0)
            {
                var exact = voices.FirstOrDefault(v =>
                    string.Equals(v.Culture?.Name, culture, StringComparison.OrdinalIgnoreCase));
                if (exact != null) picked = exact.Name;
                else
                {
                    var lang = culture.Split('-')[0];
                    var langMatch = voices.FirstOrDefault(v =>
                        v.Culture?.TwoLetterISOLanguageName?.Equals(lang, StringComparison.OrdinalIgnoreCase) == true);
                    picked = langMatch?.Name;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Tts.ResolveVoiceForCulture({culture})", ex);
        }
        _voiceCache[culture] = picked;
        return picked;
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
