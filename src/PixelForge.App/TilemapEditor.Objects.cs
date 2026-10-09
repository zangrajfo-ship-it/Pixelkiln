using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Object layer: points and boxes in map pixels (spawns, triggers, collisions).</summary>
public partial class TilemapEditor
{
    MapObject? _selObj;
    bool _objCreating, _objMoving, _objSnapshotPending;
    (int X, int Y) _objStart, _objGrab;

    static readonly Brush ObjFill = Frozen(Color.FromArgb(0x33, 0, 0xFF, 0xFF)), LabelBack = Frozen(Color.FromArgb(0xA0, 0, 0, 0));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    (int X, int Y) PixelAt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        return ((int)Math.Floor(p.X / _zoom), (int)Math.Floor(p.Y / _zoom));
    }

    /// <summary>Shift snaps to the tile grid.</summary>
    int Snap(int v) => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? (int)Math.Round((double)v / _map.TileSize) * _map.TileSize : v;

    MapObject? HitObject(int x, int y)
    {
        double r = 6.0 / _zoom + 1;
        for (int i = _map.Objects.Count - 1; i >= 0; i--)
        {
            var o = _map.Objects[i];
            bool hit = o.IsPoint
                ? Math.Abs(x - o.X) <= r && Math.Abs(y - o.Y) <= r
                : x >= o.X && y >= o.Y && x < o.X + Math.Max(1, o.W) && y < o.Y + Math.Max(1, o.H);
            if (hit) return o;
        }
        return null;
    }

    void ObjectDown((int X, int Y) p, bool right)
    {
        var hit = HitObject(p.X, p.Y);
        if (right)
        {
            if (hit is null) return;
            SelectObject(hit);
            OnDeleteObject(null, null);
            return;
        }
        if (hit is not null)
        {
            SelectObject(hit);
            (_objMoving, _objSnapshotPending, _objGrab) = (true, true, (p.X - hit.X, p.Y - hit.Y));
            return;
        }
        Snapshot();
        var o = new MapObject { Id = _map.NextObjectId, X = Snap(p.X), Y = Snap(p.Y) };
        o.Name = $"Object {o.Id}";
        _map.Objects.Add(o);
        (_objCreating, _objStart) = (true, (o.X, o.Y));
        SelectObject(o);
    }

    void ObjectMove((int X, int Y) p)
    {
        if (_selObj is not { } o) return;
        if (_objMoving)
        {
            if (_objSnapshotPending) { Snapshot(); _objSnapshotPending = false; }   // only a real move is an edit
            (o.X, o.Y) = (Snap(p.X - _objGrab.X), Snap(p.Y - _objGrab.Y));
        }
        else if (_objCreating)
        {
            int x = Snap(p.X), y = Snap(p.Y);
            (o.X, o.Y, o.W, o.H) = (Math.Min(x, _objStart.X), Math.Min(y, _objStart.Y), Math.Abs(x - _objStart.X), Math.Abs(y - _objStart.Y));
        }
        RefreshObjects();
        ShowObjectPanel();
    }

    void ObjectUp()
    {
        if (_objCreating && _selObj is { } o && o.W < 2 && o.H < 2)
            (o.X, o.Y, o.W, o.H) = (_objStart.X, _objStart.Y, 0, 0);   // a click makes a point
        _objCreating = _objMoving = false;
        RefreshObjects();
        ShowObjectPanel();
    }

    void SelectObject(MapObject? o)
    {
        _selObj = o;
        RefreshObjects();
        ShowObjectPanel();
    }

    void ShowObjectPanel()
    {
        ObjectPanel.Visibility = _selObj is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selObj is not { } o) return;
        ObjNameBox.Text = o.Name;
        ObjTypeBox.Text = o.Type;
        ObjInfo.Text = o.IsPoint ? $"point at {o.X}, {o.Y}" : $"{o.X}, {o.Y}   {o.W}×{o.H} px";
    }

    void OnObjectFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnObjectFieldCommit(sender, e);
    }

    void OnObjectFieldCommit(object sender, RoutedEventArgs e)
    {
        if (_selObj is not { } o) return;
        string name = ObjNameBox.Text.Trim(), type = ObjTypeBox.Text.Trim();
        if (name == o.Name && type == o.Type) return;
        Snapshot();
        (o.Name, o.Type) = (name, type);
        RefreshObjects();
    }

    void OnDeleteObject(object? sender, RoutedEventArgs? e)
    {
        if (_selObj is not { } o) return;
        Snapshot();
        _map.Objects.Remove(o);
        SelectObject(null);
    }

    void RefreshObjects()
    {
        ObjectsLayer.Children.Clear();
        foreach (var o in _map.Objects)
        {
            bool sel = o == _selObj;
            var stroke = sel ? Brushes.Yellow : Brushes.Cyan;
            Shape shape = o.IsPoint
                ? new Ellipse { Width = 10, Height = 10, Fill = stroke, Stroke = Brushes.Black }
                : new Rectangle { Width = Math.Max(1, o.W * _zoom), Height = Math.Max(1, o.H * _zoom), Stroke = stroke, StrokeThickness = sel ? 2 : 1, Fill = ObjFill };
            Canvas.SetLeft(shape, o.X * _zoom - (o.IsPoint ? 5 : 0));
            Canvas.SetTop(shape, o.Y * _zoom - (o.IsPoint ? 5 : 0));
            ObjectsLayer.Children.Add(shape);

            if (o.Name.Length == 0) continue;
            var label = new TextBlock { Text = o.Name, Foreground = Brushes.White, Background = LabelBack, FontSize = 10, Padding = new Thickness(2, 0, 2, 0) };
            Canvas.SetLeft(label, o.X * _zoom + (o.IsPoint ? 7 : 2));
            Canvas.SetTop(label, o.Y * _zoom + (o.IsPoint ? -7 : 2));
            ObjectsLayer.Children.Add(label);
        }
    }
}
