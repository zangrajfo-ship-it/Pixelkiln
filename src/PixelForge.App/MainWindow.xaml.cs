using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PixelForge.Core;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    // order = autosave file index; new editors are appended, whatever their tab position
    IEditor[] Editors => [SpriteEd, MapEd, MusicEd, SfxEd, CharEd, SceneEd, AudioEd, PaintEd, VectorEd, TextureEd, UiEd, MotionEd, LogicEd];

    // modes added in code (they live inside a tab's ModeHost, not in the XAML)
    readonly PaintEditor PaintEd = new();
    readonly VectorEditor VectorEd = new();
    readonly TextureEditor TextureEd = new();
    readonly UiEditor UiEd = new();
    readonly MotionEditor MotionEd = new();
    readonly LogicEditor LogicEd = new();
    // RigEditor? RigEd;   // a view of the States mode's document, so not in Editors (simplified app: switched off)

    TabItem TabOf(IEditor ed) => Tabs.Items.Cast<TabItem>().First(t => Holds(t, ed));
    static bool Holds(TabItem t, IEditor ed) => t.Content == ed || t.Content is ModeHost h && h.Contains(ed);

    /// <summary>The editor is in a tab of the simplified app (and, in a mode tab, is one of its modes).</summary>
    bool IsShown(IEditor ed) => Tabs.Items.Cast<TabItem>().Any(t => Holds(t, ed) && Shown.Contains((string)t.Header));

    /// <summary>The sample game opens with this file.</summary>
    const string SampleStart = "art/hero.pxf";

    bool SpriteShows(string path) => string.Equals(SpriteEd.CurrentPath, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows the tab and, inside it, the mode that holds the editor.</summary>
    void Reveal(IEditor ed)
    {
        var tab = TabOf(ed);
        Tabs.SelectedItem = tab;
        (tab.Content as ModeHost)?.Activate(ed);
    }

    /// <summary>Tabs whose editors are modes: new modes are added here (or later with host.Add), never as new tabs.</summary>
    void HostModes()
    {
        foreach (var (ed, name) in new (UserControl Editor, string Name)[] { (SpriteEd, "Pixel"), (CharEd, "States"), (SceneEd, "Layout") })
        {
            var tab = TabOf((IEditor)ed);
            tab.Content = null;   // detach before re-parenting
            var host = new ModeHost((name, (IEditor)ed));
            host.ModeChanged += () => { BuildMenu(); MirrorStatus(); };
            tab.Content = host;
        }
        // Simplified app (21. 10. 2026): only art (Sprite › Pixel), animation from art (Character › States) and tilemaps.
        // The other modes are switched off here, not deleted; remove the // to bring one back.
        // HostOf(SpriteEd).Add("Paint", PaintEd);
        // HostOf(SpriteEd).Add("Vector", VectorEd);
        // HostOf(SpriteEd).Add("Texture", TextureEd);
        // HostOf(SpriteEd).Add("UI", UiEd);
        // HostOf(CharEd).Add("Rig", RigEd = new RigEditor(CharEd));
        // HostOf(SceneEd).Add("Motion", MotionEd);
        // HostOf(SceneEd).Add("Logic", LogicEd);
        TextureEd.OpenPainting = a => { OpenAsset(a); PaintEd.SetWrap(true); };
        PaintEd.SendToPixel = VectorEd.SendToPixel = (px, w, h) => { Reveal(SpriteEd); SpriteEd.LoadPixels(px, w, h); };
    }

    ModeHost HostOf(IEditor ed) => (ModeHost)TabOf(ed).Content;
    IEditor? Current => (Tabs?.SelectedItem as TabItem)?.Content as IEditor;   // SelectedContent lags behind SelectionChanged

    readonly DispatcherTimer _autosave = new();

    public MainWindow()
    {
        InitializeComponent();
        HostModes();
        BuildShell();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
        CharEd.IsSpriteOpenDirty = path => SpriteEd.IsDirty && SpriteShows(path);
        CharEd.SpriteFileChanged = path =>
        {
            if (SpriteShows(path) && !SpriteEd.IsDirty) SpriteEd.OpenPath(path, confirmed: true);
        };
        Ui.NameSymbolButtons(this);
        _autosave.Tick += (_, _) => Autosave();
        Assets.OpenRequested += OpenAsset;
        Assets.FileMoved += (from, to) => { foreach (var ed in Editors) ed.OnFileMoved(from, to); };
        App.ProjectChanged += UpdateProjectTitle;
        App.AssetRestored += ReloadRestored;
        App.SettingsChanged += ApplySettings;
        ApplySettings();
        // fill the menu before the first layout pass; an empty Fluent Menu filled later can stay zero-sized
        Tabs.SelectedIndex = 0;
        BuildMenu();
        Loaded += (_, _) =>
        {
            OfferRecovery();
            ReopenLastProject();
            MirrorStatus();
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
        if (e.OriginalSource != Tabs) return;
        BuildMenu();
        SyncModeTabs();
        MirrorStatus();
    }

    void BuildMenu()
    {
        if (MainMenu is null || Current is not { } ed) return;
        MainMenu.Items.Clear();
        foreach (var m in ed.Menus) MainMenu.Items.Add(m);
        MainMenu.Items.Add(ProjectMenu());
        MainMenu.Items.Add(Ui.Menu("_Options",
            Ui.Item("_Command palette…", "Ctrl+Shift+P", ShowCommandPalette),
            // Ui.Item("_Device preview…", "Ctrl+Shift+D", ShowDevicePreview),   // simplified app
            Ui.Item("P_lugins…", null, () => PluginsWindow.Show(this)),
            // WorkspaceMenu(),   // simplified app: the profiles open tabs that are switched off
            Ui.Item("Copy st_yle", "Ctrl+Alt+C", CopyStyle),
            Ui.Item("Paste s_tyle", "Ctrl+Alt+V", PasteStyle),
            Ui.Item("_Preferences…", "Ctrl+,", () => PreferencesWindow.Show(this))));
        MainMenu.Items.Add(Ui.Menu("_Help",
            Ui.Item("_Getting started…", null, () => GettingStarted.Show(this, OpenSampleAsset)),
            Ui.Item("Open _sample game…", null, () => OpenSampleAsset(SampleStart)),
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
            Ui.Item("Open _sample game…", null, () => OpenSampleAsset(SampleStart)),
            Ui.Item("_Close project", null, ProjectCommands.Close, open),
            new Separator(),
            Ui.Item("New from _template…", "Ctrl+T", ShowTemplateStudio, open),
            Ui.Item("C_heck project…", null, () => { if (App.Project is { } p) HealthWindow.Show(this, p); }, open),
            // simplified app:
            // Ui.Item("Optimi_ze assets…", null, () => { if (App.Project is { } p) OptimizeWindow.Show(this, p, App.NotifyProjectChanged); }, open),
            // Ui.Item("St_yle guide…", null, () => { if (App.Project is { } p) StyleGuideWindow.Show(this, p); }, open),
            Ui.Item("Project _settings and collisions…", null, () => { if (App.Project is { } p) ProjectSettingsWindow.Show(this, p); }, open),
            Ui.Item("_History of selected asset…", "Ctrl+H", Assets.ShowHistory, open),
            Ui.Item("_Export…", "Ctrl+Shift+E", ShowExport, open),
            Ui.Item("_Palette studio…", null, () =>
            {
                if (App.Project is { } p)
                    PaletteStudio.Show(this, p,
                        canTouch: path => !(SpriteEd.IsDirty && SpriteShows(path)),
                        changed: path => { if (SpriteShows(path)) SpriteEd.OpenPath(path, confirmed: true); });
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
        if (App.Settings.LastProject is string path && File.Exists(path)) { ProjectCommands.OpenPath(path); return; }
        if (App.Settings.LastProject is not null) App.SetProject(null);   // forget a project that is gone
        ShowHome();   // nothing to continue: start here
    }

    /// <summary>"PixelForge.App.exe game.pfproj" opens it; a folder opens its project or becomes one; a sprite, character or map file opens in its tab.</summary>
    void OpenFromCommandLine(string arg)
    {
        try
        {
            if (File.Exists(arg) && arg.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase)) { ProjectCommands.OpenPath(arg); return; }
            if (File.Exists(arg) && OpenDocument(arg)) return;
            if (!Directory.Exists(arg)) throw new FileNotFoundException($"“{arg}” is neither a project nor a folder.");
            var existing = Directory.GetFiles(arg, "*" + Project.Extension).FirstOrDefault();
            if (existing is not null) ProjectCommands.OpenPath(existing);
            else App.SetProject(Project.CreateFromFolder(arg, Path.GetFileName(Path.GetFullPath(arg).TrimEnd(Path.DirectorySeparatorChar))));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    /// <summary>
    /// A restored version replaces the file on disk; the tab showing it reloads. With unsaved edits it asks first,
    /// because saving them would overwrite the restored file (both stay in the history either way).
    /// </summary>
    void ReloadRestored(string path)
    {
        foreach (var ed in Editors.Where(ed => string.Equals(ed.CurrentPath, path, StringComparison.OrdinalIgnoreCase)))
            if (!ed.IsDirty || MessageBox.Show(this, $"“{Path.GetFileName(path)}” has unsaved changes. Discard them and show the restored version?",
                    "Version restored", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                ed.OpenPath(path, confirmed: true);
    }

    /// <summary>The picture of the active editor (sprite frame, painting, drawing, texture, UI screen) on a chosen device.</summary>
    void ShowDevicePreview()
    {
        var pic = ActiveEditor switch
        {
            IPictureSource s => s.CurrentPicture,
            SpriteEditor sp => sp.CurrentImage,
            _ => null,
        };
        if (pic is not { } p) { Ui.Error(new InvalidOperationException("Open a sprite, painting, drawing, texture or UI screen first.")); return; }
        DevicePreview.Show(this, p, Title.Replace("PixelForge — ", ""));
    }

    void ShowTemplateStudio()
    {
        if (App.Project is { } p) TemplateStudio.Show(this, p, Assets.SelectedAll, OpenAsset);
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
            AssetType.Paint => PaintEd,
            AssetType.Vector => VectorEd,
            AssetType.Texture => TextureEd,
            AssetType.Ui => UiEd,
            AssetType.Anim => MotionEd,
            AssetType.Logic => LogicEd,
            _ => null,
        };
        if (a.Type == AssetType.Audio)   // a WAV is material: it goes onto a track
        {
            Reveal(AudioEd);
            AudioEd.AddClip(a.Id);
            return;
        }
        if (a.Type == AssetType.Palette)
        {
            Reveal(SpriteEd);
            SpriteEd.ApplyPaletteFile(path);
            return;
        }
        if (target is null) { Ui.Error(new NotSupportedException($"{a.Type} assets open in their own studio.")); return; }
        if (!IsShown(target))
        {
            MessageBox.Show(this, $"“{a.Path}” is a {a.Type} asset. This version only edits sprites, characters and tilemaps.", "PixelForge");
            return;
        }
        Reveal(target);
        target.OpenPath(path);
    }

    /// <summary>Opens (or creates) the sample game and shows one of its files.</summary>
    void OpenSampleAsset(string relPath)
    {
        if (ProjectCommands.OpenSample(this) is not { } p) return;
        if (p.FindByPath(Path.Combine(p.Root, relPath)) is { } a) OpenAsset(a);
        else Ui.Error(new FileNotFoundException($"The sample game has no “{relPath}”. Make a fresh copy with Help › Open sample game in another folder."));
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
            ShowAutosaved();
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
        if (Home.IsVisible) return;   // the start screen handles its own keys; editor shortcuts would act on a hidden editor
        if (!(e.OriginalSource is TextBoxBase) && HandleStyleKeys(e)) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.P && ctrl && shift) { ShowCommandPalette(); e.Handled = true; return; }
        if (e.Key == Key.B && ctrl && shift) { SetAssetsVisible(!AssetsVisible); e.Handled = true; return; }
        // if (e.Key == Key.D && ctrl && shift) { ShowDevicePreview(); e.Handled = true; return; }   // simplified app
        if (e.Key == Key.H && ctrl && !shift && App.Project is not null) { Assets.ShowHistory(); e.Handled = true; return; }
        if (e.Key == Key.T && ctrl && !shift && App.Project is not null) { ShowTemplateStudio(); e.Handled = true; return; }
        if (e.Key == Key.E && ctrl && shift && App.Project is not null) { ShowExport(); e.Handled = true; return; }
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
