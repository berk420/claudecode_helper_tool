using System.Collections.Generic;
using System.Linq;

namespace CCXboxController.Services;

public record WhisperModelEntry(
    string FileName,
    string DisplayName,
    int SizeMB,
    int SpeedStars,   // 1..5, 5 = fastest
    int AccuracyStars // 1..5, 5 = most accurate
)
{
    private static string Stars(int n) => new string('★', n) + new string('☆', 5 - n);

    public string Label =>
        $"{DisplayName}  ·  ~{SizeMB} MB  ·  Hız {Stars(SpeedStars)}  ·  Doğruluk {Stars(AccuracyStars)}";
}

public static class WhisperModelCatalog
{
    // Files are served from https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}
    // Ratings are relative to one another on a 5-star scale (multilingual, FP16/INT8 mixed).
    public static readonly IReadOnlyList<WhisperModelEntry> Models = new[]
    {
        new WhisperModelEntry("ggml-tiny.bin",   "Tiny",     75,  5, 2),
        new WhisperModelEntry("ggml-base.bin",   "Base",    142,  4, 3),
        new WhisperModelEntry("ggml-small.bin",  "Small",   466,  3, 4),
        new WhisperModelEntry("ggml-medium.bin", "Medium", 1533,  2, 5),
    };

    public static WhisperModelEntry FindByFileName(string fileName) =>
        Models.FirstOrDefault(m => m.FileName == fileName) ?? Models.First(m => m.FileName == "ggml-small.bin");
}
