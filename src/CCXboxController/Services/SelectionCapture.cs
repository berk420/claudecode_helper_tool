using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CCXboxController.Services;

public static class SelectionCapture
{
    public static async Task<string?> CaptureAsync(Dispatcher uiDispatcher)
    {
        string? originalClipboard = null;
        bool hadOriginal = false;
        try
        {
            (originalClipboard, hadOriginal) = await uiDispatcher.InvokeAsync(ReadClipboardSafe);
        }
        catch (Exception ex) { Logger.Error("SelectionCapture.backup", ex); }

        try { KeyboardInjector.SendCopyShortcut(); }
        catch (Exception ex)
        {
            Logger.Error("SelectionCapture.sendCtrlC", ex);
            return null;
        }

        string? captured = null;
        for (int i = 0; i < 6 && captured == null; i++)
        {
            await Task.Delay(50);
            try
            {
                var (text, has) = await uiDispatcher.InvokeAsync(ReadClipboardSafe);
                if (has && !string.IsNullOrEmpty(text) && text != originalClipboard)
                {
                    captured = text;
                    break;
                }
                if (!hadOriginal && has && !string.IsNullOrEmpty(text))
                {
                    captured = text;
                    break;
                }
            }
            catch (Exception ex) { Logger.Error($"SelectionCapture.read[{i}]", ex); }
        }

        if (hadOriginal && originalClipboard != null)
        {
            try
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    try { Clipboard.SetText(originalClipboard); }
                    catch (Exception ex) { Logger.Error("SelectionCapture.restore", ex); }
                });
            }
            catch (Exception ex) { Logger.Error("SelectionCapture.restoreInvoke", ex); }
        }

        return captured;
    }

    private static (string? text, bool hasText) ReadClipboardSafe()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                return (Clipboard.GetText(), true);
            }
        }
        catch
        {
        }
        return (null, false);
    }
}
