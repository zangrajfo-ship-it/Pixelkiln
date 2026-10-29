using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Layer opacity, replace color, sprite sheet import, scaling; below: copy/paste style and tool choice for the workspace commands.</summary>
public partial class SpriteEditor
{
    bool _refreshingOpacity;

    void RefreshOpacity()
    {
        _refreshingOpacity = true;
        OpacitySlider.Value = Math.Round(_sprite.Layers[_layer].Opacity * 100);
        OpacityValue.Text = $"{OpacitySlider.Value:0} %";
        _refreshingOpacity = false;
    }

    void OnOpacityDragStart(object sender, MouseButtonEventArgs e) => Snapshot();   // one undo step per drag

    void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OpacityValue.Text = $"{OpacitySlider.Value:0} %";
        if (_refreshingOpacity) return;
        _sprite.Layers[_layer].Opacity = OpacitySlider.Value / 100;
        _dirty = true;
        UpdateTitle();
        Render();
    }

    /// <summary>Recolors: every pixel of the left color becomes the right color, on the current layer in all frames.</summary>
    void ReplaceColor()
    {
        Snapshot();
        int n = _sprite.Layers[_layer].Cels.Sum(cel => PixelOps.ReplaceColor(cel, _primary, _secondary));
        Render();
        RefreshFrames();
        Status.Text = $"Replaced {n} pixels of {Ui.ColorName(_primary)} with {Ui.ColorName(_secondary)} on layer “{_sprite.Layers[_layer].Name}”.";
    }

    void ImportSheet()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.gif;*.bmp)|*.png;*.gif;*.bmp|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (px, w, h) = ImageIO.LoadImage(dlg.FileName);
            if (Ui.AskSize(this, $"Frame size ({w}×{h} sheet)", Math.Min(App.Settings.DefaultSpriteWidth, w), Math.Min(App.Settings.DefaultSpriteHeight, h), anchor: false)
                is not (int fw, int fh, _, _)) return;
            LoadSprite(SpriteSheet.Import(px, w, h, fw, fh), null);
            Status.Text = $"Imported {_sprite.Frames.Count} frames of {fw}×{fh}.";
        }
        catch (Exception ex) { Ui.Error(ex); }
    }

    /// <summary>Five shades of the left color (darker ones shift toward blue, lighter toward yellow) added to the palette.</summary>
    void AddRamp()
    {
        Snapshot();
        foreach (var c in PaletteTools.Ramp(_primary)) if (!_sprite.Palette.Contains(c)) _sprite.Palette.Add(c);
        RefreshPalette();
    }

    /// <summary>Seeded, mirrored sprite from the palette, drawn into the current cel (undoable).</summary>
    void GenerateSprite()
    {
        if (Ui.AskFields(this, "Generate sprite", ("_Seed (number or word)", Random.Shared.Next(100000).ToString(), null), ("_Density", "0.45", ["0.3", "0.38", "0.45", "0.52", "0.6"])) is not { } f) return;
        GenerateSprite(f[0], double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    public void GenerateSprite(string seed, double density)
    {
        try
        {
            var px = Procedural.Sprite(Rng.Seed(seed), _sprite.Width, _sprite.Height, _sprite.Palette, density);
            CommitFloat();
            Snapshot();
            Array.Copy(px, _sprite.Cel(_layer, _frame), px.Length);
            Render();
            RefreshFrames();
            Status.Text = $"Generated from seed “{seed}” (same seed, size and palette give the same sprite).";
        }
        catch (ArgumentException ex) { Ui.Error(ex); }
    }

    void SnapToPalette()
    {
        CommitFloat();
        Snapshot();
        int n = PaletteTools.SnapToPalette(_sprite, _sprite.Palette);
        Render();
        RefreshFrames();
        Status.Text = n == 0 ? "Every pixel already uses a palette color." : $"Moved {n} pixel(s) to the nearest palette color (Ctrl+Z undoes).";
    }

    void ShowOffPalette()
    {
        var off = PaletteTools.OutOfPalette(_sprite, _sprite.Palette);
        MessageBox.Show(Window.GetWindow(this), off.Count == 0 ? "All colors are in the palette."
            : $"{off.Count} color(s) outside the palette, {off.Values.Sum()} pixel(s):\n" + string.Join("\n", off.OrderByDescending(kv => kv.Value).Take(15).Select(kv => $"{Ui.ColorName(kv.Key)}: {kv.Value} px")),
            "Palette check");
    }

    void Scale(double factor)
    {
        CommitFloat();
        int w = Math.Max(1, (int)Math.Round(_sprite.Width * factor)), h = Math.Max(1, (int)Math.Round(_sprite.Height * factor));
        if (w > 4096 || h > 4096) { Ui.Error(new InvalidOperationException($"{w}×{h} is too big; sprites can be at most 4096×4096.")); return; }
        Snapshot();
        _sprite = PixelOps.Scaled(_sprite, w, h);
        (_sel, _selMask) = (null, null);
        RefreshAll();
    }
}

public partial class SpriteEditor
{
    sealed record PixelStyle(List<uint> Palette, uint Primary, uint Secondary);

    public object? CopyStyle() => new PixelStyle([.. _sprite.Palette], _primary, _secondary);

    public string? PasteStyle(object style)
    {
        if (style is not PixelStyle s) return null;
        SetPalette(s.Palette);
        (_primary, _secondary) = (s.Primary, s.Secondary);
        RefreshSwatches();
        return $"Palette ({s.Palette.Count} colors) and left/right colors pasted.";
    }

    public string CurrentToolName => CurrentTool.ToString();

    public void SelectTool(string name)
    {
        if (Enum.TryParse<Tool>(name, true, out var t)) SetTool(t);
    }
}
