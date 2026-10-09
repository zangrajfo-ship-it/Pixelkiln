using System.Windows;
using PixelForge.Core;

namespace PixelForge.App;

public partial class App : Application
{
    /// <summary>Loaded before any window exists, so editors can read it in field initializers.</summary>
    public static Settings Settings { get; private set; } = Settings.Load();

    /// <summary>Raised after Preferences are saved; editors redraw grid/checkerboard.</summary>
    public static event Action? SettingsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyTheme();   // after App.xaml resources are loaded, before the main window opens
        base.OnStartup(e);
    }

    public static void UpdateSettings(Settings s)
    {
        Settings = s.Sanitized();
        SaveSettings();
        ((App)Current).ApplyTheme();
        SettingsChanged?.Invoke();
    }

    /// <summary>Remembers a file for File → Open recent.</summary>
    public static void AddRecent(string path)
    {
        Settings.AddRecent(path);
        SaveSettings();
    }

    static void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // preferences are a convenience; never block work
    }

#pragma warning disable WPF0001   // Fluent ThemeMode is marked experimental
    void ApplyTheme()
    {
        bool light = Settings.Theme == "Light"
            || Settings.Theme == "System" && Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1;
        ThemeMode = Settings.Theme switch { "Light" => ThemeMode.Light, "System" => ThemeMode.System, _ => ThemeMode.Dark };
        // secondary text with at least 7:1 contrast on either background (WCAG 1.4.3)
        Resources["MutedText"] = new System.Windows.Media.SolidColorBrush(light
            ? System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)
            : System.Windows.Media.Color.FromRgb(0xB8, 0xB8, 0xB8));
    }
#pragma warning restore WPF0001
}
