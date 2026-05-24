using System;
using System.Threading;
using System.Threading.Tasks;
using CCXboxController.NativeMethods;

namespace CCXboxController.Services;

public enum StickDirection { None, Up, Down, Left, Right }
public enum StickId { Left, Right }

public class ButtonEventArgs : EventArgs
{
    public string Button { get; init; } = string.Empty;
    public bool Pressed { get; init; }
}

public class StickEventArgs : EventArgs
{
    public StickId Stick { get; init; }
    public StickDirection Direction { get; init; }
    public bool Active { get; init; }
}

public class ControllerService : IDisposable
{
    // Hysteresis: enter active state at higher threshold, leave at lower.
    // Enter matches Microsoft's XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE so the radial
    // menu pops as soon as the user starts tilting, not at half-press.
    private const int EnterDeadZone = 7849;
    private const int ExitDeadZone = 5000;
    private const int PollIntervalMs = 16;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private XInputState _prev;
    private bool _hasPrev;
    private StickDirection _prevLeft = StickDirection.None;
    private StickDirection _prevRight = StickDirection.None;
    private bool _connected;

    public event EventHandler<ButtonEventArgs>? ButtonEvent;
    public event EventHandler<StickEventArgs>? StickEvent;
    public event EventHandler<bool>? ConnectionChanged;

    private static readonly (string Name, ushort Mask)[] ButtonMap = new[]
    {
        ("A", XInputButtons.A),
        ("B", XInputButtons.B),
        ("X", XInputButtons.X),
        ("Y", XInputButtons.Y),
        ("LB", XInputButtons.LB),
        ("RB", XInputButtons.RB),
        ("Start", XInputButtons.Start),
        ("Back", XInputButtons.Back),
        ("DPadUp", XInputButtons.DPadUp),
        ("DPadDown", XInputButtons.DPadDown),
        ("DPadLeft", XInputButtons.DPadLeft),
        ("DPadRight", XInputButtons.DPadRight),
        ("LeftStickPress", XInputButtons.LeftThumb),
        ("RightStickPress", XInputButtons.RightThumb),
    };

    public void Start()
    {
        if (_loopTask != null) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken token)
    {
        Logger.Info("ControllerService loop started");
        while (!token.IsCancellationRequested)
        {
            try
            {
                uint result = XInputInterop.XInputGetState(0, out var state);
                bool nowConnected = result == 0;
                if (nowConnected != _connected)
                {
                    _connected = nowConnected;
                    Logger.Info($"Controller connection changed: {_connected}");
                    SafeInvoke(() => ConnectionChanged?.Invoke(this, _connected));
                    if (!_connected)
                    {
                        _hasPrev = false;
                        // Synthesize release events so any active overlays/recording stop cleanly.
                        if (_prevLeft != StickDirection.None)
                        {
                            SafeInvoke(() => StickEvent?.Invoke(this, new StickEventArgs { Stick = StickId.Left, Direction = StickDirection.None, Active = false }));
                            _prevLeft = StickDirection.None;
                        }
                        if (_prevRight != StickDirection.None)
                        {
                            SafeInvoke(() => StickEvent?.Invoke(this, new StickEventArgs { Stick = StickId.Right, Direction = StickDirection.None, Active = false }));
                            _prevRight = StickDirection.None;
                        }
                        if (_ltDown) { _ltDown = false; SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = "LT", Pressed = false })); }
                        if (_rtDown) { _rtDown = false; SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = "RT", Pressed = false })); }
                    }
                }

                if (_connected)
                {
                    ProcessState(state);
                    ProcessTriggers(state);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Controller loop body", ex);
            }

            try { await Task.Delay(PollIntervalMs, token); }
            catch (TaskCanceledException) { break; }
        }
        Logger.Info("ControllerService loop ended");
    }

    private static void SafeInvoke(Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Error("Event handler", ex); }
    }

    private bool _ltDown;
    private bool _rtDown;

    private void ProcessTriggers(XInputState s)
    {
        const byte TriggerThreshold = 64;
        bool lt = s.Gamepad.bLeftTrigger > TriggerThreshold;
        bool rt = s.Gamepad.bRightTrigger > TriggerThreshold;
        if (lt != _ltDown)
        {
            _ltDown = lt;
            SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = "LT", Pressed = lt }));
        }
        if (rt != _rtDown)
        {
            _rtDown = rt;
            SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = "RT", Pressed = rt }));
        }
    }

    private void ProcessState(XInputState s)
    {
        if (_hasPrev)
        {
            foreach (var (name, mask) in ButtonMap)
            {
                bool wasDown = (_prev.Gamepad.wButtons & mask) != 0;
                bool isDown = (s.Gamepad.wButtons & mask) != 0;
                if (wasDown != isDown)
                {
                    SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = name, Pressed = isDown }));
                }
            }
        }
        else
        {
            foreach (var (name, mask) in ButtonMap)
            {
                if ((s.Gamepad.wButtons & mask) != 0)
                {
                    SafeInvoke(() => ButtonEvent?.Invoke(this, new ButtonEventArgs { Button = name, Pressed = true }));
                }
            }
        }

        var leftDir = StickToDirection(s.Gamepad.sThumbLX, s.Gamepad.sThumbLY, _prevLeft);
        var rightDir = StickToDirection(s.Gamepad.sThumbRX, s.Gamepad.sThumbRY, _prevRight);
        if (leftDir != _prevLeft)
        {
            var captured = leftDir;
            SafeInvoke(() => StickEvent?.Invoke(this, new StickEventArgs { Stick = StickId.Left, Direction = captured, Active = captured != StickDirection.None }));
            _prevLeft = leftDir;
        }
        if (rightDir != _prevRight)
        {
            var captured = rightDir;
            SafeInvoke(() => StickEvent?.Invoke(this, new StickEventArgs { Stick = StickId.Right, Direction = captured, Active = captured != StickDirection.None }));
            _prevRight = rightDir;
        }

        _prev = s;
        _hasPrev = true;
    }

    private static StickDirection StickToDirection(short x, short y, StickDirection current)
    {
        long mag2 = (long)x * x + (long)y * y;
        bool wasActive = current != StickDirection.None;
        long threshold = wasActive ? (long)ExitDeadZone * ExitDeadZone : (long)EnterDeadZone * EnterDeadZone;
        if (mag2 < threshold) return StickDirection.None;
        int ax = Math.Abs(x);
        int ay = Math.Abs(y);
        if (ay >= ax)
            return y > 0 ? StickDirection.Up : StickDirection.Down;
        return x > 0 ? StickDirection.Right : StickDirection.Left;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loopTask?.Wait(500); } catch { }
        _cts?.Dispose();
    }
}
