using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CCXboxController.Services;

public class SpeechService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _wavWriter;
    private DateTime _startTime;
    private string _wavPath = string.Empty;
    private ITranscriber? _transcriber;

    public bool IsRecording { get; private set; }
    public TimeSpan MinDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public bool HasTranscriber => _transcriber != null;
    public string? TranscriberName => _transcriber?.Name;

    public event EventHandler<string>? Transcribed;
    public event EventHandler<Exception>? Error;

    public SpeechService(ITranscriber? transcriber = null)
    {
        _transcriber = transcriber;
    }

    public void SetTranscriber(ITranscriber? t)
    {
        var old = _transcriber;
        _transcriber = t;
        try { old?.Dispose(); } catch (Exception ex) { Logger.Error("Old transcriber dispose", ex); }
    }

    public void StartRecording()
    {
        if (IsRecording) return;
        if (_transcriber == null)
        {
            Error?.Invoke(this, new InvalidOperationException("Transkripsiyon sağlayıcı yapılandırılmamış."));
            return;
        }

        try
        {
            _wavPath = Path.Combine(Path.GetTempPath(), $"ccxbox_{Guid.NewGuid():N}.wav");
            var format = new WaveFormat(16000, 16, 1);
            _waveIn = new WaveInEvent
            {
                WaveFormat = format,
                BufferMilliseconds = 50
            };
            _wavWriter = new WaveFileWriter(_wavPath, format);
            _waveIn.DataAvailable += OnData;
            _waveIn.RecordingStopped += OnStopped;
            _startTime = DateTime.UtcNow;
            _waveIn.StartRecording();
            IsRecording = true;
        }
        catch (Exception ex)
        {
            CleanupRecording();
            Error?.Invoke(this, ex);
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        try { _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded); } catch { }
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        try { _waveIn?.StopRecording(); }
        catch (Exception ex) { Error?.Invoke(this, ex); }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        var duration = DateTime.UtcNow - _startTime;
        string path = _wavPath;
        CleanupRecording();

        if (duration < MinDuration)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            Transcribed?.Invoke(this, string.Empty);
            return;
        }

        var transcriber = _transcriber;
        if (transcriber == null)
        {
            try { File.Delete(path); } catch { }
            Error?.Invoke(this, new InvalidOperationException("Transkripsiyon sağlayıcı yapılandırılmamış."));
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var text = await transcriber.TranscribeAsync(path);
                try { File.Delete(path); } catch { }
                Transcribed?.Invoke(this, text);
            }
            catch (Exception ex)
            {
                Logger.Error("Transcribe", ex);
                Error?.Invoke(this, ex);
            }
        });
    }

    private void CleanupRecording()
    {
        try
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnData;
                _waveIn.RecordingStopped -= OnStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }
            _wavWriter?.Dispose();
            _wavWriter = null;
        }
        catch { }
    }

    public void Dispose()
    {
        CleanupRecording();
        try { _transcriber?.Dispose(); } catch { }
    }
}
