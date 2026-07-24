using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PurpleStarNotes;

/// <summary>
/// Application entry point. Applies the saved theme before the main window is
/// shown, and installs a global handler so an unexpected error is logged and
/// surfaced instead of silently crashing the app.
/// </summary>
public partial class App : Application
{
    private static string LogPath => Path.Combine(NoteStore.Folder, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log(ev.ExceptionObject as Exception);

        try { ThemeManager.Apply(AppSettings.Load().ThemeEnum); }
        catch (Exception ex) { Log(ex); }

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(
            "Purple Star Notes ran into a problem but will try to keep running.\n\n" +
            e.Exception.Message +
            "\n\nDetails were saved to:\n" + LogPath,
            "Purple Star Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;   // keep the app alive
    }

    private static void Log(Exception? ex)
    {
        if (ex == null)
            return;
        try
        {
            Directory.CreateDirectory(NoteStore.Folder);
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* logging is best-effort */ }
    }
}
