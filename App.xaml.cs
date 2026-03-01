using System;
using System.Windows;
using DersDagitim.Persistence;

namespace DersDagitim;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handler
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"Hata: {args.Exception.Message}", "Uygulama Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    // Keep for OrTools engine compatibility
    public static void LogToDisk(string msg) 
    { 
        try
        {
            System.IO.File.AppendAllText("ortools_edit.log", $"{DateTime.Now:HH:mm:ss.fff} - {msg}\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DatabaseManager.Shared.CloseDatabase();
        base.OnExit(e);
    }
}
