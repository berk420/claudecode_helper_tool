using System;
using System.IO;
using System.Media;
using System.Threading;
using CCXboxController.Models;

namespace CCXboxController.Services;

// Watches the chat-end signal file. When the hook touches it, plays a sound (if enabled).
// Debounced so rapid touches do not stack.
public sealed class ChatEndNotifierService : IDisposable
{
    private readonly Func<ChatEndSoundSettings> _getSettings;
    private FileSystemWatcher? _watcher;
    private long _lastFireTicks;
    private const long DebounceTicks = 5_000_000; // 500 ms

    public ChatEndNotifierService(Func<ChatEndSoundSettings> getSettings)
    {
        _getSettings = getSettings;
    }

    public void Start()
    {
        try
        {
            var dir = ConfigStore.AppDataDir;
            Directory.CreateDirectory(dir);
            var fileName = Path.GetFileName(ClaudeHookInstaller.SignalFilePath);

            _watcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnSignal;
            _watcher.Created += OnSignal;
            Logger.Info($"ChatEndNotifierService: watching {ClaudeHookInstaller.SignalFilePath}");
        }
        catch (Exception ex)
        {
            Logger.Error("ChatEndNotifierService.Start", ex);
        }
    }

    private void OnSignal(object sender, FileSystemEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastFireTicks);
            if (now - last < DebounceTicks) return;
            Interlocked.Exchange(ref _lastFireTicks, now);

            var settings = _getSettings();
            if (settings == null || !settings.Enabled) return;

            PlaySound(settings.SoundFile);
        }
        catch (Exception ex)
        {
            Logger.Error("ChatEndNotifierService.OnSignal", ex);
        }
    }

    private static void PlaySound(string? customPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                using var sp = new SoundPlayer(customPath);
                sp.Play();
                return;
            }

            var winMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "notify.wav");
            if (File.Exists(winMedia))
            {
                using var sp = new SoundPlayer(winMedia);
                sp.Play();
                return;
            }

            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("ChatEndNotifierService.PlaySound", ex);
        }
    }

    public void TestPlay()
    {
        var settings = _getSettings();
        PlaySound(settings?.SoundFile);
    }

    public void Dispose()
    {
        try { _watcher?.Dispose(); } catch { }
    }
}
