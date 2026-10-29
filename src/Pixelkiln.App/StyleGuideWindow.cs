using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Project › Style guide: the project's visual rules and a check of every sprite, picture and drawing against them.</summary>
static class StyleGuideWindow
{
    public static void Show(Window owner, Project p)
    {
        var g = p.Settings.Style.Clone();
        var palettes = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 90 };
        AutomationProperties.SetName(palettes, "Allowed palettes");
        foreach (var a in p.Assets.Where(a => a.Type == AssetType.Palette).OrderBy(a => a.Path))
        {
            var item = new ListBoxItem { Content = a.Path, Tag = a.Id };
            palettes.Items.Add(item);
            if (g.Palettes.Contains(a.Id)) palettes.SelectedItems.Add(item);
        }
        TextBox Box(string text, string name) { var t = new TextBox { Text = text, Width = 90, HorizontalAlignment = HorizontalAlignment.Left }; AutomationProperties.SetName(t, name); return t; }
        var grid = Box(g.Grid > 0 ? $"{g.Grid}" : "", "Sprite size grid");
        var colors = Box(g.MaxColors > 0 ? $"{g.MaxColors}" : "", "Most colours per sprite");
        var outline = Box(g.Outline is uint o ? ColorUtil.ToHex(o | 0xFF000000) : "", "Outline colour");
        var stroke = Box(g.VectorStroke > 0 ? g.VectorStroke.ToString(CultureInfo.InvariantCulture) : "", "Vector stroke width");
        var maxSize = Box(g.MaxSize > 0 ? $"{g.MaxSize}" : "", "Largest sprite or picture");
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, FrameworkElement input)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new Label { Content = label, Target = input, Padding = new Thickness(0, 4, 10, 4) };
            Grid.SetRow(l, form.RowDefinitions.Count - 1);
            Grid.SetRow(input, form.RowDefinitions.Count - 1);
            Grid.SetColumn(input, 1);
            input.Margin = new Thickness(0, 3, 0, 3);
            form.Children.Add(l);
            form.Children.Add(input);
        }
        Row("Allowed _palettes", palettes);
        Row("Sprite sizes are multiples of (_px)", grid);
        Row("_Most colours per sprite", colors);
        Row("_Outline colour (RRGGBB)", outline);
        Row("_Vector stroke width", stroke);
        Row("_Largest size (px)", maxSize);
        var hint = new TextBlock { Text = "Empty = not checked. New sprites start at the grid size with the first allowed palette.", Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        var report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(report, "Style check report");
        var save = new Button { Content = "Save and check", IsDefault = true, Padding = new Thickness(10, 2, 10, 2) };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { save, close } };
        var top = new StackPanel { Children = { form, hint } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var win = new Window
        {
            Title = $"Style guide “{p.Name}”", Width = 620, Height = 640, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { top, buttons, report } },
        };

        int? Whole(TextBox t, int max)
        {
            if (t.Text.Trim().Length == 0) { Ui.MarkInvalid(t, null); return 0; }
            bool ok = int.TryParse(t.Text.Trim(), out int v) && v >= 1 && v <= max;
            Ui.MarkInvalid(t, ok ? null : $"A whole number from 1 to {max}, or empty.");
            return ok ? v : null;
        }

        async void Check()
        {
            var guide = p.Settings.Style;
            if (guide.IsEmpty) { report.Text = "No rules yet: fill in at least one field."; return; }
            try
            {
                var issues = await Ui.RunLong(win, "Checking the style", (_, ct) => StyleCheck.Run(p, guide, ct));
                if (issues is null) { report.Text = "Cancelled."; return; }
                report.Text = issues.Count == 0 ? "Every asset follows the style guide." : $"{issues.Count} issue(s):\n" + string.Join("\n", issues.Select(i => $"  • {i.Asset.Path}: {i.Message}"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { Ui.Error(ex); }
        }

        save.Click += (_, _) =>
        {
            int? gr = Whole(grid, 1024), mc = Whole(colors, 256), ms = Whole(maxSize, 65536);
            uint? line = null;
            bool lineOk = outline.Text.Trim().Length == 0 || ColorUtil.TryParseHex(outline.Text.Trim(), out uint lc) && (line = lc & 0xFFFFFF) is not null;
            Ui.MarkInvalid(outline, lineOk ? null : "RRGGBB, or empty.");
            double sw = 0;
            bool strokeOk = stroke.Text.Trim().Length == 0 || double.TryParse(stroke.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out sw) && sw > 0 && sw <= 100;
            Ui.MarkInvalid(stroke, strokeOk ? null : "A width from 0.01 to 100, or empty.");
            if (gr is null || mc is null || ms is null || !lineOk || !strokeOk) return;
            p.Settings.Style = new StyleGuide
            {
                Palettes = [.. palettes.SelectedItems.Cast<ListBoxItem>().Select(i => (Guid)i.Tag)],
                Grid = gr.Value, MaxColors = mc.Value, MaxSize = ms.Value, Outline = line, VectorStroke = sw,
            };
            try { p.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Ui.Error(ex); return; }
            Check();
        };
        win.Loaded += (_, _) => { if (!p.Settings.Style.IsEmpty) Check(); };
        win.ShowDialog();
    }
}
