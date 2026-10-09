using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace PixelForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MapEd.SpriteImage = () => SpriteEd.CurrentImage;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabs.SelectedContent == SpriteEd) SpriteEd.HandleKey(e);
        else if (Tabs.SelectedContent == MapEd) MapEd.HandleKey(e);
        else if (Tabs.SelectedContent == MusicEd) MusicEd.HandleKey(e);
    }

    void OnClosing(object? sender, CancelEventArgs e) =>
        e.Cancel = !SpriteEd.ConfirmDiscard() || !MapEd.ConfirmDiscard() || !MusicEd.ConfirmDiscard();
}
