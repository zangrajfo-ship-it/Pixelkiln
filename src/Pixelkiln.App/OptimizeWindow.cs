using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>
/// Project › Optimize assets: sizes, unused and identical assets, oversized pictures and what an atlas saves; can point
/// uses of identical files at one copy and write an atlas. Never deletes files.
/// </summary>
static class OptimizeWindow
{
    public static void Show(Window owner, Project p, Action changed)
    {
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        AutomationProperties.SetName(report, "Optimization report");
        var maxBox = new ComboBox { ItemsSource = new[] { "1024", "2048", "4096" }, SelectedItem = "2048", Width = 80, Margin = new Thickness(6, 0, 12, 0) };
        AutomationProperties.SetName(maxBox, "Largest texture");
        var analyse = new Button { Content = "Analyse", Padding = new Thickness(10, 2, 10, 2) };
        var merge = new Button { Content = "Point identical files to one copy", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), IsEnabled = false,
            ToolTip = "Scenes, characters and kits that use a copy are changed to use the first file; the copies stay on disk" };
        var atlasBtn = new Button { Content = "Write atlas…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var bar = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "Largest texture (px)", VerticalAlignment = VerticalAlignment.Center }, maxBox, analyse, merge, atlasBtn, close },
        };
        DockPanel.SetDock(bar, Dock.Top);
        var win = new Window
        {
            Title = $"Optimize assets “{p.Name}”", Width = 760, Height = 600, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { bar, report } },
        };
        OptimizeReport? last = null;
        int Max() => int.Parse((string)maxBox.SelectedItem);

        async void Analyse()
        {
            int max = Max();
            try
            {
                last = await Ui.RunLong(win, "Analysing assets", (_, ct) => Optimizer.Run(p, max, ct));
                report.Text = last?.Text() ?? "Cancelled.";
                merge.IsEnabled = last?.Duplicates.Count > 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Ui.Error(ex); }
        }

        analyse.Click += (_, _) => Analyse();
        merge.Click += (_, _) =>
        {
            if (last is null) return;
            int copies = last.Duplicates.Sum(g => g.Count - 1);
            if (MessageBox.Show(win, $"Change every use of {copies} identical cop{(copies == 1 ? "y" : "ies")} to the first file of its group? The copies stay on disk and can be removed in the asset browser.",
                    "Identical files", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                int files = 0;
                foreach (var g in last.Duplicates)
                    foreach (var copy in g.Skip(1))
                    {
                        foreach (var user in p.UsersOf(copy.Id)) App.RecordVersion(p.FullPath(user));   // history before the change
                        files += p.ReplaceReferences(copy.Id, g[0].Id).Count;
                    }
                changed();
                MessageBox.Show(win, $"{files} file(s) now use the first copy.", "Identical files");
                Analyse();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { Ui.Error(ex); }
        };
        atlasBtn.Click += async (_, _) =>
        {
            var dlg = new SaveFileDialog { Filter = "Atlas description (*.json)|*.json", FileName = "atlas.json", InitialDirectory = p.Root };
            if (dlg.ShowDialog(win) != true) return;
            int max = Max();
            try
            {
                var atlas = await Ui.RunLong(win, "Packing the atlas", (_, ct) => Atlas.Pack(Optimizer.Pictures(p, p.Assets, a => a.Path), max));
                if (atlas is null) return;
                string stem = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dlg.FileName)!, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
                for (int i = 0; i < atlas.Pages.Count; i++) ImageIO.SavePng($"{stem}{i}.png", atlas.Pages[i].Pixels, atlas.Pages[i].W, atlas.Pages[i].H);
                File.WriteAllText(dlg.FileName, Atlas.Json(atlas, i => $"{System.IO.Path.GetFileName(stem)}{i}.png"));
                MessageBox.Show(win, $"Wrote {atlas.Pages.Count} page(s) and {System.IO.Path.GetFileName(dlg.FileName)}.", "Atlas");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Ui.Error(ex); }
        };
        win.Loaded += (_, _) => Analyse();
        win.ShowDialog();
    }
}
