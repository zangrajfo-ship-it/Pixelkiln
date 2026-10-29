using System.Windows;
using Pixelkiln.Core;

namespace Pixelkiln.App;

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
        // a crash leaves its reason next to the settings (local file only), so it can be found and fixed
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(Path.Combine(Core.Settings.Folder, "crash.log"), $"{DateTime.Now:O}{Environment.NewLine}{ev.ExceptionObject}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        StartupPath = e.Args.FirstOrDefault();
        LoadPlugins();
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
        if (Project is not { } p) return;
        if (p.FindByPath(path) is null)
            try
            {
                p.Add(path);
                ProjectChanged?.Invoke();
            }
            catch (InvalidOperationException) { return; }   // outside the project folder or not an asset type: just a loose file
        RecordVersion(path);
    }

    /// <summary>Data plugins from the plugins folder next to the settings, loaded at start and on Reload.</summary>
    public static List<Plugin> Plugins { get; private set; } = [];
    public static List<RejectedPlugin> RejectedPlugins { get; private set; } = [];

    public static void RememberCommand(string title)
    {
        Settings.AddRecentCommand(title);
        SaveSettings();
    }

    public static void LoadPlugins()
    {
        try { (Plugins, RejectedPlugins) = PluginLoader.LoadAll(PluginLoader.DefaultFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { (Plugins, RejectedPlugins) = ([], [new(PluginLoader.DefaultFolder, ex.Message)]); }
    }

    /// <summary>Keeps the file's current content in the project's .history (opened and saved files pass through here).</summary>
    public static void RecordVersion(string path, string note = "")
    {
        if (Project is not { } p || p.FindByPath(path) is not { } a) return;
        try { new VersionStore(p).Record(a.Id, note); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // history is a safety net; never block saving
    }

    /// <summary>Raised after a version was restored, so an editor showing that file can reload it.</summary>
    public static event Action<string>? AssetRestored;
    public static void NotifyAssetRestored(string path) => AssetRestored?.Invoke(path);

    static void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // preferences are a convenience; never block work
    }

#pragma warning disable WPF0001   // Fluent ThemeMode is marked experimental
    void ApplyTheme()
    {
        bool systemLight = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1;
        ThemeMode = Settings.Theme switch { "Light" => ThemeMode.Light, "System" => ThemeMode.System, _ => ThemeMode.Dark };
        // tokens, Fluent colours (Modern) and fonts; secondary text keeps at least 4.5:1 contrast (WCAG 1.4.3)
        Theme.HookWindows();
        Theme.Apply(this, Settings.Theme, systemLight);
    }
#pragma warning restore WPF0001
}
