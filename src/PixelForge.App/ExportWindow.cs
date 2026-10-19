using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Export profiles (saved in the project), a check that lists what would be written, and the export itself.</summary>
static class ExportWindow
{
    public static void Show(Window owner, Project p, IReadOnlyList<string> unsaved)
    {
        var profiles = p.Settings.ExportProfiles;
        if (profiles.Count == 0)
        {
            profiles.Add(new ExportProfile());
            profiles.Add(new ExportProfile { Name = "Generic (PNG, JSON, TMJ, WAV)", Target = ExportTarget.Generic, OutputFolder = "export/generic" });
        }

        var profileBox = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 4, 0) };
        AutomationProperties.SetName(profileBox, "Export profile");
        var add = new Button { Content = "New", Margin = new Thickness(0, 0, 4, 0) };
        var del = new Button { Content = "Delete" };
        var nameBox = new TextBox();
        var targetBox = new ComboBox { ItemsSource = Enum.GetNames<ExportTarget>() };
        var outBox = new TextBox();
        var resBox = new TextBox();
        var zipBox = new CheckBox { Content = "Also write a .zip next to the folder", Margin = new Thickness(0, 6, 0, 0) };
        var allBox = new CheckBox { Content = "All assets (otherwise the ones selected below, plus what they use)", Margin = new Thickness(0, 6, 0, 0) };
        var assetList = new ListBox { SelectionMode = SelectionMode.Extended, Height = 140, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(assetList, "Assets to export");
        assetList.ItemsSource = p.Assets.OrderBy(a => a.Path).Select(a => new ListBoxItem { Content = $"{a.Type} · {a.Path}", Tag = a.Id }).ToList();
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
        AutomationProperties.SetName(report, "Export report");
        AutomationProperties.SetLiveSetting(report, AutomationLiveSetting.Polite);
        var check = new Button { Content = "Check", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var export = new Button { Content = "Export", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Content = "Open folder", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };

        var form = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, FrameworkElement box)
        {
            int r = form.RowDefinitions.Count;
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new Label { Content = label, Target = box, Padding = new Thickness(0, 4, 8, 4) };
            box.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(l, r);
            Grid.SetRow(box, r);
            Grid.SetColumn(box, 1);
            form.Children.Add(l);
            form.Children.Add(box);
        }
        Row("_Name", nameBox);
        Row("_Target", targetBox);
        Row("_Output folder", outBox);
        Row("_Godot folder (res://)", resBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { check, export, open, close } };
        var warn = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Orange, Margin = new Thickness(0, 6, 0, 0),
            Text = unsaved.Count == 0 ? "" : "Unsaved changes are not exported (files are read from disk): " + string.Join(", ", unsaved.Select(System.IO.Path.GetFileName)),
            Visibility = unsaved.Count == 0 ? Visibility.Collapsed : Visibility.Visible };
        var top = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { profileBox, add, del } },
                form, zipBox, allBox, assetList, warn,
            },
        };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var win = new Window
        {
            Title = $"Export “{p.Name}”", Width = 720, Height = 760, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { top, buttons, new GroupBox { Header = "Report", Margin = new Thickness(0, 8, 0, 0), Content = report } } },
        };

        bool loading = false;
        ExportProfile? Current() => profiles.ElementAtOrDefault(profileBox.SelectedIndex);

        void Load()
        {
            loading = true;
            var pr = Current();
            if (pr is not null)
            {
                nameBox.Text = pr.Name;
                targetBox.SelectedItem = pr.Target.ToString();
                outBox.Text = pr.OutputFolder;
                resBox.Text = pr.ResFolder;
                resBox.IsEnabled = pr.Target == ExportTarget.Godot4;
                zipBox.IsChecked = pr.Zip;
                allBox.IsChecked = pr.Assets is null;
                assetList.IsEnabled = pr.Assets is not null;
                assetList.SelectedItems.Clear();
                foreach (ListBoxItem i in assetList.Items) if (pr.Assets?.Contains((Guid)i.Tag) == true) assetList.SelectedItems.Add(i);
            }
            loading = false;
        }

        void RefreshProfiles(int select)
        {
            loading = true;
            profileBox.ItemsSource = profiles.Select(x => x.Name).ToList();
            profileBox.SelectedIndex = Math.Clamp(select, 0, profiles.Count - 1);
            del.IsEnabled = profiles.Count > 1;
            loading = false;
            Load();
        }

        /// <summary>Form → profile → project file. Returns false (with the field marked) when the form is not valid.</summary>
        bool Store()
        {
            if (Current() is not { } pr) return false;
            bool ok = nameBox.Text.Trim().Length > 0 && outBox.Text.Trim().Length > 0;
            Ui.MarkInvalid(nameBox, nameBox.Text.Trim().Length > 0 ? null : "A profile needs a name.");
            Ui.MarkInvalid(outBox, outBox.Text.Trim().Length > 0 ? null : "A folder inside the project, e.g. export/godot.");
            if (!ok) return false;
            pr.Name = nameBox.Text.Trim();
            pr.Target = Enum.Parse<ExportTarget>((string)targetBox.SelectedItem);
            pr.OutputFolder = outBox.Text.Trim().Replace('\\', '/');
            pr.ResFolder = resBox.Text.Trim().Trim('/');
            pr.Zip = zipBox.IsChecked == true;
            pr.Assets = allBox.IsChecked == true ? null : assetList.SelectedItems.Cast<ListBoxItem>().Select(i => (Guid)i.Tag).ToList();
            try { p.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return false; }
            int keep = profileBox.SelectedIndex;
            loading = true;
            profileBox.ItemsSource = profiles.Select(x => x.Name).ToList();
            profileBox.SelectedIndex = keep;
            loading = false;
            return true;
        }

        profileBox.SelectionChanged += (_, _) => { if (!loading) Load(); };
        targetBox.SelectionChanged += (_, _) => { if (!loading) resBox.IsEnabled = (string?)targetBox.SelectedItem == nameof(ExportTarget.Godot4); };
        allBox.Click += (_, _) => assetList.IsEnabled = allBox.IsChecked != true;
        add.Click += (_, _) =>
        {
            profiles.Add(new ExportProfile { Name = $"Profile {profiles.Count + 1}", OutputFolder = $"export/profile{profiles.Count + 1}" });
            RefreshProfiles(profiles.Count - 1);
            Store();
        };
        del.Click += (_, _) =>
        {
            if (Current() is not { } pr || profiles.Count < 2) return;
            if (MessageBox.Show(win, $"Delete profile “{pr.Name}”? Exported files stay.", "Delete profile", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            profiles.Remove(pr);
            RefreshProfiles(0);
            Store();
        };
        check.Click += async (_, _) =>
        {
            if (!Store() || Current() is not { } pr) return;
            if (await Ui.RunLong(win, "Checking export", (progress, cancel) => Exporter.Plan(p, pr, progress, cancel)) is { } r) report.Text = r.Report;
        };
        export.Click += async (_, _) =>
        {
            if (!Store() || Current() is not { } pr) return;
            try
            {
                if (await Ui.RunLong(win, "Exporting", (progress, cancel) => Exporter.Run(p, pr, progress, cancel)) is not { } r) { report.Text = "Export cancelled; the previous export is unchanged."; return; }
                report.Text = (r.Ok ? $"Exported to {pr.OutputFolder}{(pr.Zip ? " (+ .zip)" : "")}.\n\n" : "") + r.Report;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };
        open.Click += (_, _) =>
        {
            if (Current() is not { } pr) return;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(p.Root, pr.OutputFolder));
            if (!dir.StartsWith(p.Root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir)) { report.Text = "Nothing exported to this folder yet."; return; }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
        };

        RefreshProfiles(0);
        win.ShowDialog();
    }
}
