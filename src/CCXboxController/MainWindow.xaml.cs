using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CCXboxController.Models;
using CCXboxController.Services;

namespace CCXboxController;

public partial class MainWindow : Window
{
    private AppConfig _config = null!;
    private ControllerService _controller = null!;
    private SpeechService? _speech;
    private TtsService? _tts;
    private ActionDispatcher? _dispatcher;
    private WhisperModelManager _modelMgr = null!;
    private CcUsageService? _ccUsage;
    private Views.UsageOverlayWindow? _usageOverlay;
    private TrayIconService? _tray;
    private ChatEndNotifierService? _chatEndNotifier;

    private static readonly List<(string Key, string Label)> ListEntries = new()
    {
        ("A", "A  (varsayılan: Sesli yazım)"),
        ("B", "B"),
        ("X", "X  (varsayılan: Seçimi İngilizce oku)"),
        ("Y", "Y  (varsayılan: Seçimi Türkçe oku)"),
        ("LB", "LB"),
        ("RB", "RB"),
        ("LT", "LT"),
        ("RT", "RT"),
        ("Start", "Start"),
        ("Back", "Back"),
        ("DPadUp", "D-Pad ↑"),
        ("DPadDown", "D-Pad ↓"),
        ("DPadLeft", "D-Pad ←"),
        ("DPadRight", "D-Pad →"),
        ("LeftStickPress", "Sol Stick (bas)"),
        ("RightStickPress", "Sağ Stick (bas)"),
        ("LeftStick.Up",    "Sol Stick ↑"),
        ("LeftStick.Down",  "Sol Stick ↓"),
        ("LeftStick.Left",  "Sol Stick ←"),
        ("LeftStick.Right", "Sol Stick →"),
        ("RightStick.Up",    "Sağ Stick ↑"),
        ("RightStick.Down",  "Sağ Stick ↓"),
        ("RightStick.Left",  "Sağ Stick ←"),
        ("RightStick.Right", "Sağ Stick →"),
    };

    private string? _selectedKey;
    private bool _suppressEditorEvents;
    private bool _suppressSettingEvents;
    private readonly Dictionary<string, RadioButton> _modelRadios = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { LoadAll(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Başlatma hatası:\n{ex}", "CCXboxController", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadAll()
    {
        _config = ConfigStore.Load();
        EnsureAllKeys();

        _modelMgr = new WhisperModelManager(_config.Whisper.ModelFileName);
        _suppressSettingEvents = true;
        BuildModelRadios();
        UpdateModelStatus();
        AutostartBox.IsChecked = AutostartService.IsEnabled();
        UsageOverlayBox.IsChecked = _config.ShowUsageOverlay;
        ChatEndSoundBox.IsChecked = _config.ChatEndSound.Enabled;
        SetLangSelection(_config.Whisper.Language);
        SetProviderSelection(_config.Whisper.Provider);
        SetOpenAiModelSelection(_config.Whisper.OpenAi.Model);
        ApiKeyBox.Password = SecretProtector.Unprotect(_config.Whisper.OpenAi.ApiKeyProtected);
        UpdateProviderPanels();
        _suppressSettingEvents = false;

        foreach (var (key, label) in ListEntries)
        {
            BindingList.Items.Add(new ListBoxItem { Content = label, Tag = key });
        }

        _speech = new SpeechService();
        _speech.Error += (_, ex) => Dispatcher.BeginInvoke(new Action(() => LastTrigger.Text = $"Ses hatası: {ex.Message}"));

        _tts = new TtsService(_config.Whisper.Language == "en" ? "en-US" : "tr-TR");
        _tts.SetRateMultiplier(_config.Tts.RateMultiplier);
        _tts.SetVolume(_config.Tts.Volume);

        _suppressSettingEvents = true;
        SetTtsRateSelection(_config.Tts.RateMultiplier);
        TtsVolumeSlider.Value = _config.Tts.Volume;
        TtsVolumeLabel.Text = _config.Tts.Volume.ToString();
        _suppressSettingEvents = false;

        _dispatcher = new ActionDispatcher(_config, _speech, _tts, Dispatcher);
        _dispatcher.Status += (_, msg) => Dispatcher.BeginInvoke(new Action(() => LastTrigger.Text = msg));
        _controller = new ControllerService();
        _controller.ConnectionChanged += OnConnectionChanged;
        _controller.ButtonEvent += OnButton;
        _controller.StickEvent += OnStick;
        _controller.Start();

        if (BindingList.Items.Count > 0) BindingList.SelectedIndex = 0;

        RebuildTranscriber();

        InitUsageOverlay();
        InitTray();
        InitChatEndNotifier();
        LoadSubagents();
    }

    private void LoadSubagents()
    {
        try
        {
            SubagentList.Items.Clear();
            var agents = SubagentService.Load();
            foreach (var a in agents) SubagentList.Items.Add(a);

            if (agents.Count == 0)
            {
                SubagentsEmptyHint.Text = $"Subagent bulunamadı.\n{SubagentService.AgentsDirectory} altına .md ekle.";
                SubagentsEmptyHint.Visibility = Visibility.Visible;
            }
            else
            {
                SubagentsEmptyHint.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Subagents load", ex);
            SubagentsEmptyHint.Text = $"Yüklenemedi: {ex.Message}";
            SubagentsEmptyHint.Visibility = Visibility.Visible;
        }
    }

    private void RefreshSubagents_Click(object sender, RoutedEventArgs e) => LoadSubagents();

    private void InitUsageOverlay()
    {
        _usageOverlay = new Views.UsageOverlayWindow();
        if (_config.ShowUsageOverlay) _usageOverlay.ShowOverlay();

        UsageOverlayStatus.Text = "yükleniyor…";
        _ccUsage = new CcUsageService();
        _ccUsage.Updated += (_, stats) => Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _usageOverlay?.ApplyStats(stats);
                UsageOverlayStatus.Text = stats == null
                    ? "güncellenemedi"
                    : $"son güncelleme: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex) { Logger.Error("UsageOverlay apply", ex); }
        }));
        _ccUsage.Error += (_, msg) => Dispatcher.BeginInvoke(new Action(() =>
        {
            UsageOverlayStatus.Text = $"hata: {msg}";
        }));
        _ccUsage.Start();
    }

    private void InitTray()
    {
        try
        {
            _tray = new TrayIconService(_config.ShowUsageOverlay);
            _tray.ShowWindowRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
            }));
            _tray.OverlayToggleRequested += (_, on) => Dispatcher.BeginInvoke(new Action(() =>
            {
                SetUsageOverlayEnabled(on, fromTray: true);
            }));
            _tray.ExitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                Application.Current.Shutdown();
            }));
        }
        catch (Exception ex) { Logger.Error("Tray init", ex); }
    }

    private void SetUsageOverlayEnabled(bool on, bool fromTray = false, bool fromCheckbox = false)
    {
        _config.ShowUsageOverlay = on;
        ConfigStore.Save(_config);
        if (on) _usageOverlay?.ShowOverlay(); else _usageOverlay?.HideOverlay();
        if (!fromCheckbox && UsageOverlayBox.IsChecked != on) UsageOverlayBox.IsChecked = on;
        if (!fromTray) _tray?.SetOverlayChecked(on);
    }

    private void UsageOverlayBox_Toggle(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = UsageOverlayBox.IsChecked == true;
        SetUsageOverlayEnabled(on, fromCheckbox: true);
    }

    private void InitChatEndNotifier()
    {
        try
        {
            ClaudeHookInstaller.EnsureInstalled();
            _chatEndNotifier = new ChatEndNotifierService(() => _config.ChatEndSound);
            _chatEndNotifier.Start();
            ChatEndSoundStatus.Text = _config.ChatEndSound.Enabled ? "etkin · hook kuruldu" : "kapalı · hook kuruldu";
        }
        catch (Exception ex)
        {
            Logger.Error("InitChatEndNotifier", ex);
            ChatEndSoundStatus.Text = $"hata: {ex.Message}";
        }
    }

    private void ChatEndSoundBox_Toggle(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = ChatEndSoundBox.IsChecked == true;
        _config.ChatEndSound.Enabled = on;
        ConfigStore.Save(_config);
        ChatEndSoundStatus.Text = on ? "etkin · hook kuruldu" : "kapalı · hook kuruldu";
    }

    private void ChatEndSoundTest_Click(object sender, RoutedEventArgs e)
    {
        _chatEndNotifier?.TestPlay();
    }

    private void EnsureAllKeys()
    {
        foreach (var (key, _) in ListEntries)
        {
            if (key.Contains('.')) continue;
            if (!_config.Buttons.ContainsKey(key)) _config.Buttons[key] = new ButtonBinding();
        }
        if (!_config.Sticks.ContainsKey("LeftStick")) _config.Sticks["LeftStick"] = new StickBinding();
        if (!_config.Sticks.ContainsKey("RightStick")) _config.Sticks["RightStick"] = new StickBinding();

        bool dirty = false;

        if (_config.Buttons.TryGetValue("Y", out var y)
            && y.Type == ActionType.Text
            && y.Text == "Tüm testleri çalıştır.\n")
        {
            y.Type = ActionType.ReadSelection;
            y.Text = string.Empty;
            dirty = true;
        }
        if (_config.Buttons.TryGetValue("Y", out var y2)
            && y2.Type == ActionType.ReadSelection
            && string.IsNullOrWhiteSpace(y2.Language))
        {
            y2.Language = "tr";
            dirty = true;
        }

        if (_config.Buttons.TryGetValue("X", out var x)
            && x.Type == ActionType.Text
            && x.Text == "Devam et.\n")
        {
            x.Type = ActionType.ReadSelection;
            x.Text = string.Empty;
            x.Language = "en";
            dirty = true;
        }
        if (_config.Buttons.TryGetValue("X", out var x2)
            && x2.Type == ActionType.ReadSelection
            && string.IsNullOrWhiteSpace(x2.Language))
        {
            x2.Language = "en";
            dirty = true;
        }

        if (dirty) ConfigStore.Save(_config);
    }

    private ITranscriber? BuildTranscriber()
    {
        try
        {
            if (_config.Whisper.Provider == "openai")
            {
                var key = SecretProtector.Unprotect(_config.Whisper.OpenAi.ApiKeyProtected);
                if (string.IsNullOrWhiteSpace(key)) return null;
                return new OpenAiTranscriber(key, _config.Whisper.OpenAi.Model, _config.Whisper.Language);
            }
            if (!_modelMgr.IsAvailable) return null;
            return new LocalWhisperTranscriber(_modelMgr.ModelPath, _config.Whisper.Language, _config.Whisper.InitialPrompt);
        }
        catch (Exception ex)
        {
            Logger.Error("BuildTranscriber", ex);
            return null;
        }
    }

    private void RebuildTranscriber()
    {
        if (_speech == null) return;
        var t = BuildTranscriber();
        _speech.SetTranscriber(t);
        if (t != null)
        {
            LastTrigger.Text = $"Sağlayıcı: {t.Name} ✓";
        }
        else if (_config.Whisper.Provider == "openai")
        {
            LastTrigger.Text = "OpenAI API key girilmedi — alt panele yapıştır";
        }
        else
        {
            LastTrigger.Text = "Yerel Whisper modeli yok — alt panelden 'Modeli indir'";
        }
        UpdateInitialPromptPreview();
    }

    private void UpdateInitialPromptPreview()
    {
        var p = _config?.Whisper?.InitialPrompt ?? "";
        InitialPromptPreview.Text = string.IsNullOrWhiteSpace(p) ? "—" : p;
        InitialPromptPreview.ToolTip = string.IsNullOrWhiteSpace(p) ? null : p;
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_modelMgr.IsAvailable)
            {
                MessageBox.Show(this, "Önce yerel Whisper modelini indir.", "Ses Kalibrasyonu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var calibTranscriber = new LocalWhisperTranscriber(_modelMgr.ModelPath, _config.Whisper.Language, _config.Whisper.InitialPrompt);
            var dlg = new Views.CalibrationWindow(calibTranscriber) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultPrompt))
            {
                _config.Whisper.InitialPrompt = dlg.ResultPrompt;
                ConfigStore.Save(_config);
                RebuildTranscriber();
                LastTrigger.Text = "Initial prompt güncellendi ✓";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Calibrate", ex);
            MessageBox.Show(this, $"Kalibrasyon hatası:\n{ex.Message}", "Ses Kalibrasyonu",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try { _controller?.Dispose(); } catch { }
        try { _speech?.Dispose(); } catch { }
        try { _tts?.Dispose(); } catch { }
        try { _ccUsage?.Dispose(); } catch { }
        try { _usageOverlay?.Close(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _chatEndNotifier?.Dispose(); } catch { }
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ConnDot.Fill = connected ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) : new SolidColorBrush(Color.FromRgb(0xB9, 0x4A, 0x4A));
            ConnText.Text = connected ? "Kontrolcü bağlı" : "Kontrolcü bağlı değil";
        }));
    }

    private void OnButton(object? sender, ButtonEventArgs e)
    {
        if (e.Pressed) Dispatcher.BeginInvoke(new Action(() => LastTrigger.Text = e.Button));
        _dispatcher?.HandleButton(e.Button, e.Pressed);
    }

    private void OnStick(object? sender, StickEventArgs e)
    {
        if (e.Active)
        {
            var s = $"{e.Stick} → {e.Direction}";
            Dispatcher.BeginInvoke(new Action(() => LastTrigger.Text = s));
        }
        _dispatcher?.HandleStick(e.Stick, e.Direction, e.Active);
    }

    private void BindingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BindingList.SelectedItem is not ListBoxItem item) return;
        _selectedKey = item.Tag as string;
        LoadSelectedIntoEditor();
    }

    private void LoadSelectedIntoEditor()
    {
        if (_selectedKey == null) return;
        var (label, binding) = ResolveBinding(_selectedKey);
        if (binding == null) return;

        _suppressEditorEvents = true;
        SelectedKeyLabel.Text = label;
        ActionTypeBox.SelectedIndex = binding.Type switch
        {
            ActionType.Voice => 1,
            ActionType.ReadSelection => 2,
            _ => 0
        };
        TextBoxValue.Text = binding.Text.Replace("\n", Environment.NewLine);
        TextBoxValue.IsEnabled = binding.Type == ActionType.Text;
        TextLabel.Visibility = binding.Type == ActionType.Text ? Visibility.Visible : Visibility.Collapsed;
        _suppressEditorEvents = false;
    }

    private (string Label, ButtonBinding? Binding) ResolveBinding(string key)
    {
        var label = ListEntries.FirstOrDefault(x => x.Key == key).Label ?? key;
        if (key.Contains('.'))
        {
            var parts = key.Split('.');
            if (!_config.Sticks.TryGetValue(parts[0], out var sb)) return (label, null);
            return (label, parts[1] switch
            {
                "Up" => sb.Up,
                "Down" => sb.Down,
                "Left" => sb.Left,
                "Right" => sb.Right,
                _ => null
            });
        }
        return _config.Buttons.TryGetValue(key, out var bind) ? (label, bind) : (label, null);
    }

    private void ActionTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditorEvents || _selectedKey == null) return;
        var (_, binding) = ResolveBinding(_selectedKey);
        if (binding == null) return;
        binding.Type = ActionTypeBox.SelectedIndex switch
        {
            1 => ActionType.Voice,
            2 => ActionType.ReadSelection,
            _ => ActionType.Text
        };
        TextBoxValue.IsEnabled = binding.Type == ActionType.Text;
        TextLabel.Visibility = binding.Type == ActionType.Text ? Visibility.Visible : Visibility.Collapsed;
        ConfigStore.Save(_config);
    }

    private void TextBoxValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents || _selectedKey == null) return;
        var (_, binding) = ResolveBinding(_selectedKey);
        if (binding == null) return;
        binding.Text = TextBoxValue.Text.Replace(Environment.NewLine, "\n");
        ConfigStore.Save(_config);
    }

    private void AutostartBox_Toggle(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = AutostartBox.IsChecked == true;
        AutostartService.SetEnabled(on);
        _config.Autostart = on;
        ConfigStore.Save(_config);
    }

    private static readonly double[] TtsRateValues = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

    private void SetTtsRateSelection(double multiplier)
    {
        int idx = 1;
        double best = double.MaxValue;
        for (int i = 0; i < TtsRateValues.Length; i++)
        {
            double d = Math.Abs(TtsRateValues[i] - multiplier);
            if (d < best) { best = d; idx = i; }
        }
        TtsRateBox.SelectedIndex = idx;
    }

    private void TtsRateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        int idx = TtsRateBox.SelectedIndex;
        if (idx < 0 || idx >= TtsRateValues.Length) return;
        double m = TtsRateValues[idx];
        _config.Tts.RateMultiplier = m;
        ConfigStore.Save(_config);
        _tts?.SetRateMultiplier(m);
    }

    private void TtsVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        if (TtsVolumeLabel != null) TtsVolumeLabel.Text = v.ToString();
        if (_suppressSettingEvents || !IsLoaded) return;
        _config.Tts.Volume = v;
        ConfigStore.Save(_config);
        _tts?.SetVolume(v);
    }

    private void TtsTest_Click(object sender, RoutedEventArgs e)
    {
        if (_tts == null) return;
        var sample = _config.Whisper.Language == "en"
            ? "This is a test of the text to speech voice."
            : "Bu bir sesli okuma testidir. Hız ve ses düzeyini buradan ayarlayabilirsin.";
        var culture = _config.Whisper.Language == "en" ? "en-US" : "tr-TR";
        _tts.Speak(sample, culture);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        var progress = new Progress<double>(p =>
        {
            ModelStatus.Text = $"İndiriliyor: %{p * 100:F0}";
        });
        try
        {
            await _modelMgr.DownloadAsync(progress);
            UpdateModelStatus();
            RefreshModelRadioLabels();
            if (_config.Whisper.Provider == "local") RebuildTranscriber();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"İndirme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateModelStatus();
        }
        DownloadButton.IsEnabled = true;
    }

    private void UpdateModelStatus()
    {
        var entry = WhisperModelCatalog.FindByFileName(_modelMgr.ModelFileName);
        if (_modelMgr.IsAvailable)
        {
            ModelStatus.Text = $"✓ {entry.DisplayName} hazır ({new FileInfo(_modelMgr.ModelPath).Length / 1024 / 1024} MB)";
            DownloadButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModelStatus.Text = $"{entry.DisplayName} indirilmemiş — indirebilirsin →";
            DownloadButton.Content = $"Modeli indir (~{entry.SizeMB} MB)";
            DownloadButton.Visibility = Visibility.Visible;
        }
    }

    private void BuildModelRadios()
    {
        ModelRadios.Children.Clear();
        _modelRadios.Clear();
        foreach (var entry in WhisperModelCatalog.Models)
        {
            var rb = new RadioButton
            {
                GroupName = "LocalModel",
                Tag = entry.FileName,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            rb.Checked += ModelRadio_Checked;
            ModelRadios.Children.Add(rb);
            _modelRadios[entry.FileName] = rb;
        }
        RefreshModelRadioLabels();

        string target = _modelRadios.ContainsKey(_config.Whisper.ModelFileName)
            ? _config.Whisper.ModelFileName
            : "ggml-small.bin";
        if (_modelRadios.TryGetValue(target, out var sel)) sel.IsChecked = true;
        else if (_modelRadios.Count > 0) _modelRadios.Values.First().IsChecked = true;
    }

    private void RefreshModelRadioLabels()
    {
        foreach (var entry in WhisperModelCatalog.Models)
        {
            if (!_modelRadios.TryGetValue(entry.FileName, out var rb)) continue;
            var path = Path.Combine(ConfigStore.ModelsDir, entry.FileName);
            bool available = File.Exists(path) && new FileInfo(path).Length > 30_000_000;
            var suffix = available ? "  ✓ indirili" : "  · indirilmemiş";
            rb.Content = entry.Label + suffix;
        }
    }

    private void ModelRadio_Checked(object? sender, RoutedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        if (sender is not RadioButton rb || rb.Tag is not string fileName) return;
        if (fileName == _config.Whisper.ModelFileName && _modelMgr.ModelFileName == fileName) return;

        _config.Whisper.ModelFileName = fileName;
        ConfigStore.Save(_config);
        _modelMgr = new WhisperModelManager(fileName);
        UpdateModelStatus();
        if (_config.Whisper.Provider == "local") RebuildTranscriber();
    }

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        if (LangBox.SelectedItem is ComboBoxItem item && item.Content is string lang)
        {
            _config.Whisper.Language = lang;
            ConfigStore.Save(_config);
            RebuildTranscriber();
        }
    }

    private void SetLangSelection(string lang)
    {
        foreach (ComboBoxItem item in LangBox.Items)
        {
            if ((string)item.Content == lang) { LangBox.SelectedItem = item; return; }
        }
        LangBox.SelectedIndex = 0;
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        _dispatcher?.ForceCloseOverlay();
        LastTrigger.Text = "Overlay zorla kapatıldı";
    }

    private void SetProviderSelection(string provider)
    {
        if (provider == "openai") OpenAiRadio.IsChecked = true;
        else LocalRadio.IsChecked = true;
    }

    private void SetOpenAiModelSelection(string model)
    {
        foreach (ComboBoxItem item in OpenAiModelBox.Items)
        {
            if ((string)item.Content == model) { OpenAiModelBox.SelectedItem = item; return; }
        }
        OpenAiModelBox.SelectedIndex = 1; // gpt-4o-mini-transcribe
    }

    private void UpdateProviderPanels()
    {
        bool openai = OpenAiRadio.IsChecked == true;
        LocalPanel.Visibility = openai ? Visibility.Collapsed : Visibility.Visible;
        OpenAiPanel.Visibility = openai ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProviderChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        _config.Whisper.Provider = OpenAiRadio.IsChecked == true ? "openai" : "local";
        UpdateProviderPanels();
        ConfigStore.Save(_config);
        RebuildTranscriber();
    }

    private void ApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        _config.Whisper.OpenAi.ApiKeyProtected = SecretProtector.Protect(ApiKeyBox.Password);
        ConfigStore.Save(_config);
        if (_config.Whisper.Provider == "openai") RebuildTranscriber();
    }

    private void OpenAiModelBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingEvents || !IsLoaded) return;
        if (OpenAiModelBox.SelectedItem is ComboBoxItem item && item.Content is string m)
        {
            _config.Whisper.OpenAi.Model = m;
            ConfigStore.Save(_config);
            if (_config.Whisper.Provider == "openai") RebuildTranscriber();
        }
    }

    private async void TestOpenAi_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("API key boş.", "Test", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var btn = (Button)sender;
        btn.IsEnabled = false;
        var oldContent = btn.Content;
        btn.Content = "Test ediliyor...";
        try
        {
            // Generate a tiny silent WAV (1 sec of silence) and POST to /audio/transcriptions
            var tmp = Path.Combine(Path.GetTempPath(), $"ccxbox_test_{Guid.NewGuid():N}.wav");
            WriteSilentWav(tmp, 1);
            var model = (OpenAiModelBox.SelectedItem as ComboBoxItem)?.Content as string ?? "gpt-4o-mini-transcribe";
            using var t = new OpenAiTranscriber(key, model, "tr");
            var _result = await t.TranscribeAsync(tmp);
            try { File.Delete(tmp); } catch { }
            MessageBox.Show("Bağlantı başarılı ✓", "Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Hata:\n{ex.Message}", "Test başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        btn.Content = oldContent;
        btn.IsEnabled = true;
    }

    private static void WriteSilentWav(string path, int seconds)
    {
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        const short channels = 1;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int dataSize = seconds * byteRate;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
    }
}
