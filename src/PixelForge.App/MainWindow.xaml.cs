using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    IEnumerable<IEditor> Editors => [SpriteEd, MapEd, MusicEd, SfxEd];
    IEditor? Current => (Tabs?.SelectedItem as TabItem)?.Content as IEditor;   // SelectedContent lags behind SelectionChanged

    public MainWindow()
    {
        InitializeComponent();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
        Ui.NameSymbolButtons(this);
        Closed += (_, _) => AudioOut.DisposeShared();
        Loaded += (_, _) => BuildMenu();   // the TabControl has no selection before it is loaded
    }

    // ---------- menu: the current tab's File/Edit/View + Help ----------

    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs) BuildMenu();
    }

    void BuildMenu()
    {
        if (MainMenu is null || Current is not { } ed) return;
        MainMenu.Items.Clear();
        foreach (var m in ed.Menus) MainMenu.Items.Add(m);
        MainMenu.Items.Add(Ui.Menu("_Help",
            Ui.Item("_Keyboard shortcuts", "F1", () => HelpWindow.Show(this)),
            Ui.Item("_About PixelForge", null, () => MessageBox.Show(this,
                "PixelForge — sprites, tilemaps, music and sound effects for your games.", "About PixelForge"))));
    }

    // ---------- keyboard ----------

    /// <summary>
    /// Editor shortcuts run only when they don't take a key away from the focused control:
    /// text fields keep every key, combo boxes keep plain keys, buttons/lists/sliders keep
    /// Space, Enter and the navigation keys (WCAG 2.1.1).
    /// </summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { HelpWindow.Show(this); e.Handled = true; return; }
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
