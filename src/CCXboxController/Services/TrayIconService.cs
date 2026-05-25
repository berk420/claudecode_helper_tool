using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;

namespace CCXboxController.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _overlayItem;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler<bool>? OverlayToggleRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(bool overlayEnabled)
    {
        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Claude Code · Xbox Controller"
        };

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Pencereyi aç");
        openItem.Click += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(openItem);

        _overlayItem = new ToolStripMenuItem("Kullanım overlay'i") { CheckOnClick = true, Checked = overlayEnabled };
        _overlayItem.CheckedChanged += (_, _) => OverlayToggleRequested?.Invoke(this, _overlayItem.Checked);
        menu.Items.Add(_overlayItem);

        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("Çıkış");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetOverlayChecked(bool value)
    {
        if (_overlayItem.Checked != value) _overlayItem.Checked = value;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
            if (info?.Stream != null)
            {
                using var s = info.Stream;
                return new Icon(s);
            }
        }
        catch (Exception ex) { Logger.Error("TrayIcon load", ex); }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        try { _icon.Visible = false; } catch { }
        try { _icon.Dispose(); } catch { }
    }
}
