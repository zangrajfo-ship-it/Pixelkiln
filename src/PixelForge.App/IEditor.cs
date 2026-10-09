using System.Windows.Controls;
using System.Windows.Input;

namespace PixelForge.App;

/// <summary>What MainWindow needs from each tab.</summary>
interface IEditor
{
    /// <summary>Editor shortcuts; called only when no control needs the key.</summary>
    void HandleKey(KeyEventArgs e);

    /// <summary>True if unsaved work may be dropped (asks the user).</summary>
    bool ConfirmDiscard();

    /// <summary>Top-level menus for this tab (File, Edit, View …), built once.</summary>
    IReadOnlyList<MenuItem> Menus { get; }
}
