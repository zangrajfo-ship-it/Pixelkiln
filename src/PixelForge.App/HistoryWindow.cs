using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Saved versions of one asset: compare a version with the current file (or two versions), and restore.</summary>
static class HistoryWindow
{
    public static void Show(Window owner, Project p, AssetEntry asset)
    {
        var store = new VersionStore(p);
        App.RecordVersion(p.FullPath(asset));   // the current state is always in the list
        var list = new ListBox { SelectionMode = SelectionMode.Extended, Width = 300 };
        AutomationProperties.SetName(list, "Versions, newest first. Select one to compare with the current file, or two to compare them.");
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Image Pic(string name)
        {
            var i = new Image { Stretch = Stretch.Uniform, MaxHeight = 220, Margin = new Thickness(0, 2, 8, 0) };
            RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.NearestNeighbor);
            AutomationProperties.SetName(i, name);
            return i;
        }
        Image older = Pic("Older version"), newer = Pic("Newer version"), diff = Pic("Changed pixels in magenta");
        var pics = new UniformGrid3(older, newer, diff);
        var fields = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(fields, "Changed fields");
        var restore = new Button { Content = "Restore selected version", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        var info = new TextBlock { Foreground = (Brush)Application.Current.FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var maxVersions = new TextBox { Width = 50, Text = p.Settings.HistoryMaxVersions.ToString() };
        var maxMb = new TextBox { Width = 60, Text = p.Settings.HistoryMaxMegabytes.ToString() };
        AutomationProperties.SetName(maxVersions, "Versions kept per asset");
        AutomationProperties.SetName(maxMb, "History size limit in megabytes");
        var saveLimits = new Button { Content = "Set limits", Margin = new Thickness(4, 0, 0, 0) };
        var limits = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { info, new TextBlock { Text = "Keep ", VerticalAlignment = VerticalAlignment.Center }, maxVersions,
                new TextBlock { Text = " versions, max ", VerticalAlignment = VerticalAlignment.Center }, maxMb, new TextBlock { Text = " MB", VerticalAlignment = VerticalAlignment.Center }, saveLimits },
        };
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { restore, close } };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(limits);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(list, Dock.Left);
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(pics, Dock.Top);
        right.Children.Add(title);
        right.Children.Add(pics);
        right.Children.Add(fields);
        var win = new Window
        {
            Title = $"History of “{asset.Path}”", Width = 980, Height = 640, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { bottom, list, right } },
        };

        List<VersionEntry> versions = [];
        void Fill()
        {
            versions = [.. store.Versions(asset.Id)];
            list.ItemsSource = versions.Select((v, i) => $"{v.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss}{(i == 0 ? " (current)" : "")} · {v.Size / 1024.0:0.#} KB{(v.Note.Length > 0 ? " · " + v.Note : "")}").ToList();
            info.Text = $"{versions.Count} version(s) · .history uses {store.TotalBytes / 1048576.0:0.##} MB.";
            restore.IsEnabled = false;
        }

        void Compare()
        {
            var sel = list.SelectedItems.Cast<string>().Select(s => list.Items.IndexOf(s)).Order().ToList();
            restore.IsEnabled = sel.Count == 1 && sel[0] > 0;
            if (sel.Count is 0 or > 2) { title.Text = "Select one version (compared with the current file) or two."; return; }
            var (a, b) = sel.Count == 1 ? (versions[sel[0]], versions[0]) : (versions[sel[1]], versions[sel[0]]);   // older, newer
            title.Text = $"{a.Utc.ToLocalTime():g} → {b.Utc.ToLocalTime():g}{(b == versions[0] ? " (current)" : "")}";
            try
            {
                byte[] da = store.Read(a), db = store.Read(b);
                fields.Text = asset.Type is AssetType.Image or AssetType.Audio ? (da.AsSpan().SequenceEqual(db) ? "identical" : $"binary content differs ({da.Length} → {db.Length} bytes)")
                    : string.Join("\n", VersionDiff.Fields(da, db));
                var pa = VersionDiff.Render(asset.Type, da, p);
                var pb = VersionDiff.Render(asset.Type, db, p);
                older.Source = pa is null ? null : ImageIO.ToBitmap(pa.Pixels, pa.Width, pa.Height);
                newer.Source = pb is null ? null : ImageIO.ToBitmap(pb.Pixels, pb.Width, pb.Height);
                if (pa is not null && pb is not null)
                {
                    var (img, changed) = VersionDiff.Pixels(pa, pb);
                    diff.Source = ImageIO.ToBitmap(img.Pixels, img.Width, img.Height);
                    title.Text += $" · {changed} pixel(s) changed";
                }
                else diff.Source = null;
                pics.Visibility = pa is null && pb is null ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or System.Text.Json.JsonException)
            {
                fields.Text = "Cannot compare: " + ex.Message;
                older.Source = newer.Source = diff.Source = null;
            }
        }

        list.SelectionChanged += (_, _) => Compare();
        restore.Click += (_, _) =>
        {
            if (list.SelectedIndex <= 0) return;
            var v = versions[list.SelectedIndex];
            if (MessageBox.Show(win, $"Restore the version from {v.Utc.ToLocalTime():g}? The current file is kept in the history first, so this can be undone the same way.",
                    "Restore version", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                store.Restore(asset.Id, v);
                App.NotifyAssetRestored(p.FullPath(asset));
                Fill();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };
        saveLimits.Click += (_, _) =>
        {
            bool ok = int.TryParse(maxVersions.Text, out int n) & int.TryParse(maxMb.Text, out int mb) && n is >= 1 and <= 10000 && mb is >= 1 and <= 100000;
            Ui.MarkInvalid(maxVersions, ok ? null : "Versions 1–10000, size 1–100000 MB.");
            if (!ok) return;
            (p.Settings.HistoryMaxVersions, p.Settings.HistoryMaxMegabytes) = (n, mb);
            try { p.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
            store = new VersionStore(p);
            info.Text = "Limits saved; they apply from the next saved version.";
        };

        Fill();
        if (versions.Count > 1) list.SelectedIndex = 1;
        win.ShowDialog();
    }

    /// <summary>Three equal columns with captions.</summary>
    sealed class UniformGrid3 : Grid
    {
        public UniformGrid3(Image a, Image b, Image c)
        {
            Margin = new Thickness(0, 6, 0, 6);
            foreach (var (img, caption, col) in new[] { (a, "Older", 0), (b, "Newer", 1), (c, "Difference", 2) })
            {
                ColumnDefinitions.Add(new ColumnDefinition());
                var panel = new StackPanel { Children = { new TextBlock { Text = caption, FontSize = 11 }, new Border { Background = Ui.CheckerBrush(), Child = img, HorizontalAlignment = HorizontalAlignment.Left } } };
                SetColumn(panel, col);
                Children.Add(panel);
            }
        }
    }
}
