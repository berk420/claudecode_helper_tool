using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace CCXboxController.Views;

public partial class VoiceStatusWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private Storyboard? _pulse;
    private Storyboard? _spin;

    public VoiceStatusWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => BuildStoryboards();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void BuildStoryboards()
    {
        var pulseAnim = new DoubleAnimation
        {
            From = 0.75, To = 1.25,
            Duration = TimeSpan.FromMilliseconds(550),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        _pulse = new Storyboard();
        Storyboard.SetTarget(pulseAnim, RecDotScale);
        Storyboard.SetTargetProperty(pulseAnim, new PropertyPath("ScaleX"));
        _pulse.Children.Add(pulseAnim);
        var pulseAnimY = pulseAnim.Clone();
        Storyboard.SetTarget(pulseAnimY, RecDotScale);
        Storyboard.SetTargetProperty(pulseAnimY, new PropertyPath("ScaleY"));
        _pulse.Children.Add(pulseAnimY);

        var spinAnim = new DoubleAnimation
        {
            From = 0, To = 360,
            Duration = TimeSpan.FromMilliseconds(900),
            RepeatBehavior = RepeatBehavior.Forever
        };
        _spin = new Storyboard();
        Storyboard.SetTarget(spinAnim, ArcRotate);
        Storyboard.SetTargetProperty(spinAnim, new PropertyPath("Angle"));
        _spin.Children.Add(spinAnim);
    }

    public void ShowRecording()
    {
        LblStatus.Text = "Kayıt";
        RecDot.Visibility = Visibility.Visible;
        TransArc.Visibility = Visibility.Collapsed;
        EnsureVisible();
        _spin?.Stop(this);
        _pulse?.Begin(this, true);
    }

    public void ShowTranscribing()
    {
        LblStatus.Text = "Yazıya çevriliyor";
        RecDot.Visibility = Visibility.Collapsed;
        TransArc.Visibility = Visibility.Visible;
        EnsureVisible();
        _pulse?.Stop(this);
        _spin?.Begin(this, true);
    }

    public void HideStatus()
    {
        _pulse?.Stop(this);
        _spin?.Stop(this);
        if (IsVisible) Hide();
    }

    private void EnsureVisible()
    {
        if (!IsVisible) Show();
        // bottom-right corner of work area, with margin
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 24;
        Top = work.Bottom - ActualHeight - 24;
        Topmost = false;
        Topmost = true;
    }
}
