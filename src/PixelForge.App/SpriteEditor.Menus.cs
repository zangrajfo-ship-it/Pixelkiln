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
            Ui.Item("_Import sprite sheet…", null, ImportSheet),
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
            Ui.Item("Replace left color with right _color", null, ReplaceColor),
            Ui.Menu("_Scale sprite",
                Ui.Item("_200 %", null, () => Scale(2)),
                Ui.Item("_300 %", null, () => Scale(3)),
                Ui.Item("_400 %", null, () => Scale(4)),
                Ui.Item("_50 %", null, () => Scale(0.5)),
                Ui.Item("25 %", null, () => Scale(0.25))),
            Ui.Item("Canvas si_ze…", null, Do(OnCanvasSize)),
            Ui.Item("Crop to _selection", null, Do(OnCrop)),
            Ui.Item("_Generate sprite from seed…", null, GenerateSprite),
            new Separator(),
            Ui.Menu("Pa_lette",
                Ui.Item("_Load… (.hex, .gpl)", null, Do(OnLoadPalette)),
                Ui.Item("_Save…", null, Do(OnSavePalette)),
                Ui.Item("_Reset to DB32", null, Do(OnResetPalette)),
                new Separator(),
                Ui.Item("Add shading _ramp from left color", null, AddRamp),
                Ui.Item("_Snap sprite to palette", null, SnapToPalette),
                Ui.Item("Show colors _outside the palette", null, ShowOffPalette))),
        Ui.Menu("_View",
            _aids.Menu(),
            new Separator(),
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit),
            new Separator(),
            Ui.Toggle("Pixel _grid", "Ctrl+G", GridToggle),
            Ui.Toggle("_Onion skin", null, OnionToggle),
            Ui.Toggle("_Tiled mode (seamless tiles)", "T", TiledToggle),
            Ui.Toggle("Ti_le grid", "Ctrl+Shift+G", TileGridToggle),
            Ui.Item("Tile grid si_ze…", null, AskTileGrid),
            Ui.Item("_Preview window", "P", TogglePreview),
            new Separator(),
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
        [Tool.Wand] = "Click to select pixels of one color; Shift+click adds. Move them with Select (M).",
    };

    static readonly Dictionary<Tool, (string Icon, string Name, string Key)> ToolLooks = new()
    {
        [Tool.Pencil] = ("pencil", "Pencil", "B"), [Tool.Eraser] = ("eraser", "Eraser", "E"), [Tool.Fill] = ("bucket", "Paint bucket", "G"),
        [Tool.Picker] = ("picker", "Eyedropper", "I"), [Tool.Line] = ("line", "Line", "L"), [Tool.Rect] = ("rect", "Rectangle", "U"),
        [Tool.Ellipse] = ("ellipse", "Ellipse", "O"), [Tool.Select] = ("marquee", "Rectangular marquee", "M"), [Tool.Wand] = ("wand", "Magic wand", "W"),
    };

    /// <summary>The context bar: the current tool's icon, name and key, then only the options it uses.</summary>
    void UpdateToolOptions()
    {
        var t = CurrentTool;
        (ToolIcon.Kind, ToolName.Text, ToolKey.Text) = ToolLooks[t];
        static Visibility V(bool show) => show ? Visibility.Visible : Visibility.Collapsed;
        SizeOption.Visibility = V(t is Tool.Pencil or Tool.Eraser or Tool.Line);
        FilledToggle.Visibility = V(t is Tool.Rect or Tool.Ellipse);
        PixelPerfectToggle.Visibility = V(t is Tool.Pencil);
        ContiguousToggle.Visibility = V(t is Tool.Wand);
        MirrorOption.Visibility = V(t is Tool.Pencil or Tool.Eraser or Tool.Line or Tool.Rect or Tool.Ellipse);
        // tools without options get a sentence instead
        ToolHint.Text = t is Tool.Fill or Tool.Picker or Tool.Select or Tool.Wand or Tool.Line or Tool.Rect or Tool.Ellipse ? ToolHints[t] : "";
        ToolHint.Visibility = V(ToolHint.Text.Length > 0);
    }
}
