using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Project settings: game resolution, tile size, collision category names and which categories collide.</summary>
static class ProjectSettingsWindow
{
    const int Categories = 8;   // ponytail: the UI edits categories 0–7; the format allows 0–31

    public static void Show(Window owner, Project p)
    {
        var s = p.Settings;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < Categories + 2; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBox Field(string label, string value, int row)
        {
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var box = new TextBox { Text = value, Width = 90, Margin = new Thickness(0, 2, 8, 2) };
            var l = new Label { Content = label, Target = box };
            Grid.SetRow(l, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            Grid.SetColumnSpan(box, 3);
            grid.Children.Add(l);
            grid.Children.Add(box);
            return box;
        }

        var tw = Field("_Game width (px)", s.TargetWidth.ToString(), 0);
        var th = Field("Game _height (px)", s.TargetHeight.ToString(), 1);
        var tile = Field("_Tile size (px)", s.TileSize.ToString(), 2);

        // category names in the first column, a symmetric "collides with" matrix beside them
        var header = new TextBlock { Text = "Collision categories (tick the pairs that collide)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        Grid.SetRow(header, 3);
        Grid.SetColumnSpan(header, Categories + 2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(header);

        var names = new TextBox[Categories];
        var checks = new CheckBox[Categories, Categories];
        int top = 4;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int a = 0; a < Categories; a++)
        {
            var colHead = new TextBlock { Text = a.ToString(), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(colHead, top);
            Grid.SetColumn(colHead, a + 2);
            grid.Children.Add(colHead);
        }
        for (int a = 0; a < Categories; a++)
        {
            int row = top + 1 + a;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var num = new TextBlock { Text = a.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            names[a] = new TextBox { Text = s.CollisionCategories.GetValueOrDefault(a, ""), Width = 110, Margin = new Thickness(0, 2, 8, 2) };
            AutomationProperties.SetName(names[a], $"Name of collision category {a}");
            Grid.SetRow(num, row);
            Grid.SetRow(names[a], row);
            Grid.SetColumn(names[a], 1);
            grid.Children.Add(num);
            grid.Children.Add(names[a]);
            for (int b = 0; b <= a; b++)
            {
                var c = new CheckBox { IsChecked = s.Collides(a, b), Margin = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Center };
                AutomationProperties.SetName(c, $"Category {a} collides with category {b}");
                checks[a, b] = c;
                Grid.SetRow(c, row);
                Grid.SetColumn(c, b + 2);
                grid.Children.Add(c);
            }
        }

        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var win = new Window
        {
            Title = $"Project settings “{p.Name}”", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(12), Children = { grid, buttons } },
        };

        ok.Click += (_, _) =>
        {
            bool wOk = int.TryParse(tw.Text, out int w) && w is >= 16 and <= 7680;
            bool hOk = int.TryParse(th.Text, out int h) && h is >= 16 and <= 4320;
            bool tOk = int.TryParse(tile.Text, out int t) && t is >= 1 and <= 256;
            Ui.MarkInvalid(tw, wOk ? null : "From 16 to 7680.");
            Ui.MarkInvalid(th, hOk ? null : "From 16 to 4320.");
            Ui.MarkInvalid(tile, tOk ? null : "From 1 to 256.");
            if (!wOk || !hOk || !tOk) return;
            (s.TargetWidth, s.TargetHeight, s.TileSize) = (w, h, t);
            for (int a = 0; a < Categories; a++)
            {
                string n = names[a].Text.Trim();
                if (n.Length > 0) s.CollisionCategories[a] = n;
                else s.CollisionCategories.Remove(a);
                uint mask = ~0u << Categories;   // categories the UI does not show keep colliding
                for (int b = 0; b < Categories; b++)
                    if (checks[Math.Max(a, b), Math.Min(a, b)].IsChecked == true) mask |= 1u << b;
                if (mask == uint.MaxValue) s.CollisionMasks.Remove(a);
                else s.CollisionMasks[a] = mask;
            }
            try { p.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return; }
            App.NotifyProjectChanged();
            win.Close();
        };
        win.ShowDialog();
    }
}
