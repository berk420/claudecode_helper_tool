using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace CCXboxController.Services;

public class LocalWhisperTranscriber : ITranscriber
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;

    public string Name => "local";

    public LocalWhisperTranscriber(string modelPath, string language, string? initialPrompt = null)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder();
        if (!string.IsNullOrEmpty(language) && language != "auto")
        {
            builder = builder.WithLanguage(language);
        }
        if (!string.IsNullOrWhiteSpace(initialPrompt))
        {
            builder = builder.WithPrompt(initialPrompt);
        }
        _processor = builder.Build();
    }

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        using var fs = File.OpenRead(wavPath);
        var sb = new StringBuilder();
        await foreach (var seg in _processor.ProcessAsync(fs, ct))
        {
            sb.Append(seg.Text);
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
