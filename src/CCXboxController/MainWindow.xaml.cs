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
    private ActionDispatcher? _dispatcher;
    private WhisperModelManager _modelMgr = null!;

    private static readonly List<(string Key, string Label)> ListEntries = new()
    {
        ("A", "A  (varsayılan: Sesli yazım)"),
        ("B", "B"),
        ("X", "X"),
        ("Y", "Y"),
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
        UpdateModelStatus();
        AutostartBox.IsChecked = AutostartService.IsEnabled();
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

        _dispatcher = new ActionDispatcher(_config, _speech, Dispatcher);
        _dispatcher.Status += (_, msg) => Dispatcher.BeginInvoke(new Action(() => LastTrigger.Text = msg));
        _controller = new ControllerService();
        _controller.ConnectionChanged += OnConnectionChanged;
        _controller.ButtonEvent += OnButton;
        _controller.StickEvent += OnStick;
        _controller.Start();

        if (BindingList.Items.Count > 0) BindingList.SelectedIndex = 0;

        RebuildTranscriber();
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
        ActionTypeBox.SelectedIndex = binding.Type == ActionType.Voice ? 1 : 0;
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
        binding.Type = ActionTypeBox.SelectedIndex == 1 ? ActionType.Voice : ActionType.Text;
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
        if (_modelMgr.IsAvailable)
        {
            ModelStatus.Text = $"✓ {_modelMgr.ModelFileName} ({new FileInfo(_modelMgr.ModelPath).Length / 1024 / 1024} MB)";
            DownloadButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModelStatus.Text = "indirilmedi";
            DownloadButton.Visibility = Visibility.Visible;
        }
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
