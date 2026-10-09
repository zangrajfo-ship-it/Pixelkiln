using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
        Ui.NameSymbolButtons(this);
        Closed += (_, _) => AudioOut.DisposeShared();
    }

    /// <summary>
    /// Editor shortcuts run only when they don't take a key away from the focused control:
    /// text fields keep every key, combo boxes keep plain keys, buttons/lists/sliders keep
    /// Space, Enter and the navigation keys (WCAG 2.1.1).
    /// </summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ControlOwnsKey(e.OriginalSource, e.Key)) return;
        if (Tabs.SelectedContent == SpriteEd) SpriteEd.HandleKey(e);
        else if (Tabs.SelectedContent == MapEd) MapEd.HandleKey(e);
        else if (Tabs.SelectedContent == MusicEd) MusicEd.HandleKey(e);
        else if (Tabs.SelectedContent == SfxEd) SfxEd.HandleKey(e);
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

    void OnClosing(object? sender, CancelEventArgs e) =>
        e.Cancel = !SpriteEd.ConfirmDiscard() || !MapEd.ConfirmDiscard() || !MusicEd.ConfirmDiscard() || !SfxEd.ConfirmDiscard();
}
