using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CCXboxController.Services;

public sealed record SubagentInfo(string Name, string Description, string? Model, string? Tools, string FilePath);

public static class SubagentService
{
    public static string AgentsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "agents");

    public static IReadOnlyList<SubagentInfo> Load()
    {
        var list = new List<SubagentInfo>();
        var dir = AgentsDirectory;
        if (!Directory.Exists(dir)) return list;

        foreach (var path in Directory.EnumerateFiles(dir, "*.md"))
        {
            try
            {
                var info = ParseFile(path);
                if (info != null) list.Add(info);
            }
            catch (Exception ex)
            {
                Logger.Error($"Subagent parse failed: {path}", ex);
            }
        }
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SubagentInfo? ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        if (!text.StartsWith("---")) return null;

        int endIdx = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        var frontmatter = text.Substring(3, endIdx - 3);
        var fields = ParseSimpleYaml(frontmatter);

        var name = fields.GetValueOrDefault("name") ?? Path.GetFileNameWithoutExtension(path);
        var description = fields.GetValueOrDefault("description") ?? "";
        var model = fields.GetValueOrDefault("model");
        var tools = fields.GetValueOrDefault("tools");

        return new SubagentInfo(name, description, model, tools, path);
    }

    private static Dictionary<string, string> ParseSimpleYaml(string yaml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentValue = new StringBuilder();

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            int colonIdx = line.IndexOf(':');
            bool isNewKey = colonIdx > 0 && !char.IsWhiteSpace(line[0]);

            if (isNewKey)
            {
                if (currentKey != null)
                    result[currentKey] = currentValue.ToString().Trim();

                currentKey = line.Substring(0, colonIdx).Trim();
                var val = line.Substring(colonIdx + 1).Trim();
                currentValue.Clear();
                currentValue.Append(val);
            }
            else if (currentKey != null)
            {
                if (currentValue.Length > 0) currentValue.Append(' ');
                currentValue.Append(line.Trim());
            }
        }

        if (currentKey != null)
            result[currentKey] = currentValue.ToString().Trim();

        return result;
    }
}
