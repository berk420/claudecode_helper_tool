using System;
using System.Windows;
using CCXboxController.Services;

namespace CCXboxController.Views;

public partial class CalibrationWindow : Window
{
    private const string Reference =
        "Bugün Claude Code üzerinde yeni bir branch açtım ve dispatcher servisini refactor ediyorum. " +
        "ControllerService içindeki BeginInvoke çağrılarını gözden geçirdim, çünkü polling thread UI'ı bloklayamaz. " +
        "Repo'da pending bir pull request var, review'dan sonra merge edeceğim. " +
        "Whisper modeli ile push-to-talk özelliğini test ettim — initial prompt sayesinde teknik kelimeleri daha doğru yazıyor. " +
        "WPF overlay'in WS_EX_TRANSPARENT flag'i sayesinde click-through çalışıyor, VS Code'a focus geçmiyor.";

    private readonly SpeechService _speech;
    private readonly bool _ownsSpeech;

    public string? ResultPrompt { get; private set; }

    public CalibrationWindow(ITranscriber? transcriber)
    {
        InitializeComponent();
        ReferenceText.Text = Reference;

        _speech = new SpeechService(transcriber);
        _ownsSpeech = true;
        _speech.Transcribed += OnTranscribed;
        _speech.Error += OnError;

        if (transcriber == null)
        {
            RecordButton.IsEnabled = false;
            StatusText.Text = "Yerel Whisper modeli yok — önce ana panelden modeli indir.";
        }

        Closed += (_, _) =>
        {
            try { if (_speech.IsRecording) _speech.StopRecording(); } catch { }
            if (_ownsSpeech) { try { _speech.Dispose(); } catch { } }
        };
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speech.IsRecording)
        {
            _speech.StopRecording();
            RecordButton.IsEnabled = false;
            RecordButton.Content = "● Kayıt Başlat";
            StatusText.Text = "Transkript ediliyor…";
        }
        else
        {
            TranscriptBox.Text = "";
            _speech.StartRecording();
            if (!_speech.IsRecording) return;
            RecordButton.Content = "■ Durdur";
            StatusText.Text = "Kayıt yapılıyor — paragrafı oku, bittiğinde Durdur'a bas.";
        }
    }

    private void OnTranscribed(object? sender, string text)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TranscriptBox.Text = text ?? "";
            RecordButton.IsEnabled = true;
            StatusText.Text = string.IsNullOrWhiteSpace(text)
                ? "Boş transkript — tekrar dene."
                : "Hazır — düzenle ve kaydet.";
        }));
    }

    private void OnError(object? sender, Exception ex)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RecordButton.IsEnabled = true;
            RecordButton.Content = "● Kayıt Başlat";
            StatusText.Text = $"Hata: {ex.Message}";
        }));
    }

    private void UseReference_Click(object sender, RoutedEventArgs e)
    {
        TranscriptBox.Text = Reference;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultPrompt = null;
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = (TranscriptBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "Boş prompt kaydedilemez.";
            return;
        }
        ResultPrompt = text;
        DialogResult = true;
        Close();
    }
}
