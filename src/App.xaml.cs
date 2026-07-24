using System.Windows;

namespace PurpleStarNotes;

/// <summary>
/// Application entry point. Applies the saved theme before the main window is
/// shown, then loads MainWindow.xaml via the StartupUri.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Apply(AppSettings.Load().ThemeEnum);
        base.OnStartup(e);
    }
}
