using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelForge.App;

/// <summary>A runnable command. Built from the menus, so every menu command is in the palette without duplicate logic.</summary>
public sealed record Command(string Title, string Category, string? Gesture, Func<bool> CanRun, Action Run)
{
    public override string ToString() => Title;
}

/// <summary>Turns menus into commands (recursively) and filters them by typed words; MainWindow.AllCommands adds tabs, assets and plugin macros.</summary>
public static class CommandRegistry
{
    public static IEnumerable<Command> FromMenu(ItemsControl menu, string path = "")
    {
        foreach (var mi in menu.Items.OfType<MenuItem>())
        {
            string header = (mi.Header as string ?? "").Replace("_", "");
            string title = path.Length == 0 ? header : $"{path} › {header}";
            if (mi.Items.Count > 0)
            {
                foreach (var c in FromMenu(mi, title)) yield return c;   // submenu; File › Open recent fills when opened (until then only "(none)", skipped below)
                continue;
            }
            if (header.Length == 0 || header.StartsWith('(')) continue;
            var item = mi;
            yield return new Command(title, path.Split(" › ")[0], string.IsNullOrEmpty(mi.InputGestureText) ? null : mi.InputGestureText,
                () => item.IsEnabled,
                () =>
                {
                    if (item.IsCheckable) item.IsChecked = !item.IsChecked;
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                });
        }
    }

    /// <summary>All words must appear in the title (any order, case-insensitive).</summary>
    public static IEnumerable<Command> Filter(IEnumerable<Command> all, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(c => words.All(w => c.Title.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>Ctrl+Shift+P: type to find a command or asset, Enter to run it.</summary>
static class CommandPalette
{
    public static void Show(Window owner, IReadOnlyList<Command> commands)
    {
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(box, "Type a command or asset name");
        var list = new ListBox { MinHeight = 300 };
        AutomationProperties.SetName(list, "Matching commands");
        var hint = new TextBlock { Text = "Enter runs · ↑↓ choose · Esc closes · greyed = not available now", FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
        DockPanel.SetDock(box, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        var win = new Window
        {
            Title = "Command palette", Width = 620, Height = 460, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(10), Children = { box, hint, list } },
        };

        var recent = App.Settings.RecentCommands;
        int Rank(Command c) => recent.IndexOf(c.Title) is var i and >= 0 ? i : int.MaxValue;

        void Fill()
        {
            list.Items.Clear();
            foreach (var c in CommandRegistry.Filter(commands, box.Text).OrderBy(Rank).Take(200))   // recently used first, then menu order
            {
                bool can = c.CanRun();
                var row = new DockPanel { Opacity = can ? 1 : 0.5 };
                var gesture = new TextBlock { Text = c.Gesture ?? "", Margin = new Thickness(12, 0, 0, 0) };
                gesture.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
                DockPanel.SetDock(gesture, Dock.Right);
                row.Children.Add(gesture);
                row.Children.Add(new TextBlock { Text = c.Title });
                // the name goes on the list item itself: that is what screen readers announce
                var item = new ListBoxItem { Content = row, Tag = c };
                AutomationProperties.SetName(item, c.Title + (c.Gesture is null ? "" : $", {c.Gesture}") + (can ? "" : ", not available now"));
                list.Items.Add(item);
            }
            list.SelectedIndex = list.Items.Count > 0 ? 0 : -1;
        }

        void RunSelected()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: Command c } || !c.CanRun()) return;
            win.Close();
            App.RememberCommand(c.Title);
            c.Run();
        }

        box.TextChanged += (_, _) => Fill();
        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); break;
                case Key.Up: list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); break;
                case Key.Enter: RunSelected(); break;
                case Key.Escape: win.Close(); break;
                default: return;
            }
            list.ScrollIntoView(list.SelectedItem);
            e.Handled = true;
        };
        list.MouseDoubleClick += (_, _) => RunSelected();
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) RunSelected(); };
        win.Loaded += (_, _) => { Fill(); box.Focus(); };
        win.ShowDialog();
    }
}
