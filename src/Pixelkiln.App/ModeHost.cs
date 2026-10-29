using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Pixelkiln.App;

/// <summary>
/// Several editing modes in one tab (Sprite: Pixel, Paint, Vector …). Shows a mode bar when there is more than one
/// mode and passes every IEditor call to the active mode, so the tab count stays the same as modes are added.
/// </summary>
sealed class ModeHost : UserControl, IEditor
{
    readonly List<(string Name, IEditor Editor)> _modes = [];
    readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 2) };
    readonly Grid _views = new();
    int _active;

    /// <summary>Raised when the active mode changes (the main window rebuilds the menu bar).</summary>
    public event Action? ModeChanged;

    public ModeHost(params (string Name, IEditor Editor)[] modes)
    {
        AutomationProperties.SetName(_bar, "Editing mode");
        DockPanel.SetDock(_bar, Dock.Top);
        Content = new DockPanel { Children = { _bar, _views } };
        Focusable = false;
        foreach (var m in modes) Add(m.Name, m.Editor);
        Activate(0);
    }

    public IReadOnlyList<(string Name, IEditor Editor)> Modes => _modes;
    public IEditor Active => _modes[_active].Editor;

    public void Add(string name, IEditor editor)
    {
        int index = _modes.Count;
        _modes.Add((name, editor));
        var button = new RadioButton
        {
            Content = index < 9 ? $"{name}  Ctrl+{index + 1}" : name, GroupName = "mode" + GetHashCode(), Margin = new Thickness(0, 0, 4, 0),
            Style = (Style)FindResource(typeof(ToggleButton)), Padding = new Thickness(10, 3, 10, 3),
        };
        AutomationProperties.SetName(button, $"{name} mode");
        button.Checked += (_, _) => Activate(index);
        _bar.Children.Add(button);
        var view = (UIElement)editor;
        view.Visibility = Visibility.Collapsed;
        _views.Children.Add(view);
        _bar.Visibility = _modes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool Contains(IEditor editor) => _modes.Any(m => m.Editor == editor);

    public void Activate(IEditor editor) => Activate(_modes.FindIndex(m => m.Editor == editor));

    public void Activate(int index)
    {
        if (index < 0 || index >= _modes.Count) return;
        bool changed = index != _active;
        _active = index;
        for (int i = 0; i < _modes.Count; i++) ((UIElement)_modes[i].Editor).Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        ((RadioButton)_bar.Children[index]).IsChecked = true;
        if (changed) ModeChanged?.Invoke();
    }

    // ---------- IEditor: the active mode, or the mode that owns the file type ----------

    public IReadOnlyList<MenuItem> Menus => Active.Menus;
    public string[] FileExtensions => [.. _modes.SelectMany(m => m.Editor.FileExtensions)];
    public bool IsDirty => Active.IsDirty;
    public string? CurrentPath => Active.CurrentPath;
    public bool ConfirmDiscard() => Active.ConfirmDiscard();

    public void OpenPath(string path, bool confirmed = false)
    {
        string ext = System.IO.Path.GetExtension(path);
        var owner = _modes.FirstOrDefault(m => m.Editor.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)).Editor ?? Active;
        Activate(owner);
        owner.OpenPath(path, confirmed);
    }

    public void OnFileMoved(string oldPath, string newPath) { foreach (var m in _modes) m.Editor.OnFileMoved(oldPath, newPath); }
    public void WriteAutosave(string file) => Active.WriteAutosave(file);
    public void RestoreAutosave(string file, string? originalPath) => Active.RestoreAutosave(file, originalPath);

    public void HandleKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key is >= Key.D1 and <= Key.D9 && e.Key - Key.D1 < _modes.Count && _modes.Count > 1 && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            Activate(e.Key - Key.D1);
            e.Handled = true;
            return;
        }
        Active.HandleKey(e);
    }
}
