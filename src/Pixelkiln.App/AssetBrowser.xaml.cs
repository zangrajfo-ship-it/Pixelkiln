using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>All assets of the open project: search, filter, preview, open, rename, move, tag, remove, health check.</summary>
public partial class AssetBrowser : UserControl
{
    /// <summary>Double-click / Enter on an asset.</summary>
    public event Action<AssetEntry>? OpenRequested;
    /// <summary>A file was renamed or moved on disk (old full path, new full path); open editors follow it.</summary>
    public event Action<string, string>? FileMoved;

    readonly DispatcherTimer _anim = new();
    Sprite? _animSprite;
    int _animFrame;

    static Project? P => App.Project;
    AssetEntry? Selected => (List.SelectedItem as AssetItem)?.Entry;
    /// <summary>Every selected asset, in list order (Ctrl/Shift+click selects several).</summary>
    public List<AssetEntry> SelectedAll => [.. List.Items.Cast<AssetItem>().Where(List.SelectedItems.Contains).Select(i => i.Entry)];

    public AssetBrowser()
    {
        InitializeComponent();
        TypeFilter.ItemsSource = new[] { "All types" }.Concat(Enum.GetNames<AssetType>()).ToList();
        TypeFilter.SelectedIndex = 0;
        PreviewBack.Background = Ui.CheckerBrush();
        _anim.Tick += (_, _) => NextAnimFrame();
        App.ProjectChanged += Refresh;
        Refresh();
    }

    // ---------- list ----------

    public void Refresh()
    {
        bool open = P is not null;
        EmptyState.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        BrowserPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (P is not { } p) return;
        Header.Text = $"{p.Name}  ({p.Assets.Count})";
        Guid? keep = Selected?.Id;
        AssetType? type = TypeFilter.SelectedIndex > 0 ? Enum.Parse<AssetType>((string)TypeFilter.SelectedItem) : null;
        var items = p.Search(SearchBox.Text, type, FavoritesOnly.IsChecked == true).Select(a => new AssetItem(a, p.FullPath(a))).ToList();
        List.ItemsSource = items;
        List.SelectedItem = items.FirstOrDefault(i => i.Entry.Id == keep);
        if (List.SelectedItem is null) ShowDetails(null);
    }

    void OnFilterChanged(object sender, RoutedEventArgs e) => Refresh();

    void OnSelected(object sender, SelectionChangedEventArgs e) => ShowDetails(List.SelectedItem as AssetItem);

    async void ShowDetails(AssetItem? item)
    {
        StopPreview();
        DetailPanel.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null || P is not { } p) return;
        var a = item.Entry;
        DetailName.Text = a.Path;
        DetailText.Text = AssetInfo.Describe(a.Type, item.FullPath);
        PreviewImage.Source = AssetItem.Make(a.Type, item.FullPath, p, 160);
        PreviewButton.Visibility = a.Type is AssetType.Song or AssetType.Sound or AssetType.Audio or AssetType.Mix or AssetType.Sprite ? Visibility.Visible : Visibility.Collapsed;
        UsedByText.Text = "Looking for users…";
        var users = await Task.Run(() => p.UsersOf(a.Id).Select(u => u.Name).ToList());
        if (Selected?.Id != a.Id) return;   // selection moved on meanwhile
        UsedByText.Text = users.Count == 0 ? "Not used by other assets." : "Used by: " + string.Join(", ", users);
    }

    // ---------- preview ----------

    void OnPreview(object sender, RoutedEventArgs e) => Preview();

    void Preview()
    {
        if (List.SelectedItem is not AssetItem item) return;
        if (_anim.IsEnabled) { StopPreview(); return; }
        try
        {
            switch (item.Entry.Type)
            {
                case AssetType.Song: AudioOut.Shared.PlayOnce(Synth.Render(Song.Load(item.FullPath))); break;
                case AssetType.Sound: AudioOut.Shared.PlayOnce(Sfx.Load(item.FullPath).Render()); break;
                case AssetType.Audio or AssetType.Mix when App.Project is { } p: AudioOut.Shared.PlayOnce(new AudioLibrary(p).Samples(item.Entry.Id)); break;
                case AssetType.Sprite:
                    _animSprite = ProjectFile.Load(item.FullPath);
                    if (_animSprite.Frames.Count < 2) return;
                    _animFrame = -1;
                    NextAnimFrame();
                    _anim.Start();
                    PreviewButton.Content = "■";
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { Ui.Error(ex); }
    }

    void NextAnimFrame()
    {
        if (_animSprite is not { } s) return;
        _animFrame = (_animFrame + 1) % s.Frames.Count;
        PreviewImage.Source = ImageIO.ToBitmap(Compositor.Flatten(s, _animFrame), s.Width, s.Height);
        _anim.Interval = TimeSpan.FromMilliseconds(s.Frames[_animFrame].DurationMs);
    }

    void StopPreview()
    {
        _anim.Stop();
        _animSprite = null;
        PreviewButton.Content = "▶";
    }

    // ---------- commands ----------

    void OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
    void OnOpenItem(object sender, RoutedEventArgs e) => OpenSelected();

    void OpenSelected()
    {
        if (Selected is { } a) OpenRequested?.Invoke(a);
    }

    void OnListKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Enter: OpenSelected(); break;
            case Key.F2: Rename(); break;
            case Key.Delete: Remove(); break;
            case Key.D when ctrl: ToggleFavorite(); break;
            case Key.H when ctrl: ShowHistory(); break;
            case Key.Space: Preview(); break;
            default: return;
        }
        e.Handled = true;
    }

    void Do(Action<Project, AssetEntry> change)
    {
        if (P is not { } p || Selected is not { } a) return;
        try
        {
            change(p, a);
            App.NotifyProjectChanged();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException) { Ui.Error(ex); }
    }

    void OnRename(object sender, RoutedEventArgs e) => Rename();

    void Rename() => Do((p, a) =>
    {
        if (Ui.Ask(this, "Rename asset", "New name (references to it stay valid):", a.Name) is not string name) return;
        string old = p.FullPath(a);
        p.Rename(a.Id, name);
        FileMoved?.Invoke(old, p.FullPath(a));
    });

    void OnMove(object sender, RoutedEventArgs e) => Do((p, a) =>
    {
        if (Ui.Ask(this, "Move asset", "Folder inside the project (empty = top level):", a.Folder) is not string folder) return;
        string old = p.FullPath(a);
        p.Move(a.Id, folder);
        FileMoved?.Invoke(old, p.FullPath(a));
    });

    void OnDuplicate(object sender, RoutedEventArgs e) => Do((p, a) => p.Duplicate(a.Id));

    void OnFavorite(object sender, RoutedEventArgs e) => ToggleFavorite();

    void ToggleFavorite() => Do((p, a) => { a.Favorite = !a.Favorite; p.Save(); });

    void OnTags(object sender, RoutedEventArgs e) => Do((p, a) =>
    {
        if (Ui.Ask(this, "Tags", "Tags, separated by commas:", string.Join(", ", a.Tags)) is not string text) return;
        a.Tags = [.. text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        p.Save();
    });

    void OnRemove(object sender, RoutedEventArgs e) => Remove();

    void Remove() => Do((p, a) =>
    {
        int users = p.UsersOf(a.Id).Count();
        string warn = users > 0 ? $"\n\n{users} other asset(s) use it and will show it as missing." : "";
        if (MessageBox.Show(Window.GetWindow(this), $"Remove “{a.Name}” from the project?\nThe file is moved to the project's .trash folder, not deleted.{warn}",
                "Remove asset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        p.Remove(a.Id);
    });

    // ---------- several assets ----------

    /// <summary>Runs a change over the selection; versions of the touched files are recorded first.</summary>
    void DoMany(string what, Func<Project, List<AssetEntry>, string?> change)
    {
        if (P is not { } p || SelectedAll is not { Count: > 0 } sel) return;
        try
        {
            foreach (var a in sel) App.RecordVersion(p.FullPath(a), "before " + what);
            if (change(p, sel) is string status) Status(status);
            App.NotifyProjectChanged();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or KeyNotFoundException or InvalidDataException or InvalidOperationException)
        {
            Ui.Error(ex);
        }
    }

    void Status(string text) => System.Windows.Automation.AutomationProperties.SetItemStatus(List, text);

    void OnBatchRename(object sender, RoutedEventArgs e) => DoMany("rename", (p, sel) =>
    {
        if (Ui.Ask(this, "Rename selected", $"Pattern for {sel.Count} file(s): {{name}} = current name, {{n}} = number (01, 02 …)", "{name}") is not string pattern) return null;
        var moved = Batch.Rename(p, [.. sel.Select(a => a.Id)], pattern);
        foreach (var (from, to) in moved) FileMoved?.Invoke(from, to);
        return $"Renamed {moved.Count} file(s).";
    });

    void OnBatchTags(object sender, RoutedEventArgs e) => DoMany("tags", (p, sel) =>
    {
        if (Ui.AskFields(this, $"Tags for {sel.Count} asset(s)", ("_Add (commas)", "", null), ("_Remove (commas)", "", null)) is not { } f) return null;
        Batch.Tag(p, sel.Select(a => a.Id), f[0].Split(','), f[1].Split(','));
        return "Tags updated.";
    });

    void OnBatchScale(object sender, RoutedEventArgs e) => DoMany("scale", (p, sel) =>
    {
        if (Ui.AskFields(this, "Scale selected sprites", ("_Percent", "200", ["25", "50", "200", "300", "400"])) is not { } f) return null;
        if (!int.TryParse(f[0], out int pct)) throw new ArgumentException("A whole percentage, e.g. 200.");
        var files = Batch.ScaleSprites(p, sel.Select(a => a.Id), pct);
        foreach (var f2 in files) App.NotifyAssetRestored(f2);   // open tabs reload the scaled file
        return $"Scaled {files.Count} sprite(s); previous versions are in the history.";
    });

    void OnBatchConvert(object sender, RoutedEventArgs e) => DoMany("convert", (p, sel) =>
    {
        var made = Batch.Convert(p, sel.Select(a => a.Id));
        return made.Count == 0 ? "Only images and sprites can be converted." : $"Made {made.Count} new file(s): " + string.Join(", ", made.Select(a => a.Path));
    });

    /// <summary>Points every scene, character, map, mix, playlist and export profile that uses the selected asset at another one of the same type.</summary>
    void OnReplaceEverywhere(object sender, RoutedEventArgs e)
    {
        if (P is not { } p || Selected is not { } a) return;
        var users = p.UsersOf(a.Id).ToList();
        var candidates = p.Assets.Where(x => x.Type == a.Type && x.Id != a.Id).OrderBy(x => x.Path).ToList();
        if (candidates.Count == 0) { Ui.Error(new InvalidOperationException($"There is no other {a.Type} in the project to use instead.")); return; }
        if (Ui.AskFields(this, $"Replace “{a.Name}” everywhere", ("_Use instead", candidates[0].Path, [.. candidates.Select(c => c.Path)])) is not { } f) return;
        var target = candidates.First(c => c.Path == f[0]);
        if (MessageBox.Show(Window.GetWindow(this), $"{users.Count} asset(s) use “{a.Name}”" + (users.Count > 0 ? ":\n" + string.Join("\n", users.Take(15).Select(u => "• " + u.Path)) : ".")
                + $"\n\nPoint them, playlists and export profiles at “{target.Name}”? Previous versions are kept in the history.",
                "Replace everywhere", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            foreach (var u in users) App.RecordVersion(p.FullPath(u), $"before replacing {a.Name}");
            var changed = p.ReplaceReferences(a.Id, target.Id);
            foreach (var c in changed) { App.RecordVersion(p.FullPath(c), $"{a.Name} → {target.Name}"); App.NotifyAssetRestored(p.FullPath(c)); }
            App.NotifyProjectChanged();
            Status($"{changed.Count} asset(s) now use {target.Name}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        if (P is not { } p) return;
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Pixelkiln assets|" + string.Join(";", Project.TypesByExtension.Keys.Select(x => "*" + x)) + "|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        string folder = Selected?.Folder ?? "";
        var skipped = new List<string>();
        int added = 0;
        foreach (var src in dlg.FileNames)
        {
            try
            {
                string full = Path.GetFullPath(src);
                string dest = full.StartsWith(p.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? full   // already in the project folder: only register
                    : Path.Combine(p.Root, folder, Path.GetFileName(src));
                if (dest != full)
                {
                    if (File.Exists(dest)) { skipped.Add($"{Path.GetFileName(src)} (a file with that name exists)"); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(full, dest);
                }
                p.Add(dest);
                added++;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { skipped.Add($"{Path.GetFileName(src)} ({ex.Message})"); }
        }
        App.NotifyProjectChanged();
        if (skipped.Count > 0)
            MessageBox.Show(Window.GetWindow(this), $"Added {added}. Not added:\n" + string.Join("\n", skipped), "Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void OnHistory(object sender, RoutedEventArgs e) => ShowHistory();

    public void ShowHistory()
    {
        if (P is { } p && Selected is { } a) HistoryWindow.Show(Window.GetWindow(this), p, a);
    }

    void OnCheck(object sender, RoutedEventArgs e)
    {
        if (P is { } p) HealthWindow.Show(Window.GetWindow(this), p);
    }

    void OnNewProject(object sender, RoutedEventArgs e) => ProjectCommands.New(Window.GetWindow(this));
    void OnOpenProject(object sender, RoutedEventArgs e) => ProjectCommands.Open(Window.GetWindow(this));
    void OnProjectFromFolder(object sender, RoutedEventArgs e) => ProjectCommands.FromFolder(Window.GetWindow(this));
}
