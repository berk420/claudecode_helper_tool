using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CCXboxController.Services;

namespace CCXboxController.Views;

public partial class UsageOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public UsageOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Reposition();
        LblBlock.Text = "veri yükleniyor…";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ApplyStats(UsageStats? s)
    {
        if (s == null)
        {
            LblModel.Text = "—";
            LblBlock.Text = "veri yok";
            BlockProgress.Value = 0;
            return;
        }

        LblModel.Text = string.IsNullOrEmpty(s.ModelDisplay) ? "—" : s.ModelDisplay;
        if (s.HasActiveBlock)
        {
            var pct = s.HasUsagePercent ? $" · %{s.BlockUsagePercent:F0}" : "";
            LblBlock.Text = $"${s.BlockCostUsd:F2} · {FormatTokens(s.BlockTokens)}{pct}";
        }
        else
        {
            LblBlock.Text = "aktif blok yok";
        }
        BlockProgress.Value = s.HasUsagePercent ? Math.Clamp(s.BlockUsagePercent, 0, 100) : 0;
    }

    private static string FormatTokens(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F2}M";
        if (n >= 1_000) return $"{n / 1_000.0:F1}k";
        return n.ToString();
    }

    public void Reposition()
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Left + 16;
        Top = work.Bottom - ActualHeight - 16;
    }

    public void ShowOverlay()
    {
        if (!IsVisible) Show();
        Reposition();
        Topmost = false;
        Topmost = true;
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
    }
}
