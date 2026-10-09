using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;

namespace PixelForge.App;

public partial class SfxEditor : IEditor
{
    IReadOnlyList<MenuItem>? _menus;

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_Open…", "Ctrl+O", () => OnOpen(null, null)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", () => OnSave(null, null)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("Export _WAV…", null, () => OnExportWav(this, new RoutedEventArgs())),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Sound",
            [.. Sfx.PresetNames.Select(n => (object)Ui.Item($"New _{n}", null, () => SetSfx(Sfx.Preset(n, _rnd)))),
             new Separator(),
             Ui.Item("_Mutate", null, () => SetSfx(_sfx.Mutate(_rnd))),
             Ui.Item("_Play", "Space", Play)]),
    ];
}
