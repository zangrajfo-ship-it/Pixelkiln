using System.Windows;
using System.Windows.Controls;

namespace PixelForge.App;

public partial class MusicEditor : IEditor
{
    IReadOnlyList<MenuItem>? _menus;

    Action Do(RoutedEventHandler handler) => () => handler(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New song…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("Export _WAV…", null, Do(OnExportWav)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo))),
        Ui.Menu("_Pattern",
            Ui.Item("_New pattern", null, Do(OnAddPattern)),
            Ui.Item("_Duplicate pattern", null, Do(OnDuplicatePattern)),
            Ui.Item("De_lete pattern", null, Do(OnDeletePattern))),
        Ui.Menu("P_lay",
            Ui.Item("Play _song", "Space", Do(OnPlaySong)),
            Ui.Item("Play _pattern", null, Do(OnPlayPattern)),
            Ui.Item("S_top", "Space", Do(OnStop))),
        Ui.Menu("_Channel",
            Ui.Item("Pulse _1", "1", () => SelectChannel(0)),
            Ui.Item("Pulse _2", "2", () => SelectChannel(1)),
            Ui.Item("_Triangle (bass)", "3", () => SelectChannel(2)),
            Ui.Item("_Noise (drums)", "4", () => SelectChannel(3))),
    ];
}
