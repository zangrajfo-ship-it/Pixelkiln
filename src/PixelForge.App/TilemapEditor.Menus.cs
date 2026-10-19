using System.Windows;
using System.Windows.Controls;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Menus, zoom controls and the tool hint.</summary>
public partial class TilemapEditor : IEditor
{
    IReadOnlyList<MenuItem>? _menus;

    Action Do(RoutedEventHandler handler) => () => handler(this, new RoutedEventArgs());

    public IReadOnlyList<MenuItem> Menus => _menus ??=
    [
        Ui.Menu("_File",
            Ui.Item("_New map…", "Ctrl+N", Do(OnNew)),
            Ui.Item("_Open…", "Ctrl+O", Do(OnOpen)),
            Ui.RecentMenu(this),
            Ui.Item("_Save", "Ctrl+S", Do(OnSave)),
            Ui.Item("Save _as…", "Ctrl+Shift+S", () => Save(null)),
            new Separator(),
            Ui.Item("Export _PNG…", null, Do(OnExportPng)),
            Ui.Item("Export for _Tiled (.tmj)…", null, Do(OnExportTiled)),
            new Separator(),
            Ui.Item("E_xit", "Alt+F4", () => Window.GetWindow(this)?.Close())),
        Ui.Menu("_Edit",
            Ui.Item("_Undo", "Ctrl+Z", Do(OnUndo)),
            Ui.Item("_Redo", "Ctrl+Y", Do(OnRedo)),
            new Separator(),
            Ui.Item("Map _size…", null, Do(OnMapSize)),
            Ui.Item("_Delete selected object", "Del", () => OnDeleteObject(null, null))),
        Ui.Menu("_Tileset",
            Ui.Item("_Load… (PNG or .pxf)", null, Do(OnLoadTileset)),
            Ui.Item("From _Sprite tab", null, Do(OnTilesetFromSprite)),
            Ui.Item("_Reload from file", null, Do(OnReloadTileset)),
            new Separator(),
            Ui.Item("_Add terrain from selection", null, Do(OnAddTerrain)),
            Ui.Item("Re_move terrain", null, Do(OnRemoveTerrain)),
            Ui.Item("Set terrain _variants from selection", null, SetTerrainVariants),
            Ui.Item("Save 16-tile _template…", null, () => SaveTemplate(TerrainMode.Blob16)),
            Ui.Item("Save 47-tile te_mplate…", null, () => SaveTemplate(TerrainMode.Blob47)),
            Ui.Item("E_xport terrain rules (JSON)…", null, ExportTerrainRules),
            new Separator(),
            Ui.Item("Find _duplicate tiles…", null, FindDuplicateTiles),
            Ui.Item("_Preview selection repeated", null, PreviewRepeat)),
        Ui.Menu("_View",
            Ui.Item("Zoom _in", "Ctrl+=", () => ZoomStep(+1)),
            Ui.Item("Zoom _out", "Ctrl+-", () => ZoomStep(-1)),
            Ui.Item("_Fit to window", "Ctrl+0", ZoomFit),
            new Separator(),
            Ui.Toggle("_Grid", "Ctrl+G", GridToggle)),
        Ui.Menu("_Layer",
            Ui.Item("_Add layer", null, Do(OnAddLayer)),
            Ui.Item("_Delete layer", null, Do(OnDeleteLayer)),
            Ui.Item("Move _up", null, Do(OnLayerUp)),
            Ui.Item("Move d_own", null, Do(OnLayerDown))),
    ];

    void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1);
    void OnZoomFit(object sender, RoutedEventArgs e) => ZoomFit();

    void ZoomStep(int dir)
    {
        int i = Array.IndexOf(ZoomLevels, _zoom) + dir;
        if (i < 0 || i >= ZoomLevels.Length) return;
        ZoomTo(ZoomLevels[i], Scroller.TranslatePoint(new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2), CanvasHost));
    }

    void ZoomFit()
    {
        double w = Scroller.ViewportWidth - 80, h = Scroller.ViewportHeight - 80;
        int px = _map.TileSize;
        ZoomTo(ZoomLevels.LastOrDefault(l => _map.Width * px * l <= w && _map.Height * px * l <= h, 1), new Point(0, 0));
    }

    void UpdateToolHint()
    {
        var t = CurrentTool;
        ToolHint.Text =
            t == PaintBtn ? "Paints the tiles selected in the tileset. Right mouse erases." :
            t == EraseBtn ? "Removes tiles from the current layer." :
            t == FillBtn ? "Fills the touching area with the selected tiles. Right mouse clears it." :
            t == PickBtn ? "Click or drag on the map to copy tiles into the brush." :
            t == AutoBtn ? "Paints the chosen terrain; edges join by themselves. Right mouse erases." :
            "Drag: box, click: point. Click an object to select or move it. Shift snaps to tiles.";
    }
}
