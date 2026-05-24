using System;
using System.IO;
using System.Text.Json;
using CCXboxController.Models;

namespace CCXboxController.Services;

public static class ConfigStore
{
    public static string AppDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CCXboxController");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string ModelsDir => Path.Combine(AppDataDir, "models");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppConfig Load()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(ModelsDir);

        if (!File.Exists(ConfigPath))
        {
            var def = AppConfig.CreateDefault();
            Save(def);
            return def;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
            return cfg ?? AppConfig.CreateDefault();
        }
        catch
        {
            return AppConfig.CreateDefault();
        }
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(cfg, Options);
        File.WriteAllText(ConfigPath, json);
    }
}
