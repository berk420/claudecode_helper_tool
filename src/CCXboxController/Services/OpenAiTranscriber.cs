using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CCXboxController.Services;

public class OpenAiTranscriber : ITranscriber
{
    private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _language;

    public string Name => "openai";

    public OpenAiTranscriber(string apiKey, string model, string language)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini-transcribe" : model;
        _language = language;
    }

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("OpenAI API key boş.");

        using var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(wavPath, ct);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(_model), "model");
        if (!string.IsNullOrEmpty(_language) && _language != "auto")
        {
            content.Add(new StringContent(_language), "language");
        }
        content.Add(new StringContent("json"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Content = content;

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI {(int)resp.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
    }

    public void Dispose() { }
}
