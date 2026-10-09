using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Layer opacity, replace color, sprite sheet import, scaling.</summary>
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
