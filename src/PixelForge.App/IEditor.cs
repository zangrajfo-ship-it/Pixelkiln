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

    /// <summary>Extensions this tab opens; the first one is its own file format.</summary>
    string[] FileExtensions { get; }

    /// <summary>Opens a file (asks to save unsaved work first unless <paramref name="confirmed"/>).</summary>
    void OpenPath(string path, bool confirmed = false);

    bool IsDirty { get; }
    string? CurrentPath { get; }

    /// <summary>Called after the asset browser renamed or moved a file on disk.</summary>
    void OnFileMoved(string oldPath, string newPath);

    /// <summary>Writes the current document for crash recovery (does not change the saved state).</summary>
    void WriteAutosave(string file);

    /// <summary>Loads a recovered document as unsaved work.</summary>
    void RestoreAutosave(string file, string? originalPath);
}
