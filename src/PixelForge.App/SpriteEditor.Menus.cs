using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelForge.App;

/// <summary>Menus, zoom controls and the context-sensitive tool options strip.</summary>
public partial class SpriteEditor : IEditor
{
    IReadOnlyList<MenuItem>? _menus;

    Action Do(RoutedEventHandler handler) => () => handler(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("Export _PNG…", null, Do(OnExportPng)),
            Ui.Item("Export animated _GIF…", null, Do(OnExportGif)),
            Ui.Item("Export sprite _sheet…", null, Do(OnExportSheet)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("Cu_t", "Ctrl+X", Cut),
            Ui.Item("_Copy", "Ctrl+C", Copy),
            Ui.Item("_Paste", "Ctrl+V", Paste),
            Ui.Item("_Delete", "Del", DeleteSelection),
            Ui.Item("Select _all", "Ctrl+A", SelectAll),
            new Separator(),
            Ui.Item("Flip _horizontally", "H", () => TransformFloat(Core.PixelOps.FlipH)),
            Ui.Item("Flip _vertically", "V", () => TransformFloat(Core.PixelOps.FlipV)),
            Ui.Item("R_otate 90°", "R", () => TransformFloat(Core.PixelOps.Rotate90, swapsSize: true)),
            new Separator(),
            Ui.Item("Canvas si_ze…", null, Do(OnCanvasSize)),
            Ui.Item("Crop to _selection", null, Do(OnCrop)),
            new Separator(),
            Ui.Menu("Pa_lette",
                Ui.Item("_Load… (.hex, .gpl)", null, Do(OnLoadPalette)),
                Ui.Item("_Save…", null, Do(OnSavePalette)),
                Ui.Item("_Reset to DB32", null, Do(OnResetPalette)))),
        Ui.Menu("_View",
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit),
            new Separator(),
            Ui.Toggle("Pixel _grid", "Ctrl+G", GridToggle),
            Ui.Toggle("_Onion skin", null, OnionToggle),
            Ui.Toggle("Mirror _X", null, MirrorXToggle),
            Ui.Toggle("Mirror _Y", null, MirrorYToggle)),
        Ui.Menu("_Layer",
            Ui.Item("_Add layer", null, Do(OnAddLayer)),
            Ui.Item("_Delete layer", null, Do(OnDeleteLayer)),
            Ui.Item("Move _up", null, Do(OnLayerUp)),
            Ui.Item("Move d_own", null, Do(OnLayerDown))),
        Ui.Menu("F_rame",
            Ui.Item("_New frame", null, Do(OnAddFrame)),
            Ui.Item("_Duplicate frame", null, Do(OnDuplicateFrame)),
            Ui.Item("De_lete frame", null, Do(OnDeleteFrame)),
            Ui.Item("Move _left", null, Do(OnFrameLeft)),
            Ui.Item("Move _right", null, Do(OnFrameRight)),
            new Separator(),
            Ui.Item("_Previous frame", "←", () => { StopPlaying(); SelectFrame((_frame + _sprite.Frames.Count - 1) % _sprite.Frames.Count); }),
            Ui.Item("Ne_xt frame", "→", () => { StopPlaying(); SelectFrame((_frame + 1) % _sprite.Frames.Count); }),
            Ui.Item("_Play / stop", "Space", () => OnPlay(null, null))),
    ];

    // ---------- zoom ----------

    void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1);
    void OnZoomFit(object sender, RoutedEventArgs e) => ZoomFit();

    /// <summary>One zoom level up/down, keeping the middle of the view in place.</summary>
    void ZoomStep(int dir)
    {
        int i = Array.IndexOf(ZoomLevels, _zoom) + dir;
        if (i < 0 || i >= ZoomLevels.Length) return;
        var center = Scroller.TranslatePoint(new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2), CanvasHost);
        ZoomTo(ZoomLevels[i], center);
    }

    /// <summary>Biggest zoom at which the whole sprite is visible.</summary>
    void ZoomFit()
    {
        double w = Scroller.ViewportWidth - 80, h = Scroller.ViewportHeight - 80;
        int z = ZoomLevels.LastOrDefault(l => _sprite.Width * l <= w && _sprite.Height * l <= h, 1);
        ZoomTo(z, new Point(0, 0));
    }

    // ---------- tool options strip ----------

    static readonly Dictionary<Tool, string> ToolHints = new()
    {
        [Tool.Pencil] = "Left and right mouse paint with the two colors.",
        [Tool.Eraser] = "Paints transparent pixels.",
        [Tool.Fill] = "Fills the touching area of the same color.",
        [Tool.Picker] = "Click a pixel to take its color (left or right mouse).",
        [Tool.Line] = "Drag to draw. Shift: 0°, 45° or 90°.",
        [Tool.Rect] = "Drag to draw. Shift: square.",
        [Tool.Ellipse] = "Drag to draw. Shift: circle.",
        [Tool.Select] = "Drag to select, drag inside to move. H/V flip, R rotate, Enter apply, Esc cancel.",
    };

    /// <summary>Shows only the options the current tool uses (like LibreSprite's context bar).</summary>
    void UpdateToolOptions()
    {
        var t = CurrentTool;
        ToolHint.Text = ToolHints[t];
        static Visibility V(bool show) => show ? Visibility.Visible : Visibility.Collapsed;
        SizeOption.Visibility = V(t is Tool.Pencil or Tool.Eraser or Tool.Line);
        FilledToggle.Visibility = V(t is Tool.Rect or Tool.Ellipse);
        MirrorXToggle.Visibility = MirrorYToggle.Visibility = V(t is Tool.Pencil or Tool.Eraser or Tool.Line or Tool.Rect or Tool.Ellipse);
    }
}
