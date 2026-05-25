using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CCXboxController.Models;

public enum ActionType
{
    Text,
    Voice,
    ReadSelection
}

public class ButtonBinding
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionType Type { get; set; } = ActionType.Text;

    public string Text { get; set; } = string.Empty;

    // Only used when Type == ReadSelection. "tr" or "en" (BCP-47 prefix).
    public string? Language { get; set; }
}

public class StickBinding
{
    public ButtonBinding Up { get; set; } = new();
    public ButtonBinding Down { get; set; } = new();
    public ButtonBinding Left { get; set; } = new();
    public ButtonBinding Right { get; set; } = new();
}

public class OpenAiSettings
{
    // DPAPI-protected base64 blob; raw key never written to disk.
    public string ApiKeyProtected { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini-transcribe";
}

public class TtsSettings
{
    // 1.0 = normal hız. UI 0.75 / 1.0 / 1.25 / 1.5 / 1.75 / 2.0 değerlerini gösterir.
    public double RateMultiplier { get; set; } = 1.0;
    // 0-100. SAPI Volume aralığı.
    public int Volume { get; set; } = 100;
}

public class WhisperSettings
{
    public string Provider { get; set; } = "local"; // "local" | "openai"
    public string ModelFileName { get; set; } = "ggml-small.bin";
    public string Language { get; set; } = "tr";
    public string InitialPrompt { get; set; } = "Claude Code, VS Code, prompt, dispatcher, BeginInvoke, repo, commit, branch, debug, controller, Xbox.";
    public OpenAiSettings OpenAi { get; set; } = new();
}

public class ChatEndSoundSettings
{
    public bool Enabled { get; set; } = true;
    public string SoundFile { get; set; } = "";
}

public class AppConfig
{
    public Dictionary<string, ButtonBinding> Buttons { get; set; } = new();
    public Dictionary<string, StickBinding> Sticks { get; set; } = new();
    public WhisperSettings Whisper { get; set; } = new();
    public TtsSettings Tts { get; set; } = new();
    public bool Autostart { get; set; } = false;
    public bool ShowUsageOverlay { get; set; } = true;
    public ChatEndSoundSettings ChatEndSound { get; set; } = new();

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig();
        foreach (var btn in new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Start", "Back", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LeftStickPress", "RightStickPress" })
        {
            cfg.Buttons[btn] = new ButtonBinding();
        }
        cfg.Buttons["A"].Type = ActionType.Voice;
        cfg.Buttons["B"].Text = "git status\n";
        cfg.Buttons["X"].Type = ActionType.ReadSelection;
        cfg.Buttons["X"].Language = "en";
        cfg.Buttons["Y"].Type = ActionType.ReadSelection;
        cfg.Buttons["Y"].Language = "tr";

        cfg.Sticks["LeftStick"] = new StickBinding();
        cfg.Sticks["RightStick"] = new StickBinding();
        return cfg;
    }
}
