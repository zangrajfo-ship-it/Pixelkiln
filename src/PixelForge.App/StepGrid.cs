using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PixelForge.Core;
using SongT = PixelForge.Core.Song;

namespace PixelForge.App;

/// <summary>Piano-roll for one pattern: rows = notes (high at top), columns = steps, volume lane below.</summary>
sealed class StepGrid : FrameworkElement
{
    public const double CellW = 28, CellH = 14, LabelW = 40, VolGap = 8, VolH = 64;

    public static readonly Brush[] ChannelBrushes =
        [Frozen(0xFFE8833A), Frozen(0xFFE8C73A), Frozen(0xFF4FA3E8), Frozen(0xFFB0B0B0)];

    static readonly Brush WhiteRow = Frozen(0xFF3A3A3A), BlackRow = Frozen(0xFF2E2E2E), Label = Frozen(0xFFBBBBBB),
        PlayheadBrush = Frozen(0x40FFFFFF), LaneBrush = Frozen(0xFF262626), EmptyVol = Frozen(0xFF444444);
    static readonly Pen Thin = FrozenPen(0xFF262626), Beat = FrozenPen(0xFF555555);
    static readonly Typeface Font = new("Segoe UI");

    public Song? Song { get; set; }
    public int Pattern { get; set; }
    public int Channel { get; set; }
    public int Playhead { get; set; } = -1;
    /// <summary>Keyboard cursor (step, note); drawn while the grid has keyboard focus.</summary>
    public (int Step, int Note) KeyCursor { get; set; } = (0, 60);

    static readonly Pen CursorPen = FrozenPen(0xFF3B82F6, 2);

    public Rect CellRect(int step, int note) => new(LabelW + step * CellW, RowY(note), CellW, CellH);

    protected override void OnGotKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    static int Rows => SongT.MaxNote - SongT.MinNote + 1;
    static double NotesH => Rows * CellH;
    static double LaneTop => NotesH + VolGap;
    static double RowY(int note) => (SongT.MaxNote - note) * CellH;

    public double YOf(int note) => RowY(note);

    public int StepAt(double x) => Song is null ? 0 : Math.Clamp((int)((x - LabelW) / CellW), 0, Song.Steps - 1);

    public (int Step, int Note)? Hit(Point p)
    {
        if (Song is null || p.X < LabelW || p.Y >= NotesH) return null;
        int step = (int)((p.X - LabelW) / CellW), note = SongT.MaxNote - (int)(p.Y / CellH);
        return step < Song.Steps && note >= SongT.MinNote && note <= SongT.MaxNote ? (step, note) : null;
    }

    public bool InVolumeLane(Point p) => Song is not null && p.X >= LabelW && p.Y >= LaneTop && p.Y < LaneTop + VolH;

    public int VolumeAt(double y) => (int)Math.Round(Math.Clamp(1 - (y - LaneTop) / VolH, 0, 1) * Core.Pattern.MaxVolume);

    protected override Size MeasureOverride(Size available) =>
        new(LabelW + (Song?.Steps ?? 16) * CellW, LaneTop + VolH);

    protected override void OnRender(DrawingContext dc)
    {
        if (Song is null) return;
        double w = LabelW + Song.Steps * CellW;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int n = SongT.MinNote; n <= SongT.MaxNote; n++)
        {
            bool black = (n % 12) is 1 or 3 or 6 or 8 or 10;
            double y = RowY(n);
            dc.DrawRectangle(black ? BlackRow : WhiteRow, Thin, new Rect(LabelW, y, w - LabelW, CellH));
            dc.DrawText(Text(SongT.NoteName(n), n % 12 == 0 ? Brushes.White : Label, dpi), new Point(4, y));
        }
        dc.DrawRectangle(LaneBrush, null, new Rect(LabelW, LaneTop, w - LabelW, VolH));
        dc.DrawText(Text("Vol", Label, dpi), new Point(4, LaneTop));
        for (int s = 0; s <= Song.Steps; s += 4)
            dc.DrawLine(Beat, new Point(LabelW + s * CellW, 0), new Point(LabelW + s * CellW, LaneTop + VolH));

        if (Playhead >= 0)
            dc.DrawRectangle(PlayheadBrush, null, new Rect(LabelW + Playhead * CellW, 0, CellW, LaneTop + VolH));

        var pat = Song.Patterns[Pattern];
        for (int ch = 0; ch < SongT.Channels; ch++)
        {
            if (ch == Channel) continue;
            dc.PushOpacity(0.3);
            DrawNotes(dc, pat, ch, ChannelBrushes[ch], inset: 4);
            dc.Pop();
        }
        DrawNotes(dc, pat, Channel, ChannelBrushes[Channel], inset: 1);
        if (IsKeyboardFocused) dc.DrawRectangle(null, CursorPen, CellRect(KeyCursor.Step, KeyCursor.Note));

        // volume bars for the current channel, where notes start
        for (int s = 0; s < Song.Steps; s++)
        {
            bool starts = pat.Notes[Channel][s] >= 0;
            double h = starts ? pat.Volumes[Channel][s] / (double)Core.Pattern.MaxVolume * (VolH - 4) : 2;
            dc.DrawRectangle(starts ? ChannelBrushes[Channel] : EmptyVol, null, new Rect(LabelW + s * CellW + 4, LaneTop + VolH - 2 - h, CellW - 8, h));
        }
    }

    static void DrawNotes(DrawingContext dc, Core.Pattern pat, int ch, Brush brush, double inset)
    {
        var notes = pat.Notes[ch];
        for (int s = 0; s < notes.Length; s++)
        {
            if (notes[s] < 0 || notes[s] < SongT.MinNote || notes[s] > SongT.MaxNote) continue;
            int len = pat.Length(ch, s);
            dc.DrawRectangle(brush, null, new Rect(LabelW + s * CellW + inset, RowY(notes[s]) + inset, len * CellW - 2 * inset, CellH - 2 * inset));
        }
    }

    FormattedText Text(string s, Brush b, double dpi) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, b, dpi);

    static SolidColorBrush Frozen(uint c)
    {
        var b = Ui.BrushOf(c);
        b.Freeze();
        return b;
    }

    static Pen FrozenPen(uint c, double thickness = 1)
    {
        var p = new Pen(Frozen(c), thickness);
        p.Freeze();
        return p;
    }
}
