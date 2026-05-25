using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CCXboxController.Services;

public class UsageStats
{
    public string ModelDisplay { get; set; } = "";
    public bool HasActiveBlock { get; set; }
    public long BlockTokens { get; set; }
    public double BlockCostUsd { get; set; }
    public bool HasUsagePercent { get; set; }
    public double BlockUsagePercent { get; set; }
    public long TodayTokens { get; set; }
    public double TodayCostUsd { get; set; }
}

public sealed class CcUsageService : IDisposable
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _running;

    public event EventHandler<UsageStats?>? Updated;
    public event EventHandler<string>? Error;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        try { _cts?.Dispose(); } catch { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var stats = await FetchAsync(ct).ConfigureAwait(false);
                SafeInvokeUpdated(stats);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.Error("CcUsage poll", ex);
                try { Error?.Invoke(this, ex.Message); } catch { }
                SafeInvokeUpdated(null);
            }

            try { await Task.Delay(_interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void SafeInvokeUpdated(UsageStats? stats)
    {
        try { Updated?.Invoke(this, stats); } catch (Exception ex) { Logger.Error("CcUsage Updated handler", ex); }
    }

    private static async Task<UsageStats?> FetchAsync(CancellationToken ct)
    {
        var stats = new UsageStats();
        // Fetch full history so we can compute usage% vs the highest previously-observed block.
        // ccusage offline + cache is fast; pulling all blocks each minute is fine.
        var blocks = await RunCcUsageAsync("blocks --json --offline", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(blocks))
        {
            try { ParseBlocks(blocks, stats); }
            catch (Exception ex) { Logger.Error("CcUsage parse blocks", ex); }
        }
        return stats;
    }

    private static void ParseBlocks(string json, UsageStats stats)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("blocks", out var blocks)) return;

        long maxCompletedTokens = 0;
        JsonElement? activeBlock = null;

        foreach (var b in blocks.EnumerateArray())
        {
            if (b.TryGetProperty("isGap", out var gap) && gap.GetBoolean()) continue;
            bool isActive = b.TryGetProperty("isActive", out var act) && act.GetBoolean();
            if (isActive)
            {
                activeBlock = b;
                continue;
            }
            if (b.TryGetProperty("totalTokens", out var tt))
            {
                var t = tt.GetInt64();
                if (t > maxCompletedTokens) maxCompletedTokens = t;
            }
        }

        if (activeBlock == null) return;
        var ab = activeBlock.Value;
        stats.HasActiveBlock = true;
        if (ab.TryGetProperty("totalTokens", out var att)) stats.BlockTokens = att.GetInt64();
        if (ab.TryGetProperty("costUSD", out var c)) stats.BlockCostUsd = c.GetDouble();
        if (ab.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetString();
                if (string.IsNullOrEmpty(name)) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(PrettyModel(name));
            }
            if (sb.Length > 0) stats.ModelDisplay = sb.ToString();
        }

        // Usage% vs the highest previously-completed block (this is what ccusage --token-limit max
        // uses and is the closest approximation of the claude.ai session-usage indicator).
        if (maxCompletedTokens > 0 && stats.BlockTokens > 0)
        {
            stats.BlockUsagePercent = Math.Clamp(stats.BlockTokens / (double)maxCompletedTokens * 100.0, 0, 999);
            stats.HasUsagePercent = true;
        }
    }

private static string PrettyModel(string raw)
    {
        // claude-opus-4-7 → Opus 4.7
        var s = raw.ToLowerInvariant();
        if (s.StartsWith("claude-")) s = s.Substring("claude-".Length);
        var parts = s.Split('-');
        if (parts.Length >= 3)
        {
            var family = char.ToUpper(parts[0][0]) + parts[0].Substring(1);
            var ver = $"{parts[1]}.{parts[2]}";
            return $"{family} {ver}";
        }
        return raw;
    }

    private static async Task<string> RunCcUsageAsync(string args, CancellationToken ct)
    {
        // Use cmd.exe so PATHEXT resolves npx.cmd / ccusage.cmd correctly on Windows.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c npx -y ccusage {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception ex)
        {
            Logger.Error("CcUsage start", ex);
            return string.Empty;
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        // Drain stderr so the pipe doesn't fill and stall the child.
        _ = proc.StandardError.ReadToEndAsync();

        var waitTask = proc.WaitForExitAsync(ct);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(45), ct)).ConfigureAwait(false);
        if (completed != waitTask)
        {
            try { proc.Kill(true); } catch { }
            return string.Empty;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        return stdout;
    }
}
