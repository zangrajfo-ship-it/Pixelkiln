using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Installed data plugins, the ones refused and why, install from a folder, reload.</summary>
static class PluginsWindow
{
    public static void Show(Window owner)
    {
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(report, "Plugins report");
        var install = new Button { Content = "Install from folder…", Margin = new Thickness(0, 0, 8, 0) };
        var reload = new Button { Content = "Reload", Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Content = "Open plugins folder", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { install, reload, open, close } };
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Plugins are data only (palettes, generator presets, export profiles, command macros); Pixelkiln never runs code from them. "
                   + "Their commands are in the command palette (Ctrl+Shift+P) under “Plugin ›”.",
        };
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var win = new Window
        {
            Title = "Plugins", Width = 640, Height = 520, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { note, buttons, report } },
        };

        void Fill()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Folder: {PluginLoader.DefaultFolder}");
            sb.AppendLine($"Plugin API: {PluginLoader.ApiVersion}");
            sb.AppendLine();
            sb.AppendLine($"Loaded: {App.Plugins.Count}");
            foreach (var p in App.Plugins)
            {
                var m = p.Manifest;
                var c = m.Contributions;
                sb.AppendLine($"  • {m.Name} {m.Version} ({m.Id}){(m.Author is { Length: > 0 } a ? " by " + a : "")}");
                if (m.Description is { Length: > 0 } d) sb.AppendLine($"    {d}");
                sb.AppendLine($"    {c.Palettes?.Count ?? 0} palette(s), {c.GeneratorPresets?.Count ?? 0} generator preset(s), {c.ExportProfiles?.Count ?? 0} export profile(s), {c.Macros?.Count ?? 0} macro(s)");
            }
            sb.AppendLine();
            sb.AppendLine($"Refused: {App.RejectedPlugins.Count}");
            foreach (var r in App.RejectedPlugins) sb.AppendLine($"  • {System.IO.Path.GetFileName(r.Folder)}: {r.Reason}");
            report.Text = sb.ToString();
        }

        install.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "Plugin folder (named after the plugin id, containing plugin.json)" };
            if (dlg.ShowDialog(win) != true) return;
            try
            {
                Directory.CreateDirectory(PluginLoader.DefaultFolder);
                var p = PluginLoader.Install(dlg.FolderName, PluginLoader.DefaultFolder);
                App.LoadPlugins();
                Fill();
                MessageBox.Show(win, $"Installed “{p.Manifest.Name}”.", "Plugins");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); }
        };
        reload.Click += (_, _) => { App.LoadPlugins(); Fill(); };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(PluginLoader.DefaultFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PluginLoader.DefaultFolder}\"") { UseShellExecute = false });
        };
        Fill();
        win.ShowDialog();
    }
}
