using System;
using System.Windows;
using System.Windows.Threading;

namespace CCXboxController;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowError(ex);
            }
        };
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            $"Beklenmeyen hata:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
            "CCXboxController",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
