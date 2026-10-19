using System.Windows;
using PixelForge.Core;

namespace PixelForge.App;

public partial class App : Application
{
    /// <summary>Loaded before any window exists, so editors can read it in field initializers.</summary>
    public static Settings Settings { get; private set; } = Settings.Load();

    /// <summary>Raised after Preferences are saved; editors redraw grid/checkerboard.</summary>
    public static event Action? SettingsChanged;

    /// <summary>The open project, or null when editing loose files (ADR-0001).</summary>
    public static Project? Project { get; private set; }

    /// <summary>Raised when a project is opened or closed, and when its asset list changes.</summary>
    public static event Action? ProjectChanged;

    public static void SetProject(Project? p)
    {
        Project = p;
        Settings.LastProject = p?.FilePath;
        SaveSettings();
        ProjectChanged?.Invoke();
    }

    public static void NotifyProjectChanged() => ProjectChanged?.Invoke();

    /// <summary>Project file or folder passed on the command line.</summary>
    public static string? StartupPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupPath = e.Args.FirstOrDefault();
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

    /// <summary>
    /// Remembers a file for File → Open recent and, if it lies inside the open project,
    /// registers it as an asset (so saving into the project folder is enough to add it).
    /// </summary>
    public static void AddRecent(string path)
    {
        Settings.AddRecent(path);
        SaveSettings();
        if (Project is not { } p || p.FindByPath(path) is not null) return;
        try
        {
            p.Add(path);
            ProjectChanged?.Invoke();
        }
        catch (InvalidOperationException) { }   // outside the project folder or not an asset type: just a loose file
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
