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

public class WhisperSettings
{
    public string Provider { get; set; } = "local"; // "local" | "openai"
    public string ModelFileName { get; set; } = "ggml-small.bin";
    public string Language { get; set; } = "tr";
    public string InitialPrompt { get; set; } = "Claude Code, VS Code, prompt, dispatcher, BeginInvoke, repo, commit, branch, debug, controller, Xbox.";
    public OpenAiSettings OpenAi { get; set; } = new();
}

public class AppConfig
{
    public Dictionary<string, ButtonBinding> Buttons { get; set; } = new();
    public Dictionary<string, StickBinding> Sticks { get; set; } = new();
    public WhisperSettings Whisper { get; set; } = new();
    public bool Autostart { get; set; } = false;

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig();
        foreach (var btn in new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Start", "Back", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LeftStickPress", "RightStickPress" })
        {
            cfg.Buttons[btn] = new ButtonBinding();
        }
        cfg.Buttons["A"].Type = ActionType.Voice;
        cfg.Buttons["B"].Text = "git status\n";
        cfg.Buttons["X"].Text = "Devam et.\n";
        cfg.Buttons["Y"].Type = ActionType.ReadSelection;

        cfg.Sticks["LeftStick"] = new StickBinding();
        cfg.Sticks["RightStick"] = new StickBinding();
        return cfg;
    }
}
