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
    IEditor[] Editors => [SpriteEd, MapEd, MusicEd, SfxEd];
    IEditor? Current => (Tabs?.SelectedItem as TabItem)?.Content as IEditor;   // SelectedContent lags behind SelectionChanged

    readonly DispatcherTimer _autosave = new();

    public MainWindow()
    {
        InitializeComponent();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
        Ui.NameSymbolButtons(this);
        _autosave.Tick += (_, _) => Autosave();
        App.SettingsChanged += ApplySettings;
        ApplySettings();
        Loaded += (_, _) =>
        {
            BuildMenu();   // the TabControl has no selection before it is loaded
            OfferRecovery();
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
        MainMenu.Items.Add(Ui.Menu("_Options",
            Ui.Item("_Preferences…", "Ctrl+,", () => PreferencesWindow.Show(this))));
        MainMenu.Items.Add(Ui.Menu("_Help",
            Ui.Item("_Keyboard shortcuts", "F1", () => HelpWindow.Show(this)),
            Ui.Item("_About PixelForge", null, () => MessageBox.Show(this,
                "PixelForge — sprites, tilemaps, music and sound effects for your games.", "About PixelForge"))));
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
        string names = string.Join(", ", found.Select(i => ((TabItem)Tabs.Items[i]).Header));
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
