using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Command palette sources: every tab's menus, the shared menus, tabs, assets and plugin contributions.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Every command with a stable title: "Sprite › Edit › Flip horizontally" works from any tab (it switches there first),
    /// so macros can name commands of any editor.
    /// </summary>
    List<Command> AllCommands()
    {
        var commands = new List<Command>();
        foreach (TabItem tab in Tabs.Items)
        {
            var t = tab;
            // the first mode keeps the plain tab prefix ("Sprite › Edit › …"), so existing macros keep working
            var modes = tab.Content is ModeHost host ? host.Modes : tab.Content is IEditor single ? [("", single)] : [];
            for (int m = 0; m < modes.Count; m++)
            {
                var ed = modes[m].Editor;
                string prefix = m == 0 ? $"{t.Header}" : $"{t.Header} › {modes[m].Name}";
                foreach (var menu in ed.Menus)
                    foreach (var c in CommandRegistry.FromMenu(menu, $"{prefix} › {((string)menu.Header).Replace("_", "")}"))
                        commands.Add(c with { Run = () => { Reveal(ed); c.Run(); } });
                if (m > 0) commands.Add(new Command($"Go to {t.Header} › {modes[m].Name} mode", "View", $"Ctrl+{m + 1}", () => true, () => Reveal(ed)));
            }
            commands.Add(new Command($"Go to {t.Header} tab", "View", null, () => true, () => Tabs.SelectedItem = t));
        }
        // shared menus (Project, Options, Help): the ones not owned by the current editor
        var own = Current?.Menus ?? [];
        foreach (var mi in MainMenu.Items.OfType<MenuItem>().Where(m => !own.Contains(m)))
            commands.AddRange(CommandRegistry.FromMenu(mi, ((string)mi.Header).Replace("_", "")));
        if (App.Project is { } p)
            foreach (var a in p.Assets)
            {
                var asset = a;
                commands.Add(new Command($"Open asset: {a.Path}", "Asset", null, () => File.Exists(p.FullPath(asset)), () => OpenAsset(asset)));
            }
        commands.AddRange(PluginCommands());
        foreach (var source in CommandRegistry.Sources) commands.AddRange(source());
        return commands;
    }

    void ShowCommandPalette() => CommandPalette.Show(this, AllCommands());

    // ---------- plugin contributions ----------

    IEnumerable<Command> PluginCommands()
    {
        foreach (var plugin in App.Plugins)
        {
            var m = plugin.Manifest;
            string prefix = $"Plugin › {m.Name} › ";
            var c = m.Contributions;
            foreach (var pal in c.Palettes ?? [])
                yield return new Command(prefix + $"Use palette: {pal.Name}", "Plugin", null, () => true, () =>
                {
                    Reveal(SpriteEd);
                    SpriteEd.ApplyPaletteColors(pal.Argb());
                });
            foreach (var g in c.GeneratorPresets ?? [])
                yield return new Command(prefix + $"Generate: {g.Name}", "Plugin", null, () => true, () => RunPreset(g));
            foreach (var e in c.ExportProfiles ?? [])
                yield return new Command(prefix + $"Add export profile: {e.Name}", "Plugin", null, () => App.Project is not null, () => AddExportProfile(e));
            foreach (var macro in c.Macros ?? [])
                yield return new Command(prefix + $"Macro: {macro.Name}", "Plugin", null, () => true, () => RunMacro(macro));
        }
    }

    void RunPreset(PluginGeneratorPreset g)
    {
        string S(string key, string fallback) => g.Settings?.GetValueOrDefault(key) ?? fallback;
        double D(string key, double fallback) => double.Parse(S(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        switch (g.Generator)
        {
            case "sprite":
                Reveal(SpriteEd);
                SpriteEd.GenerateSprite(S("seed", "1"), D("density", 0.45));
                break;
            case "tiles":
                Reveal(MapEd);
                MapEd.GenerateTileset(S("seed", "1"), Enum.Parse<TilePattern>(S("pattern", nameof(TilePattern.Noise))), S("baseColor", "4A7A3A"), int.Parse(S("variants", "4")));
                break;
            case "layout":
                Reveal(MapEd);
                MapEd.GenerateLayout(S("seed", "1"), S("layout", "Cave"), D("fill", 0.45));
                break;
        }
    }

    void AddExportProfile(PluginExportProfile e)
    {
        if (App.Project is not { } p) return;
        if (p.Settings.ExportProfiles.All(x => x.Name != e.Name))
        {
            p.Settings.ExportProfiles.Add(new ExportProfile { Name = e.Name, Target = e.Target, OutputFolder = e.OutputFolder, ResFolder = e.ResFolder, Zip = e.Zip });
            try { p.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return; }
        }
        ExportWindow.Show(this, p, [.. Editors.Where(ed => ed.IsDirty).Select(ed => ed.CurrentPath ?? "untitled")]);
    }

    /// <summary>Runs the named commands in order; checks all names first, stops at the first one that cannot run now.</summary>
    void RunMacro(PluginMacro macro)
    {
        var all = AllCommands().Where(c => !c.Title.StartsWith("Plugin › ")).GroupBy(c => c.Title).ToDictionary(g => g.Key, g => g.First());   // no macros inside macros
        var missing = macro.Commands.Where(t => !all.ContainsKey(t)).ToList();
        if (missing.Count > 0)
        {
            Ui.Error(new InvalidOperationException($"Macro “{macro.Name}” names commands that do not exist:\n" + string.Join("\n", missing)
                + "\n\nUse the titles shown in the command palette (Ctrl+Shift+P)."));
            return;
        }
        foreach (var title in macro.Commands)
        {
            var c = all[title];
            if (!c.CanRun()) { Ui.Error(new InvalidOperationException($"Macro “{macro.Name}” stopped: “{title}” is not available now.")); return; }
            c.Run();
        }
    }
}
