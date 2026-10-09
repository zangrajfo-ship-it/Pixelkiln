using System.Windows;
using System.Windows.Media;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Canvas layout: tiled mode (3×3 repeat + wrap-around drawing), tile grid, preview window.</summary>
public partial class SpriteEditor
{
    bool Tiled => TiledToggle.IsChecked == true;
    PreviewWindow? _preview;

    void OnTiledToggle(object sender, RoutedEventArgs e)
    {
        CommitFloat();
        _sel = null;   // selections don't wrap
        ApplyZoom();
    }

    /// <summary>Sizes the canvas; in tiled mode the real canvas (CanvasCore) sits in the middle of a 3×3 repeat.</summary>
    void LayoutCanvas()
    {
        double cw = _sprite.Width * _zoom, ch = _sprite.Height * _zoom;
        CanvasCore.Width = cw;
        CanvasCore.Height = ch;
        CanvasHost.Width = Tiled ? cw * 3 : cw;
        CanvasHost.Height = Tiled ? ch * 3 : ch;
        TiledChecker.Visibility = TiledBg.Visibility = Tiled ? Visibility.Visible : Visibility.Collapsed;
        TiledChecker.Fill = Tiled ? Ui.CheckerBrush() : null;
        UpdateTiledBg();

        var s = App.Settings;
        TileGridOverlay.Fill = TileGridToggle.IsChecked == true
            ? Ui.GridBrush(s.TileGridWidth * _zoom, s.TileGridHeight * _zoom, Ui.ColorOf(s.TileGridColor), s.TileGridOffsetX * _zoom, s.TileGridOffsetY * _zoom)
            : null;
    }

    void UpdateTiledBg()
    {
        if (!Tiled || _bmp is null) { TiledBg.Fill = null; return; }
        TiledBg.Fill = new ImageBrush(_bmp)   // the live bitmap, so the repeat updates while drawing
        {
            TileMode = TileMode.Tile, Stretch = Stretch.Fill,
            Viewport = new Rect(0, 0, _sprite.Width * _zoom, _sprite.Height * _zoom), ViewportUnits = BrushMappingMode.Absolute,
        };
    }

    void AskTileGrid()
    {
        var s = App.Settings;
        string current = $"{s.TileGridWidth}x{s.TileGridHeight}, {s.TileGridOffsetX}, {s.TileGridOffsetY}";
        if (Ui.Ask(this, "Tile grid", "Tile size, then optional offset: W x H, X, Y (e.g. 16x16 or 16x16, 0, 8)", current) is not string text) return;
        var n = text.Split(['x', 'X', '×', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(p => int.TryParse(p, out int v) ? v : -1).ToArray();
        if (n.Length is not (2 or 4) || n.Any(v => v < 0) || n[0] < 1 || n[1] < 1)
        {
            Ui.Error(new FormatException("Enter the size as W x H, optionally followed by the X and Y offset, e.g. 16x16, 0, 8."));
            return;
        }
        var c = Settings.Deserialize(s.Serialize());
        (c.TileGridWidth, c.TileGridHeight) = (n[0], n[1]);
        (c.TileGridOffsetX, c.TileGridOffsetY) = n.Length == 4 ? (n[2], n[3]) : (0, 0);
        TileGridToggle.IsChecked = true;
        App.UpdateSettings(c);   // redraws via SettingsChanged
    }

    void OnPreview(object sender, RoutedEventArgs e) => TogglePreview();

    void TogglePreview()
    {
        if (_preview is { IsVisible: true }) { _preview.Close(); return; }
        _preview = new PreviewWindow(() => _sprite, () => PlayRange) { Owner = Window.GetWindow(this) };
        _preview.Show();
        _preview.ShowFrame(Compositor.Flatten(_sprite, _frame), _sprite.Width, _sprite.Height);
    }
}
