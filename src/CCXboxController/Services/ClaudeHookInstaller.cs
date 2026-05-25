using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CCXboxController.Services;

// Idempotently installs a "Stop" hook into ~/.claude/settings.json that touches a signal file
// whenever Claude Code finishes a response. The running app watches that signal file and plays
// a sound if ChatEndSound is enabled in config.
public static class ClaudeHookInstaller
{
    public const string MarkerTag = "CCXboxController:chat-end-signal";

    public static string ClaudeSettingsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string SignalFilePath { get; } =
        Path.Combine(ConfigStore.AppDataDir, "chat-end.signal");

    public static string HookCommand =>
        $"powershell -NoProfile -WindowStyle Hidden -Command \"$p='{SignalFilePath.Replace("\\", "\\\\")}'; New-Item -Path $p -ItemType File -Force | Out-Null; (Get-Item $p).LastWriteTime = Get-Date; # {MarkerTag}\"";

    public static void EnsureInstalled()
    {
        try
        {
            var dir = Path.GetDirectoryName(ClaudeSettingsPath)!;
            Directory.CreateDirectory(dir);

            JsonObject root;
            if (File.Exists(ClaudeSettingsPath))
            {
                var text = File.ReadAllText(ClaudeSettingsPath);
                root = string.IsNullOrWhiteSpace(text)
                    ? new JsonObject()
                    : (JsonNode.Parse(text) as JsonObject ?? new JsonObject());
            }
            else
            {
                root = new JsonObject();
            }

            if (root["hooks"] is not JsonObject hooks)
            {
                hooks = new JsonObject();
                root["hooks"] = hooks;
            }

            if (hooks["Stop"] is not JsonArray stopArr)
            {
                stopArr = new JsonArray();
                hooks["Stop"] = stopArr;
            }

            // Already installed? Look for our marker.
            foreach (var entry in stopArr)
            {
                if (entry is not JsonObject group) continue;
                if (group["hooks"] is not JsonArray inner) continue;
                foreach (var h in inner)
                {
                    if (h is JsonObject ho && (ho["command"]?.ToString() ?? "").Contains(MarkerTag))
                    {
                        // Refresh command in case path changed.
                        ho["command"] = BuildCommandWithMarker();
                        File.WriteAllText(ClaudeSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                        return;
                    }
                }
            }

            var newGroup = new JsonObject
            {
                ["matcher"] = "",
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = BuildCommandWithMarker()
                    }
                }
            };
            stopArr.Add(newGroup);

            File.WriteAllText(ClaudeSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Logger.Info($"ClaudeHookInstaller: Stop hook installed at {ClaudeSettingsPath}");
        }
        catch (Exception ex)
        {
            Logger.Error("ClaudeHookInstaller.EnsureInstalled", ex);
        }
    }

    private static string BuildCommandWithMarker() => HookCommand;
}
