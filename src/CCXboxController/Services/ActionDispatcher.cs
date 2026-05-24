using System;
using System.Windows;
using System.Windows.Threading;
using CCXboxController.Models;
using CCXboxController.Views;

namespace CCXboxController.Services;

public class ActionDispatcher
{
    private readonly AppConfig _config;
    private SpeechService? _speech;
    private readonly Dispatcher _uiDispatcher;
    private RadialMenuWindow? _menu;
    private VoiceStatusWindow? _voiceStatus;
    private StickDirection _leftDir = StickDirection.None;
    private StickDirection _rightDir = StickDirection.None;

    public event EventHandler<string>? Status;

    public ActionDispatcher(AppConfig config, SpeechService? speech, Dispatcher uiDispatcher)
    {
        _config = config;
        _uiDispatcher = uiDispatcher;
        AttachSpeech(speech);
    }

    public void AttachSpeech(SpeechService? speech)
    {
        _speech = speech;
        if (_speech != null)
        {
            _speech.Transcribed += (_, text) =>
            {
                HideVoiceStatus();
                try
                {
                    if (!string.IsNullOrWhiteSpace(text)) KeyboardInjector.TypeText(text);
                }
                catch (Exception ex) { Logger.Error("Transcribed→TypeText", ex); }
            };
            _speech.Error += (_, _) => HideVoiceStatus();
        }
    }

    private void ShowVoiceStatus(bool recording)
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _voiceStatus ??= new VoiceStatusWindow();
                if (recording) _voiceStatus.ShowRecording();
                else _voiceStatus.ShowTranscribing();
            }
            catch (Exception ex) { Logger.Error("VoiceStatus show", ex); }
        }));
    }

    private void HideVoiceStatus()
    {
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            try { _voiceStatus?.HideStatus(); }
            catch (Exception ex) { Logger.Error("VoiceStatus hide", ex); }
        }));
    }

    public void HandleButton(string button, bool pressed)
    {
        try
        {
            if (!_config.Buttons.TryGetValue(button, out var bind)) return;

            if (bind.Type == ActionType.Voice)
            {
                if (_speech == null || !_speech.HasTranscriber)
                {
                    if (pressed)
                    {
                        var msg = _config.Whisper.Provider == "openai"
                            ? "OpenAI API key boş — alt panele yapıştır."
                            : "Yerel Whisper modeli yok — alt panelden 'Modeli indir'e bas.";
                        Status?.Invoke(this, msg);
                    }
                    return;
                }
                if (pressed)
                {
                    _speech.StartRecording();
                    ShowVoiceStatus(recording: true);
                }
                else
                {
                    _speech.StopRecording();
                    ShowVoiceStatus(recording: false);
                }
                return;
            }

            if (pressed && bind.Type == ActionType.Text && !string.IsNullOrEmpty(bind.Text))
            {
                KeyboardInjector.TypeText(bind.Text);
            }
        }
        catch (Exception ex) { Logger.Error($"HandleButton({button},{pressed})", ex); }
    }

    public void HandleStick(StickId id, StickDirection dir, bool active)
    {
        try
        {
            string key = id == StickId.Left ? "LeftStick" : "RightStick";
            if (!_config.Sticks.TryGetValue(key, out var sb)) return;

            if (active)
            {
                if (id == StickId.Left) _leftDir = dir; else _rightDir = dir;
                _uiDispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _menu ??= new RadialMenuWindow();
                        _menu.ShowForStick(id, sb, dir);
                        _menu.UpdateDirection(dir);
                    }
                    catch (Exception ex) { Logger.Error("Menu show", ex); }
                }));
            }
            else
            {
                var lastDir = id == StickId.Left ? _leftDir : _rightDir;
                if (id == StickId.Left) _leftDir = StickDirection.None; else _rightDir = StickDirection.None;

                _uiDispatcher.BeginInvoke(new Action(() =>
                {
                    try { _menu?.HideMenu(); }
                    catch (Exception ex) { Logger.Error("Menu hide", ex); }
                }));

                var bind = lastDir switch
                {
                    StickDirection.Up => sb.Up,
                    StickDirection.Down => sb.Down,
                    StickDirection.Left => sb.Left,
                    StickDirection.Right => sb.Right,
                    _ => null
                };
                if (bind != null && bind.Type == ActionType.Text && !string.IsNullOrEmpty(bind.Text))
                {
                    KeyboardInjector.TypeText(bind.Text);
                }
            }
        }
        catch (Exception ex) { Logger.Error($"HandleStick({id},{dir},{active})", ex); }
    }

    public void ForceCloseOverlay()
    {
        _leftDir = StickDirection.None;
        _rightDir = StickDirection.None;
        _uiDispatcher.BeginInvoke(new Action(() =>
        {
            try { _menu?.HideMenu(); }
            catch (Exception ex) { Logger.Error("ForceCloseOverlay", ex); }
        }));
    }
}
