using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>
/// Template Studio (Ctrl+T): find a template (built-in, project, user), set its parameters, make it as a starting point
/// or a full kit, save selected assets as a new template, and move templates between projects as .pxtplpack files.
/// </summary>
static class TemplateStudio
{
    static readonly Dictionary<string, ImageSource?> Previews = [];

    public static void Show(Window owner, Project p, IReadOnlyList<AssetEntry> selection, Action<AssetEntry> open)
    {
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(search, "Search templates");
        var kind = new ComboBox { ItemsSource = new[] { "All kinds" }.Concat(Enum.GetNames<TemplateKind>()).ToList(), SelectedIndex = 0, Margin = new Thickness(0, 0, 4, 0), Width = 120 };
        var source = new ComboBox { ItemsSource = new[] { "All sources", "BuiltIn", "Project", "User" }, SelectedIndex = 0, Width = 120 };
        AutomationProperties.SetName(kind, "Template kind");
        AutomationProperties.SetName(source, "Template source");
        var list = new ListBox { Width = 300 };
        AutomationProperties.SetName(list, "Templates");
        var left = new DockPanel { Width = 300, Margin = new Thickness(0, 0, 12, 0) };
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { kind, source } };
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(filters, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(filters);
        left.Children.Add(list);

        var preview = new Image { Height = 160, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(preview, "Template preview");
        var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        info.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
        var version = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(version, "Template version");
        var form = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        var start = new RadioButton { Content = "Starting point (the main asset and what it needs)", IsChecked = true, GroupName = "tplmode" };
        var kit = new RadioButton { Content = "Full kit (everything in the template)", GroupName = "tplmode" };
        var folder = new TextBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        var create = new Button { Content = "Create", IsDefault = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { new Label { Content = "_Folder in the project", Target = folder, Padding = new Thickness(0, 4, 8, 4) }, folder } };
        var right = new StackPanel
        {
            Children = { preview, title, info, new StackPanel { Orientation = Orientation.Horizontal, Children = { new Label { Content = "_Version", Target = version, Padding = new Thickness(0, 4, 8, 4) }, version } },
                form, start, kit, folderRow, create, status },
        };

        var saveAs = new Button { Content = "Save selected assets as template…", Margin = new Thickness(0, 0, 8, 0), IsEnabled = selection.Count > 0,
            ToolTip = selection.Count > 0 ? $"{selection.Count} asset(s) selected in the asset browser" : "Select assets in the asset browser first" };
        var import = new Button { Content = "Import pack…", Margin = new Thickness(0, 0, 8, 0) };
        var export = new Button { Content = "Export pack…", Margin = new Thickness(0, 0, 8, 0) };
        var delete = new Button { Content = "Delete template", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { saveAs, import, export, delete, close } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(left, Dock.Left);
        var win = new Window
        {
            Title = "Template Studio", Width = 900, Height = 700, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { bottom, left, new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } },
        };

        List<TemplateInfo> all = [];
        var inputs = new Dictionary<string, Control>();

        void Load()
        {
            var (loaded, rejected) = TemplateLibrary.All(p);
            all = loaded;
            status.Text = rejected.Count == 0 ? "" : "Not loaded: " + string.Join("; ", rejected.Select(r => $"{System.IO.Path.GetFileName(r.Folder)} ({r.Reason})"));
            Fill();
        }

        TemplateInfo? Selected() => list.SelectedItem is ListBoxItem { Tag: string id }
            ? all.Where(t => t.Manifest.Id == id && (version.SelectedItem is not string v || t.Manifest.Version == v)).OrderByDescending(t => Version.Parse(t.Manifest.Version)).FirstOrDefault()
            : null;

        void Fill()
        {
            string q = search.Text.Trim();
            var shown = all.Where(t => kind.SelectedIndex == 0 || t.Manifest.Kind.ToString() == (string)kind.SelectedItem)
                .Where(t => source.SelectedIndex == 0 || t.Source.ToString() == (string)source.SelectedItem)
                .Where(t => q.Length == 0 || $"{t.Manifest.Name} {t.Manifest.Description} {string.Join(" ", t.Manifest.Tags ?? [])}".Contains(q, StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.Manifest.Id).Select(g => g.OrderByDescending(t => Version.Parse(t.Manifest.Version)).First())
                .OrderBy(t => t.Manifest.Kind).ThenBy(t => t.Manifest.Name).ToList();
            list.ItemsSource = shown.Select(t =>
            {
                var item = new ListBoxItem { Tag = t.Manifest.Id, Content = new StackPanel { Children = { new TextBlock { Text = t.Manifest.Name, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = $"{t.Manifest.Kind} · {t.Source}", FontSize = 11 } } } };
                AutomationProperties.SetName(item, $"{t.Manifest.Name}, {t.Manifest.Kind}, {t.Source}");
                return item;
            }).ToList();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void ShowSelected(bool keepVersion = false)
        {
            if (list.SelectedItem is not ListBoxItem { Tag: string id }) { right.Visibility = Visibility.Collapsed; return; }
            right.Visibility = Visibility.Visible;
            if (!keepVersion)
            {
                version.ItemsSource = all.Where(t => t.Manifest.Id == id).Select(t => t.Manifest.Version).OrderByDescending(Version.Parse).ToList();
                version.SelectedIndex = 0;
            }
            if (Selected() is not { } t) return;
            var m = t.Manifest;
            title.Text = m.Name;
            info.Text = $"{m.Kind} · {t.Source} · version {m.Version}{(m.Tags is { Count: > 0 } tags ? " · " + string.Join(", ", tags) : "")}\n{m.Description}";
            delete.IsEnabled = t.Source != TemplateSource.BuiltIn;
            folder.Text = m.Kind switch { TemplateKind.Character => "characters", TemplateKind.Level => "levels", TemplateKind.Music => "music", _ => "assets" };
            kit.IsEnabled = TemplateEngine.Plan(m, true).Count > TemplateEngine.Plan(m, false).Count;
            if (!kit.IsEnabled) start.IsChecked = true;

            form.Children.Clear();
            form.RowDefinitions.Clear();
            inputs.Clear();
            foreach (var (prm, row) in (m.Parameters ?? []).Select((x, i) => (x, i)))
            {
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Control input;
                if (prm.Source is AssetType type)
                {
                    var choices = new List<string> { prm.Default };
                    choices.AddRange(p.Assets.Where(a => a.Type == type || type == AssetType.Image && a.Type == AssetType.Sprite).Select(a => a.Name).Distinct());
                    input = new ComboBox { ItemsSource = choices, SelectedIndex = 0 };
                }
                else if (prm.Options is { } opts) input = new ComboBox { ItemsSource = opts, SelectedItem = prm.Default, IsEditable = true, Text = prm.Default };
                else input = new TextBox { Text = prm.Default };
                input.Margin = new Thickness(0, 2, 0, 2);
                var label = new Label { Content = prm.Label, Target = input, Padding = new Thickness(0, 4, 10, 4) };
                Grid.SetRow(label, row);
                Grid.SetRow(input, row);
                Grid.SetColumn(input, 1);
                form.Children.Add(label);
                form.Children.Add(input);
                inputs[prm.Key] = input;
            }
            ShowPreview(t);
        }

        async void ShowPreview(TemplateInfo t)
        {
            if (Previews.TryGetValue(t.Key, out var cached)) { preview.Source = cached; return; }
            preview.Source = null;
            var img = await Task.Run(() => MakePreview(t));
            Previews[t.Key] = img;
            if (Selected()?.Key == t.Key) preview.Source = img;
        }

        Dictionary<string, string> Values() => inputs.ToDictionary(kv => kv.Key, kv => kv.Value switch
        {
            TextBox tb => tb.Text.Trim(),
            ComboBox cb => (cb.IsEditable ? cb.Text : cb.SelectedItem as string ?? "").Trim(),
            _ => "",
        });

        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        source.SelectionChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) => ShowSelected();
        version.SelectionChanged += (_, _) => { if (version.IsDropDownOpen || version.IsKeyboardFocusWithin) ShowSelected(keepVersion: true); };

        create.Click += async (_, _) =>
        {
            if (Selected() is not { } t) return;
            var values = Values();
            bool full = kit.IsChecked == true;
            string dir = folder.Text.Trim();
            try
            {
                var r = await Ui.RunLong(win, "Creating from template", (progress, cancel) =>
                {
                    progress.Report($"Making “{t.Manifest.Name}”…");
                    return TemplateEngine.Create(t, values, p, dir, full);
                });
                if (r is null) return;
                App.NotifyProjectChanged();
                status.Text = $"Created {r.Created.Count} asset(s) in “{(dir.Length == 0 ? "the project folder" : dir)}”: " + string.Join(", ", r.Created.Select(a => a.Name));
                open(r.Primary);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException or FormatException) { Ui.Error(ex); }
        };

        saveAs.Click += (_, _) =>
        {
            var first = selection[0];
            if (Ui.AskFields(win, "Save as template",
                    ("_Name", first.Name, null), ("_Id (lowercase, no spaces)", Slug(first.Name), null), ("_Version", "1.0", null),
                    ("_Kind", nameof(TemplateKind.Pack), Enum.GetNames<TemplateKind>()), ("_Tags (commas)", "", null),
                    ("_Description", "", null), ("_Available in", "This project", ["This project", "All my projects"])) is not { } f) return;
            bool user = f[6] == "All my projects";
            try
            {
                var t = TemplateLibrary.SaveFromAssets(p, [.. selection.Select(a => a.Id)], user ? TemplateLibrary.UserFolder : TemplateLibrary.ProjectFolder(p),
                    user ? TemplateSource.User : TemplateSource.Project, f[1].Trim(), f[0].Trim(), f[2].Trim(), Enum.Parse<TemplateKind>(f[3]),
                    f[5].Trim().Length == 0 ? null : f[5].Trim(), [.. f[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]);
                status.Text = $"Saved “{t.Manifest.Name}” {t.Manifest.Version} with {t.Manifest.Steps.Count} file(s) (used assets are included).";
                Load();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
        };

        import.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Template pack (*.pxtplpack)|*.pxtplpack" };
            if (dlg.ShowDialog(win) != true) return;
            if (Ui.AskFields(win, "Import templates", ("_Install for", "This project", ["This project", "All my projects"])) is not { } f) return;
            bool user = f[0] == "All my projects";
            try
            {
                var got = TemplateLibrary.ImportPack(dlg.FileName, user ? TemplateLibrary.UserFolder : TemplateLibrary.ProjectFolder(p), user ? TemplateSource.User : TemplateSource.Project);
                status.Text = "Imported: " + string.Join(", ", got.Select(t => $"{t.Manifest.Name} {t.Manifest.Version}"));
                Load();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };

        export.Click += (_, _) =>
        {
            if (Selected() is not { } t) return;
            var dlg = new SaveFileDialog { Filter = "Template pack (*.pxtplpack)|*.pxtplpack", FileName = t.Manifest.Id + ".pxtplpack" };
            if (dlg.ShowDialog(win) != true) return;
            try
            {
                TemplateLibrary.ExportPack(dlg.FileName, [t]);
                status.Text = $"Exported “{t.Manifest.Name}” {t.Manifest.Version}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };

        delete.Click += (_, _) =>
        {
            if (Selected() is not { Folder: string dir } t || t.Source == TemplateSource.BuiltIn) return;
            if (MessageBox.Show(win, $"Delete “{t.Manifest.Name}” {t.Manifest.Version}? It is moved to a .trash folder next to it, not erased.", "Delete template",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try
            {
                string trash = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dir)!, ".trash");
                Directory.CreateDirectory(trash);
                string dest = System.IO.Path.Combine(trash, System.IO.Path.GetFileName(dir) + "-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.Move(dir, dest);
                Load();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };

        Load();
        win.ShowDialog();
    }

    static string Slug(string name)
    {
        var s = new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        return s.Length >= 2 ? s : "my-template";
    }

    /// <summary>Makes the template with its defaults in a throwaway project and pictures the main asset.</summary>
    static ImageSource? MakePreview(TemplateInfo t)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pf-tplpreview-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var p = Project.Create(root, "preview");
            var r = TemplateEngine.Create(t, null, p, "", fullKit: true);
            if (AssetKinds.Of(r.Primary.Type).Picture?.Invoke(p.FullPath(r.Primary), p) is not var (px, w, h)) return null;
            var (tp, tw, th) = AssetInfo.Thumbnail(px, w, h, 480);
            var bmp = ImageIO.ToBitmap(tp, tw, th);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException or FormatException) { return null; }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (IOException) { }   // a locked temp file is cleaned up by the OS later
        }
    }
}
