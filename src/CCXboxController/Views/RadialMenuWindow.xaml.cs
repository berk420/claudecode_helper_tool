using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CCXboxController.Models;
using CCXboxController.Services;

namespace CCXboxController.Views;

public partial class RadialMenuWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private readonly DispatcherTimer _watchdog;

    public RadialMenuWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _watchdog.Tick += (_, _) => HideMenu();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ShowForStick(StickId stick, StickBinding binding, StickDirection initial)
    {
        LblStick.Text = stick == StickId.Left ? "LEFT STICK" : "RIGHT STICK";
        LblUp.Text = Trim(binding.Up.Text);
        LblDown.Text = Trim(binding.Down.Text);
        LblLeft.Text = Trim(binding.Left.Text);
        LblRight.Text = Trim(binding.Right.Text);

        // Center on primary screen
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - 280) / 2;
        Top = work.Top + (work.Height - 280) / 2;

        UpdateDirection(initial);
        if (!IsVisible) Show();
        // Force topmost: other always-on-top windows can dethrone us — flicker the flag.
        Topmost = false;
        Topmost = true;
        RestartWatchdog();
    }

    private void RestartWatchdog()
    {
        _watchdog.Stop();
        _watchdog.Start();
    }

    public void UpdateDirection(StickDirection dir)
    {
        RestartWatchdog();
        LblUp.FontWeight = dir == StickDirection.Up ? FontWeights.Bold : FontWeights.Normal;
        LblDown.FontWeight = dir == StickDirection.Down ? FontWeights.Bold : FontWeights.Normal;
        LblLeft.FontWeight = dir == StickDirection.Left ? FontWeights.Bold : FontWeights.Normal;
        LblRight.FontWeight = dir == StickDirection.Right ? FontWeights.Bold : FontWeights.Normal;

        LblUp.Foreground = dir == StickDirection.Up ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) : Brushes.White;
        LblDown.Foreground = dir == StickDirection.Down ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) : Brushes.White;
        LblLeft.Foreground = dir == StickDirection.Left ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) : Brushes.White;
        LblRight.Foreground = dir == StickDirection.Right ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) : Brushes.White;
    }

    public void HideMenu()
    {
        _watchdog.Stop();
        if (IsVisible) Hide();
    }

    private static string Trim(string s)
    {
        s = s.Replace("\r", "").Replace("\n", "⏎");
        return s.Length > 60 ? s.Substring(0, 57) + "…" : s;
    }
}
