using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PixelForge.Core;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    // order = autosave file index; new editors are appended, whatever their tab position
    IEditor[] Editors => [SpriteEd, MapEd, MusicEd, SfxEd, CharEd, SceneEd, AudioEd];

    TabItem TabOf(IEditor ed) => Tabs.Items.Cast<TabItem>().First(t => t.Content == ed);
    IEditor? Current => (Tabs?.SelectedItem as TabItem)?.Content as IEditor;   // SelectedContent lags behind SelectionChanged

    readonly DispatcherTimer _autosave = new();

    public MainWindow()
    {
        InitializeComponent();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
        CharEd.IsSpriteOpenDirty = path => SpriteEd.IsDirty && string.Equals(SpriteEd.CurrentPath, path, StringComparison.OrdinalIgnoreCase);
        CharEd.SpriteFileChanged = path =>
        {
            if (string.Equals(SpriteEd.CurrentPath, path, StringComparison.OrdinalIgnoreCase) && !SpriteEd.IsDirty) SpriteEd.OpenPath(path, confirmed: true);
        };
        Ui.NameSymbolButtons(this);
        _autosave.Tick += (_, _) => Autosave();
        Assets.OpenRequested += OpenAsset;
        Assets.FileMoved += (from, to) => { foreach (var ed in Editors) ed.OnFileMoved(from, to); };
        App.ProjectChanged += UpdateProjectTitle;
        App.SettingsChanged += ApplySettings;
        ApplySettings();
        // fill the menu before the first layout pass; an empty Fluent Menu filled later can stay zero-sized
        Tabs.SelectedIndex = 0;
        BuildMenu();
        Loaded += (_, _) =>
        {
            OfferRecovery();
            ReopenLastProject();
        };
        Closed += (_, _) =>
        {
            ClearAutosave();   // closed normally: every editor already asked about unsaved work
            AudioOut.DisposeShared();
        };
    }

    void ApplySettings()
    {
        ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(App.Settings.UiScale, App.Settings.UiScale);
        _autosave.Stop();
        if (App.Settings.AutosaveMinutes <= 0) return;
        _autosave.Interval = TimeSpan.FromMinutes(App.Settings.AutosaveMinutes);
        _autosave.Start();
    }

    // ---------- menu: the current tab's menus + Options + Help ----------

    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs) BuildMenu();
    }

    void BuildMenu()
    {
        if (MainMenu is null || Current is not { } ed) return;
        MainMenu.Items.Clear();
        foreach (var m in ed.Menus) MainMenu.Items.Add(m);
        MainMenu.Items.Add(ProjectMenu());
        MainMenu.Items.Add(Ui.Menu("_Options",
            Ui.Item("_Command palette…", "Ctrl+Shift+P", ShowCommandPalette),
            Ui.Item("_Preferences…", "Ctrl+,", () => PreferencesWindow.Show(this))));
        MainMenu.Items.Add(Ui.Menu("_Help",
            Ui.Item("_Keyboard shortcuts", "F1", () => HelpWindow.Show(this)),
            Ui.Item("_About PixelForge", null, () => MessageBox.Show(this,
                "PixelForge — sprites, tilemaps, music and sound effects for your games.", "About PixelForge"))));
    }

    // ---------- project ----------

    MenuItem ProjectMenu()
    {
        bool open = App.Project is not null;
        var recent = new MenuItem { Header = "Open _recent project" };
        foreach (var f in ProjectCommands.RecentProjects().Take(10))
            recent.Items.Add(Ui.Item(Path.GetFileNameWithoutExtension(f).Replace("_", "__"), null, () => ProjectCommands.OpenPath(f)));
        if (recent.Items.Count == 0) recent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
        var browser = new MenuItem { Header = "Show _asset browser", InputGestureText = "Ctrl+Shift+B", IsCheckable = true, IsChecked = AssetsVisible };
        browser.Click += (_, _) => SetAssetsVisible(browser.IsChecked);
        return Ui.Menu("_Project",
            Ui.Item("_New project…", null, () => ProjectCommands.New(this)),
            Ui.Item("_Open project…", null, () => ProjectCommands.Open(this)),
            recent,
            Ui.Item("Make project from _folder…", null, () => ProjectCommands.FromFolder(this)),
            Ui.Item("_Close project", null, ProjectCommands.Close, open),
            new Separator(),
            Ui.Item("C_heck project…", null, () => { if (App.Project is { } p) HealthWindow.Show(this, p); }, open),
            Ui.Item("Project _settings and collisions…", null, () => { if (App.Project is { } p) ProjectSettingsWindow.Show(this, p); }, open),
            Ui.Item("_Export…", "Ctrl+Shift+E", () =>
            {
                if (App.Project is { } p)
                    ExportWindow.Show(this, p, [.. Editors.Where(ed => ed.IsDirty).Select(ed => ed.CurrentPath ?? "untitled")]);
            }, open),
            Ui.Item("_Palette studio…", null, () =>
            {
                if (App.Project is { } p)
                    PaletteStudio.Show(this, p,
                        canTouch: path => !(SpriteEd.IsDirty && string.Equals(SpriteEd.CurrentPath, path, StringComparison.OrdinalIgnoreCase)),
                        changed: path => { if (string.Equals(SpriteEd.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) SpriteEd.OpenPath(path, confirmed: true); });
            }, open),
            browser);
    }

    bool AssetsVisible => AssetsColumn.Width.Value > 0;

    void SetAssetsVisible(bool show)
    {
        AssetsColumn.Width = new GridLength(show ? 270 : 0);
        AssetsSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BuildMenu();
    }

    /// <summary>A project or folder given on the command line wins; otherwise the last project reopens.</summary>
    void ReopenLastProject()
    {
        if (App.StartupPath is string arg)
        {
            OpenFromCommandLine(arg);
            return;
        }
        if (App.Settings.LastProject is not string path) return;
        if (File.Exists(path)) ProjectCommands.OpenPath(path);
        else App.SetProject(null);   // forget a project that is gone
    }

    /// <summary>"PixelForge.App.exe game.pfproj" opens it; a folder opens its project or becomes one.</summary>
    static void OpenFromCommandLine(string arg)
    {
        try
        {
            if (File.Exists(arg) && arg.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase)) { ProjectCommands.OpenPath(arg); return; }
            if (!Directory.Exists(arg)) throw new FileNotFoundException($"“{arg}” is neither a project nor a folder.");
            var existing = Directory.GetFiles(arg, "*" + Project.Extension).FirstOrDefault();
            if (existing is not null) ProjectCommands.OpenPath(existing);
            else App.SetProject(Project.CreateFromFolder(arg, Path.GetFileName(Path.GetFullPath(arg).TrimEnd(Path.DirectorySeparatorChar))));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    void UpdateProjectTitle() => BuildMenu();   // Close project / Check project enable with a project

    /// <summary>Opens an asset in the tab that edits its type.</summary>
    void OpenAsset(AssetEntry a)
    {
        if (App.Project is not { } p) return;
        string path = p.FullPath(a);
        if (!File.Exists(path)) { Ui.Error(new FileNotFoundException($"“{a.Path}” is missing. Use Project → Check project to fix the project.")); return; }
        IEditor? target = a.Type switch
        {
            AssetType.Sprite or AssetType.Image => SpriteEd,
            AssetType.Tilemap => MapEd,
            AssetType.Song => MusicEd,
            AssetType.Sound => SfxEd,
            AssetType.Character => CharEd,
            AssetType.Scene => SceneEd,
            AssetType.Mix => AudioEd,
            _ => null,
        };
        if (a.Type == AssetType.Audio)   // a WAV is material: it goes onto a track
        {
            Tabs.SelectedItem = TabOf(AudioEd);
            AudioEd.AddClip(a.Id);
            return;
        }
        if (a.Type == AssetType.Palette)
        {
            Tabs.SelectedIndex = 0;
            SpriteEd.ApplyPaletteFile(path);
            return;
        }
        if (target is null) { Ui.Error(new NotSupportedException($"{a.Type} assets open in their own studio.")); return; }
        Tabs.SelectedItem = TabOf(target);
        target.OpenPath(path);
    }

    // ---------- command palette ----------

    void ShowCommandPalette()
    {
        var commands = new List<Command>(CommandRegistry.FromMenu(MainMenu));
        foreach (TabItem tab in Tabs.Items)
        {
            var t = tab;
            commands.Add(new Command($"Go to {t.Header} tab", "View", null, () => true, () => Tabs.SelectedItem = t));
        }
        if (App.Project is { } p)
            foreach (var a in p.Assets)
            {
                var asset = a;
                commands.Add(new Command($"Open asset: {a.Path}", "Asset", null, () => File.Exists(p.FullPath(asset)), () => OpenAsset(asset)));
            }
        foreach (var source in CommandRegistry.Sources) commands.AddRange(source());
        CommandPalette.Show(this, commands);
    }

    // ---------- autosave + recovery ----------

    static string AutosaveDir => Path.Combine(Settings.Folder, "autosave");

    string AutosaveFile(int i) => Path.Combine(AutosaveDir, $"{i}{Editors[i].FileExtensions[0]}");

    /// <summary>Writes every editor with unsaved work; removes the copy of editors that are saved.</summary>
    void Autosave()
    {
        try
        {
            Directory.CreateDirectory(AutosaveDir);
            for (int i = 0; i < Editors.Length; i++)
            {
                string file = AutosaveFile(i);
                if (Editors[i].IsDirty)
                {
                    Editors[i].WriteAutosave(file);
                    File.WriteAllText(file + ".path", Editors[i].CurrentPath ?? "");
                }
                else { File.Delete(file); File.Delete(file + ".path"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // next tick tries again
    }

    void ClearAutosave()
    {
        try { if (Directory.Exists(AutosaveDir)) Directory.Delete(AutosaveDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Autosave files left behind mean the last session did not close normally.</summary>
    void OfferRecovery()
    {
        var found = Enumerable.Range(0, Editors.Length).Where(i => File.Exists(AutosaveFile(i))).ToList();
        if (found.Count == 0) return;
        string names = string.Join(", ", found.Select(i => TabOf(Editors[i]).Header));
        var r = MessageBox.Show(this, $"PixelForge did not close normally last time.\n\nRestore the unsaved work? ({names})",
            "Restore unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            foreach (int i in found)
            {
                try
                {
                    string? original = File.Exists(AutosaveFile(i) + ".path") ? File.ReadAllText(AutosaveFile(i) + ".path") : null;
                    Editors[i].RestoreAutosave(AutosaveFile(i), string.IsNullOrEmpty(original) ? null : original);
                }
                catch (Exception ex) { Ui.Error(ex); }
            }
        ClearAutosave();
    }

    // ---------- keyboard ----------

    /// <summary>
    /// Editor shortcuts run only when they don't take a key away from the focused control:
    /// text fields keep every key, combo boxes keep plain keys, buttons/lists/sliders keep
    /// Space, Enter and the navigation keys (WCAG 2.1.1).
    /// </summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.F1) { HelpWindow.Show(this); e.Handled = true; return; }
        if (e.Key == Key.OemComma && ctrl) { PreferencesWindow.Show(this); e.Handled = true; return; }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.P && ctrl && shift) { ShowCommandPalette(); e.Handled = true; return; }
        if (e.Key == Key.B && ctrl && shift) { SetAssetsVisible(!AssetsVisible); e.Handled = true; return; }
        if (e.Key == Key.E && ctrl && shift && App.Project is { } project)
        {
            ExportWindow.Show(this, project, [.. Editors.Where(ed => ed.IsDirty).Select(ed => ed.CurrentPath ?? "untitled")]);
            e.Handled = true;
            return;
        }
        if (Assets.IsKeyboardFocusWithin) return;   // the asset browser has its own keys (Del, F2, Enter …)
        if (ControlOwnsKey(e.OriginalSource, e.Key)) return;
        Current?.HandleKey(e);
    }

    static bool ControlOwnsKey(object source, Key key)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (source is TextBoxBase or PasswordBox) return true;
        if (source is ComboBox or ComboBoxItem) return !ctrl;
        bool navigation = key is Key.Space or Key.Enter or Key.Escape or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Tab;
        return navigation && source is ButtonBase or ListBox or ListBoxItem or Slider or ScrollBar or TabItem or TabControl or MenuItem;
    }

    void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !Editors.All(ed => ed.ConfirmDiscard());
}
