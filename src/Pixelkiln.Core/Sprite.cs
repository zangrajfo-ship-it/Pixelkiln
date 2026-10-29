namespace Pixelkiln.Core;

/// <summary>What the layers panel needs; shared by sprite and map layers.</summary>
public interface ILayer
{
    string Name { get; set; }
    bool Visible { get; set; }
}

/// <summary>Pixels are ARGB uint (0xAARRGGBB), row-major. 0 = transparent.</summary>
public sealed class Layer : ILayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    /// <summary>0..1, applied when layers are flattened.</summary>
    public double Opacity { get; set; } = 1;
    /// <summary>One cel per frame, same order as <see cref="Sprite.Frames"/>.</summary>
    public List<uint[]> Cels { get; } = [];
}

public sealed class Frame
{
    public int DurationMs { get; set; } = 100;
}

/// <summary>Named frame range for game animations (walk, idle …). From/To are inclusive frame indices.</summary>
public sealed class FrameTag
{
    public string Name { get; set; } = "tag";
    public int From { get; set; }
    public int To { get; set; }
    public FrameTag Clone() => (FrameTag)MemberwiseClone();
}

/// <summary>Layers[0] is the bottom layer.</summary>
public sealed class Sprite
{
    public int Width { get; }
    public int Height { get; }
    public List<uint> Palette { get; } = [.. DefaultPalette];
    public List<Layer> Layers { get; } = [];
    public List<Frame> Frames { get; } = [];
    public List<FrameTag> Tags { get; } = [];

    public Sprite(int width, int height, bool empty = false)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Size must be 1..4096.");
        Width = width;
        Height = height;
        if (empty) return;
        Frames.Add(new Frame());
        AddLayer(0, "Layer 1");
    }

    public uint[] Cel(int layer, int frame) => Layers[layer].Cels[frame];

    public Layer AddLayer(int index, string name)
    {
        var layer = new Layer { Name = name };
        foreach (var _ in Frames) layer.Cels.Add(new uint[Width * Height]);
        Layers.Insert(index, layer);
        return layer;
    }

    public void RemoveLayer(int index)
    {
        if (Layers.Count > 1) Layers.RemoveAt(index);
    }

    public void MoveLayer(int from, int to)
    {
        if (to >= 0 && to < Layers.Count) Move(Layers, from, to);
    }

    /// <summary>Inserts a frame at index; copies pixels from <paramref name="copyFrom"/> or starts blank.</summary>
    public void AddFrame(int index, int? copyFrom = null)
    {
        Frames.Insert(index, new Frame { DurationMs = copyFrom is int c ? Frames[c].DurationMs : 100 });
        foreach (var l in Layers)
            l.Cels.Insert(index, copyFrom is int f ? (uint[])l.Cels[f].Clone() : new uint[Width * Height]);
        // frames after the insert point move right; inserting inside a tag makes it longer
        foreach (var t in Tags)
        {
            if (t.From >= index) t.From++;
            if (t.To >= index) t.To++;
        }
    }

    public void RemoveFrame(int index)
    {
        if (Frames.Count <= 1) return;
        Frames.RemoveAt(index);
        foreach (var l in Layers) l.Cels.RemoveAt(index);
        foreach (var t in Tags)
        {
            if (t.From > index) t.From--;
            if (t.To >= index) t.To--;
        }
        Tags.RemoveAll(t => t.To < t.From);   // its only frame was removed
    }

    public void MoveFrame(int from, int to)
    {
        if (to < 0 || to >= Frames.Count) return;
        Move(Frames, from, to);
        foreach (var l in Layers) Move(l.Cels, from, to);
    }

    static void Move<T>(List<T> list, int from, int to)
    {
        var x = list[from];
        list.RemoveAt(from);
        list.Insert(to, x);
    }

    public Sprite Clone()
    {
        var s = new Sprite(Width, Height, empty: true);
        s.Palette.Clear();
        s.Palette.AddRange(Palette);
        foreach (var f in Frames) s.Frames.Add(new Frame { DurationMs = f.DurationMs });
        foreach (var l in Layers)
        {
            var nl = new Layer { Name = l.Name, Visible = l.Visible, Opacity = l.Opacity };
            foreach (var c in l.Cels) nl.Cels.Add((uint[])c.Clone());
            s.Layers.Add(nl);
        }
        s.Tags.AddRange(Tags.Select(t => t.Clone()));
        return s;
    }

    /// <summary>DawnBringer 32.</summary>
    public static readonly uint[] DefaultPalette =
    [
        0xFF000000, 0xFF222034, 0xFF45283C, 0xFF663931, 0xFF8F563B, 0xFFDF7126, 0xFFD9A066, 0xFFEEC39A,
        0xFFFBF236, 0xFF99E550, 0xFF6ABE30, 0xFF37946E, 0xFF4B692F, 0xFF524B24, 0xFF323C39, 0xFF3F3F74,
        0xFF306082, 0xFF5B6EE1, 0xFF639BFF, 0xFF5FCDE4, 0xFFCBDBFC, 0xFFFFFFFF, 0xFF9BADB7, 0xFF847E87,
        0xFF696A6A, 0xFF595652, 0xFF76428A, 0xFFAC3232, 0xFFD95763, 0xFFD77BBA, 0xFF8F974A, 0xFF8A6F30,
    ];
}
