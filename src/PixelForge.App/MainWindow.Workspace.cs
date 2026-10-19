using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Copy/paste style between documents and objects, and workspace profiles (tab, mode, panels, tool).</summary>
public partial class MainWindow
{
    static object? _styleClipboard;

    /// <summary>The editor that has the keyboard: inside a tab with modes, the active mode.</summary>
    IEditor? ActiveEditor => Current is ModeHost h ? h.Active : Current;

    void CopyStyle()
    {
        if (ActiveEditor is not IStyleSource s) { Ui.Error(new InvalidOperationException("This editor has no style to copy.")); return; }
        if (s.CopyStyle() is not { } style) { Ui.Error(new InvalidOperationException("Select something first.")); return; }
        _styleClipboard = style;
        Announce("Style copied.");
    }

    void PasteStyle()
    {
        if (_styleClipboard is null) { Ui.Error(new InvalidOperationException("Copy a style first (Ctrl+Alt+C).")); return; }
        if (ActiveEditor is not IStyleSource s || s.PasteStyle(_styleClipboard) is not string done)
        {
            Ui.Error(new InvalidOperationException("The copied style does not fit here (copy and paste between the same kind of thing)."));
            return;
        }
        Announce(done);
    }

    void Announce(string text) => System.Windows.Automation.AutomationProperties.SetItemStatus(Tabs, text);

    // ---------- workspaces ----------

    static readonly WorkspaceProfile[] BuiltInWorkspaces =
    [
        new("Pixel art", "Sprite", "Pixel", AssetBrowser: true, Tool: "Pencil"),
        new("Painting", "Sprite", "Paint", AssetBrowser: false),
        new("Animation", "Character", "States", AssetBrowser: false),
        new("Levels", "Scene", "Layout", AssetBrowser: true),
    ];

    MenuItem WorkspaceMenu()
    {
        var menu = new MenuItem { Header = "_Workspace" };
        foreach (var w in BuiltInWorkspaces.Concat(App.Settings.Workspaces))
            menu.Items.Add(Ui.Item(w.Name.Replace("_", "__"), null, () => ApplyWorkspace(w)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("_Save current as workspace…", null, SaveWorkspace));
        var delete = new MenuItem { Header = "_Delete workspace", IsEnabled = App.Settings.Workspaces.Count > 0 };
        foreach (var w in App.Settings.Workspaces) delete.Items.Add(Ui.Item(w.Name.Replace("_", "__"), null, () => DeleteWorkspace(w)));
        menu.Items.Add(delete);
        return menu;
    }

    void ApplyWorkspace(WorkspaceProfile w)
    {
        if (Tabs.Items.Cast<TabItem>().FirstOrDefault(t => (string)t.Header == w.Tab) is not { } tab) return;
        Tabs.SelectedItem = tab;
        if (tab.Content is ModeHost host)
        {
            int mode = host.Modes.ToList().FindIndex(m => m.Name == w.Mode);
            host.Activate(Math.Max(0, mode));   // a mode this version does not have yet: the first one
        }
        SetAssetsVisible(w.AssetBrowser);
        if (w.Tool is not null && ActiveEditor is IToolSelectable t) t.SelectTool(w.Tool);
        Announce($"Workspace “{w.Name}”.");
    }

    void SaveWorkspace()
    {
        if (Ui.Ask(this, "Save workspace", "Name:", "My workspace") is not { Length: > 0 } name) return;
        name = name.Trim();
        if (BuiltInWorkspaces.Any(b => b.Name == name)) { Ui.Error(new InvalidOperationException("That name belongs to a built-in workspace.")); return; }
        var tab = (TabItem)Tabs.SelectedItem;
        var w = new WorkspaceProfile(name, (string)tab.Header, (tab.Content as ModeHost)?.Modes.First(m => m.Editor == ActiveEditor).Name,
            AssetsVisible, (ActiveEditor as IToolSelectable)?.CurrentToolName);
        var s = App.Settings;
        s.Workspaces.RemoveAll(x => x.Name == name);
        s.Workspaces.Add(w);
        App.UpdateSettings(s);
        BuildMenu();
    }

    void DeleteWorkspace(WorkspaceProfile w)
    {
        var s = App.Settings;
        s.Workspaces.Remove(w);
        App.UpdateSettings(s);
        BuildMenu();
    }

    /// <summary>Ctrl+Alt+C / Ctrl+Alt+V (with Alt, WPF reports the letter as SystemKey).</summary>
    bool HandleStyleKeys(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt)) return false;
        if (key == Key.C) CopyStyle();
        else if (key == Key.V) PasteStyle();
        else return false;
        e.Handled = true;
        return true;
    }
}
