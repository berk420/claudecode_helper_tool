using System;
using System.IO;
using System.Threading;

namespace CCXboxController.Services;

public static class Logger
{
    private static readonly object _lock = new();
    private static string LogPath => Path.Combine(ConfigStore.AppDataDir, "app.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);

    public static void Error(string context, Exception ex)
    {
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }

    public static void Write(string level, string msg)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(ConfigStore.AppDataDir);
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Thread.CurrentThread.ManagedThreadId}] {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
