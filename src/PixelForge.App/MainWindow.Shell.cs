using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace PixelForge.App;

/// <summary>The window frame of the redesign: mode tabs, document chip, undo/redo and export in the top bar, a shared status bar.</summary>
public partial class MainWindow
{
    void BuildShell()
    {
        foreach (TabItem tab in Tabs.Items)
        {
            string name = (string)tab.Header;
            if (!Shown.Contains(name)) continue;   // simplified app: the other tabs stay in the code, switched off
            var b = new RadioButton { Content = name, GroupName = "modes", Style = (Style)FindResource("ModeTab"), Tag = tab };
            AutomationProperties.SetName(b, name);
            b.Checked += (_, _) => { if (Tabs.SelectedItem != tab) Tabs.SelectedItem = tab; };
            ModeTabs.Children.Add(b);
        }
        SyncModeTabs();
        // editors write "PixelForge — name *" into the title; the chip shows the same
        DependencyPropertyDescriptor.FromProperty(TitleProperty, typeof(Window)).AddValueChanged(this, (_, _) => UpdateDocChip());
        UpdateDocChip();
    }

    /// <summary>The tabs of the simplified app: make art, animate it, build tilemaps. Add a name to bring a tab back.</summary>
    static readonly string[] Shown = ["Sprite", "Character", "Tilemap"];   // "Scene", "Audio", "Music", "SFX"

    void SyncModeTabs()
    {
        foreach (RadioButton b in ModeTabs.Children) b.IsChecked = b.Tag == Tabs.SelectedItem;
    }

    void UpdateDocChip()
    {
        string t = Title ?? "";
        int dash = t.IndexOf(" — ", StringComparison.Ordinal);
        string doc = dash >= 0 ? t[(dash + 3)..] : "";
        bool dirty = doc.EndsWith(" *", StringComparison.Ordinal);
        if (dirty) doc = doc[..^2];
        DocName.Text = doc.Length > 0 ? doc : "untitled";
        DirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(DocChip, $"Document {DocName.Text}{(dirty ? ", unsaved changes" : "")}");
    }

    /// <summary>The active editor's own status line moves into the window's status bar.</summary>
    void MirrorStatus()
    {
        var ed = Current is ModeHost h ? h.Active : Current;
        if (ed is FrameworkElement fe && fe.FindName("Status") is TextBlock status)
        {
            if (status.Parent is FrameworkElement bar && bar != fe) bar.Visibility = Visibility.Collapsed;
            MainStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = status });
        }
        else
        {
            BindingOperations.ClearBinding(MainStatus, TextBlock.TextProperty);
            MainStatus.Text = "";
        }
    }

    void ShowAutosaved()
    {
        SavedDot.Visibility = Visibility.Visible;
        SavedText.Text = $"Autosaved {DateTime.Now:HH:mm}";
    }

    /// <summary>Runs a command of the current editor's menus by its header (Undo, Redo …).</summary>
    void InvokeMenu(string header)
    {
        MenuItem? Find(IEnumerable<object> items) =>
            items.OfType<MenuItem>().Select(m => (string)m.Header == header ? m : Find(m.Items.Cast<object>())).FirstOrDefault(m => m is not null);
        if (Current is { } ed && Find(ed.Menus) is { IsEnabled: true } item) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    void OnUndo(object sender, RoutedEventArgs e) => InvokeMenu("_Undo");
    void OnRedo(object sender, RoutedEventArgs e) => InvokeMenu("_Redo");
    void OnGettingStarted(object sender, RoutedEventArgs e) => GettingStarted.Show(this, OpenSampleAsset);
    void OnLogo(object sender, RoutedEventArgs e) => GettingStarted.Show(this, OpenSampleAsset);

    void OnExport(object sender, RoutedEventArgs e) => ShowExport();

    void ShowExport()
    {
        if (App.Project is not { } p)
        {
            MessageBox.Show(this, "Export works on a project. Open or create one in the Project menu (or open the sample game from Help).", "Export");
            return;
        }
        ExportWindow.Show(this, p, [.. Editors.Where(ed => ed.IsDirty).Select(ed => ed.CurrentPath ?? "untitled")]);
    }
}
