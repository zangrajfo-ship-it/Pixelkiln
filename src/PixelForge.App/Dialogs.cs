using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace PixelForge.App;

static class Dialogs
{
    /// <summary>One-line text prompt. Returns null on cancel.</summary>
    public static string? Ask(DependencyObject owner, string title, string label, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 6, 0, 10) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var win = new Window
        {
            Title = title, Width = 320, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(owner),
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = label }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }

    /// <summary>Windows color picker. Returns opaque ARGB or null on cancel.</summary>
    public static uint? PickColor(uint initial)
    {
        using var dlg = new WinForms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb((int)(initial | 0xFF000000)) };
        return dlg.ShowDialog() == WinForms.DialogResult.OK ? (uint)dlg.Color.ToArgb() | 0xFF000000 : null;
    }

    public static void Error(Exception ex) =>
        MessageBox.Show(ex.Message, "PixelForge", MessageBoxButton.OK, MessageBoxImage.Error);
}
