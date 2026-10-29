using System.Text;
using System.Windows;
using System.Windows.Controls;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Project check: missing, damaged, unregistered and unused assets, broken references; with fixes that never delete files.</summary>
static class HealthWindow
{
    public static void Show(Window owner, Project p)
    {
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(report, "Project check report");
        var addLoose = new Button { Content = "Add unregistered files", Margin = new Thickness(0, 0, 8, 0) };
        var forget = new Button { Content = "Forget missing entries", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { addLoose, forget, close } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var win = new Window
        {
            Title = $"Check project “{p.Name}”", Width = 640, Height = 520, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { buttons, report } },
        };

        void Run()
        {
            var missing = p.MissingAssets().ToList();
            var loose = p.UnregisteredFiles().ToList();
            var damaged = p.DamagedAssets().ToList();
            var broken = p.BrokenReferences().ToList();
            // only images and palettes are meant to be used by others; an unused map or song is normal
            var unused = p.Assets.Where(a => a.Type is AssetType.Image or AssetType.Palette && !p.UsersOf(a.Id).Any()).ToList();

            var sb = new StringBuilder();
            void Section<T>(string title, List<T> items, Func<T, string> line)
            {
                sb.AppendLine($"{title}: {(items.Count == 0 ? "none" : items.Count.ToString())}");
                foreach (var i in items) sb.AppendLine("  • " + line(i));
                sb.AppendLine();
            }
            Section("Missing files", missing, a => a.Path);
            Section("Damaged files", damaged, d => $"{d.Asset.Path}: {d.Problem}");
            Section("Broken references", broken, b => $"{b.From.Path} → unknown asset {b.To}");
            Section("Files in the folder but not in the project", loose, f => Path.GetRelativePath(p.Root, f));
            Section("Images and palettes nothing uses", unused, a => a.Path);
            if (!p.Settings.Style.IsEmpty) Section("Style guide", StyleCheck.Run(p, p.Settings.Style), i => $"{i.Asset.Path}: {i.Message}");
            report.Text = sb.ToString();
            addLoose.IsEnabled = loose.Count > 0;
            forget.IsEnabled = missing.Count > 0;
        }

        addLoose.Click += (_, _) =>
        {
            foreach (var f in p.UnregisteredFiles()) p.Add(f);
            App.NotifyProjectChanged();
            Run();
        };
        forget.Click += (_, _) =>
        {
            if (MessageBox.Show(win, "Remove the entries whose file is missing from the project? Assets that refer to them will report broken references.",
                    "Forget missing entries", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            foreach (var a in p.MissingAssets().ToList()) p.Assets.Remove(a);
            p.Save();
            App.NotifyProjectChanged();
            Run();
        };
        win.Loaded += (_, _) => Run();
        win.ShowDialog();
    }
}
