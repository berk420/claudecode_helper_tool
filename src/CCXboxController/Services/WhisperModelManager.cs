using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CCXboxController.Services;

public class WhisperModelManager
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60) };

    public string ModelFileName { get; }
    public string ModelPath { get; }
    public string DownloadUrl => BaseUrl + ModelFileName;

    public WhisperModelManager(string fileName)
    {
        ModelFileName = fileName;
        ModelPath = Path.Combine(ConfigStore.ModelsDir, fileName);
    }

    // Tiny ggml file is ~75 MB; anything below 30 MB is almost certainly a truncated/aborted download.
    public bool IsAvailable => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 30_000_000;

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken token = default)
    {
        Directory.CreateDirectory(ConfigStore.ModelsDir);
        var tmp = ModelPath + ".part";
        if (File.Exists(tmp)) File.Delete(tmp);

        using var resp = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;

        await using (var src = await resp.Content.ReadAsStreamAsync(token))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, token)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), token);
                read += n;
                if (total.HasValue && progress != null)
                {
                    progress.Report((double)read / total.Value);
                }
            }
        }
        File.Move(tmp, ModelPath, overwrite: true);
    }
}
