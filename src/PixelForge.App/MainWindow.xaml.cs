using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabs.SelectedContent == SpriteEd) SpriteEd.HandleKey(e);
    }

    void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !SpriteEd.ConfirmDiscard();
}
