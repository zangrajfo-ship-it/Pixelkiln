using System.Windows;
using System.Windows.Controls;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Options → Preferences (Ctrl+,). Builds its form in code; every field has a label with an access key.</summary>
static class PreferencesWindow
{
    public static void Show(Window owner)
    {
        var s = App.Settings;
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var theme = new ComboBox { ItemsSource = Theme.Names, SelectedItem = s.Theme };
        double[] scales = [0.75, 1, 1.25, 1.5, 1.75, 2];
        var scale = new ComboBox { ItemsSource = scales.Select(x => $"{x * 100:0} %").ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(scales, s.UiScale)) };
        var gridColor = new TextBox { Text = ColorUtil.ToHex(s.GridColor) };
        var checkLight = new TextBox { Text = ColorUtil.ToHex(s.CheckerLight) };
        var checkDark = new TextBox { Text = ColorUtil.ToHex(s.CheckerDark) };
        var checkSize = new TextBox { Text = s.CheckerSize.ToString() };
        var spriteW = new TextBox { Text = s.DefaultSpriteWidth.ToString() };
        var spriteH = new TextBox { Text = s.DefaultSpriteHeight.ToString() };
        var undo = new TextBox { Text = s.UndoLimit.ToString() };
        var autosave = new TextBox { Text = s.AutosaveMinutes.ToString() };

        (string Label, Control Field, string Help)[] rows =
        [
            ("_Theme", theme, "Modern (the redesigned dark look), dark, light, or follow Windows."),
            ("Interface _scale", scale, "Makes all text and controls bigger."),
            ("Pixel _grid color", gridColor, "AARRGGBB, e.g. #99000000 (black, 60 %)."),
            ("Checkerboard _light", checkLight, "Transparency background, light squares."),
            ("Checkerboard _dark", checkDark, "Transparency background, dark squares."),
            ("Checkerboard square si_ze", checkSize, "2 to 64 screen pixels."),
            ("New sprite _width", spriteW, "1 to 4096."),
            ("New sprite _height", spriteH, "1 to 4096."),
            ("_Undo steps", undo, "10 to 1000; applies to newly opened documents."),
            ("_Autosave every (minutes)", autosave, "0 = off. Unsaved work is offered back after a crash."),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new Label { Content = rows[i].Label, Target = rows[i].Field, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 0, 8, 0) };
            rows[i].Field.Margin = new Thickness(0, 4, 0, 4);
            rows[i].Field.ToolTip = rows[i].Help;
            System.Windows.Automation.AutomationProperties.SetHelpText(rows[i].Field, rows[i].Help);
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }

        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel } };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(buttons, rows.Length);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        var win = new Window
        {
            Title = "Preferences", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = grid,
        };
        ok.Click += (_, _) =>
        {
            var n = Settings.Deserialize(s.Serialize());   // copy, so settings not on this form are kept
            n.Theme = (string)theme.SelectedItem;
            n.UiScale = scales[scale.SelectedIndex];
            bool valid = true;
            n.GridColor = Hex(gridColor, n.GridColor);
            n.CheckerLight = Hex(checkLight, n.CheckerLight);
            n.CheckerDark = Hex(checkDark, n.CheckerDark);
            n.CheckerSize = Int(checkSize, 2, 64);
            n.DefaultSpriteWidth = Int(spriteW, 1, 4096);
            n.DefaultSpriteHeight = Int(spriteH, 1, 4096);
            n.UndoLimit = Int(undo, 10, 1000);
            n.AutosaveMinutes = Int(autosave, 0, 60);
            if (!valid) return;   // fields are marked; the window stays open
            App.UpdateSettings(n);
            win.DialogResult = true;

            uint Hex(TextBox box, uint fallback)
            {
                if (ColorUtil.TryParseHex(box.Text, out uint c)) { Ui.MarkInvalid(box, null); return c; }
                Ui.MarkInvalid(box, "Use 6 hex digits (RRGGBB) or 8 (AARRGGBB).");
                valid = false;
                return fallback;
            }

            int Int(TextBox box, int min, int max)
            {
                if (int.TryParse(box.Text, out int v) && v >= min && v <= max) { Ui.MarkInvalid(box, null); return v; }
                Ui.MarkInvalid(box, $"Enter a whole number from {min} to {max}.");
                valid = false;
                return min;
            }
        };
        win.ShowDialog();
    }
}
