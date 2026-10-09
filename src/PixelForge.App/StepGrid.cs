using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PixelForge.Core;
using SongT = PixelForge.Core.Song;

namespace PixelForge.App;

/// <summary>Piano-roll for one pattern: rows = notes (high at top), columns = steps.</summary>
sealed class StepGrid : FrameworkElement
{
    public const double CellW = 28, CellH = 14, LabelW = 40;

    public static readonly Brush[] ChannelBrushes =
        [Frozen(0xFFE8833A), Frozen(0xFFE8C73A), Frozen(0xFF4FA3E8), Frozen(0xFFB0B0B0)];

    static readonly Brush WhiteRow = Frozen(0xFF3A3A3A), BlackRow = Frozen(0xFF2E2E2E), Label = Frozen(0xFFBBBBBB), PlayheadBrush = Frozen(0x40FFFFFF);
    static readonly Pen Thin = new(Frozen(0xFF262626), 1), Beat = new(Frozen(0xFF555555), 1);
    static readonly Typeface Font = new("Segoe UI");

    public Song? Song { get; set; }
    public int Pattern { get; set; }
    public int Channel { get; set; }
    public int Playhead { get; set; } = -1;

    static int Rows => SongT.MaxNote - SongT.MinNote + 1;
    static double RowY(int note) => (SongT.MaxNote - note) * CellH;

    public (int Step, int Note)? Hit(Point p)
    {
        if (Song is null || p.X < LabelW) return null;
        int step = (int)((p.X - LabelW) / CellW), note = SongT.MaxNote - (int)(p.Y / CellH);
        return step < Song.Steps && note >= SongT.MinNote && note <= SongT.MaxNote ? (step, note) : null;
    }

    public double YOf(int note) => RowY(note);

    protected override Size MeasureOverride(Size available) =>
        new(LabelW + (Song?.Steps ?? 16) * CellW, Rows * CellH);

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
            var text = new FormattedText(SongT.NoteName(n), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10,
                n % 12 == 0 ? Brushes.White : Label, dpi);
            dc.DrawText(text, new Point(4, y));
        }
        for (int s = 0; s <= Song.Steps; s += 4)
            dc.DrawLine(Beat, new Point(LabelW + s * CellW, 0), new Point(LabelW + s * CellW, Rows * CellH));

        if (Playhead >= 0)
            dc.DrawRectangle(PlayheadBrush, null, new Rect(LabelW + Playhead * CellW, 0, CellW, Rows * CellH));

        var pat = Song.Patterns[Pattern];
        for (int ch = 0; ch < SongT.Channels; ch++)
        {
            if (ch == Channel) continue;
            dc.PushOpacity(0.3);
            DrawNotes(dc, pat.Notes[ch], ChannelBrushes[ch], inset: 4);
            dc.Pop();
        }
        DrawNotes(dc, pat.Notes[Channel], ChannelBrushes[Channel], inset: 1);
    }

    static void DrawNotes(DrawingContext dc, int[] notes, Brush brush, double inset)
    {
        for (int s = 0; s < notes.Length; s++)
            if (notes[s] != Core.Pattern.Off)
                dc.DrawRectangle(brush, null, new Rect(LabelW + s * CellW + inset, RowY(notes[s]) + inset, CellW - 2 * inset, CellH - 2 * inset));
    }

    static SolidColorBrush Frozen(uint c)
    {
        var b = Ui.BrushOf(c);
        b.Freeze();
        return b;
    }
}
