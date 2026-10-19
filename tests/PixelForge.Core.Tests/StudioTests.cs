using System.Text.Json;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class CharacterTests
{
    [Fact]
    public void Template_adds_missing_states_once_and_maps_them()
    {
        var s = new Sprite(8, 8);
        s.AddFrame(1);
        s.Tags.Add(new FrameTag { Name = "idle", From = 0, To = 1 });
        var c = new Character();
        var added = c.ApplyTemplate(s);
        Assert.Equal(Character.StandardStates.Skip(1), added);   // idle existed
        Assert.Equal(2 + 6, s.Frames.Count);
        Assert.Equal((0, 1), c.FramesOf("idle", s));
        Assert.Equal((2, 2), c.FramesOf("walk", s));
        Assert.Empty(c.ApplyTemplate(s));                           // second time: nothing new
        Assert.Null(c.FramesOf("swim", s));
    }

    [Fact]
    public void Boxes_by_state_and_frame_and_round_trip()
    {
        var c = new Character { Kind = CharacterKind.Enemy, SpriteAssetId = Guid.NewGuid(), OriginX = 4, OriginY = 15, Facing = Facing.Left };
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hurt, X = 1, Y = 1, W = 6, H = 14 });                       // always
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hit, State = "attack", Frame = 1, X = 6, Y = 4, W = 4, H = 3, Category = 2 });
        c.Properties["hp"] = "3";
        Assert.Single(c.BoxesAt("attack", 0));
        Assert.Equal(2, c.BoxesAt("attack", 1).Count());
        Assert.Single(c.BoxesAt("walk", 1));

        var t = Character.Deserialize(Character.Serialize(c));
        Assert.Equal((CharacterKind.Enemy, 4, 15, Facing.Left, c.SpriteAssetId), (t.Kind, t.OriginX, t.OriginY, t.Facing, t.SpriteAssetId));
        Assert.Equal(BoxKind.Hit, t.Boxes[1].Kind);
        Assert.Equal("3", t.Properties["hp"]);
        Assert.Contains("\"Hit\"", Character.Serialize(c));          // enums as text: readable for engines
        Assert.Throws<InvalidDataException>(() => Character.Deserialize("""{"Version":1,"Boxes":[{"W":0,"H":5}]}"""));
    }
}

public class KeyframeTests
{
    [Fact]
    public void Interpolates_eases_steps_and_loops()
    {
        var t = new KeyframeTrack { Keys = [new Keyframe(0, X: 0, Opacity: 1), new Keyframe(1000, X: 100, Opacity: 0)] };
        Assert.Equal(50, t.Sample(500).X, 3);
        Assert.Equal(0.5, t.Sample(500).Opacity, 3);
        Assert.Equal(50, t.Sample(1500).X, 3);                          // loops
        t.Loop = false;
        Assert.Equal(100, t.Sample(1500).X, 3);                         // holds the last pose
        t.Keys[0] = t.Keys[0] with { Easing = Easing.EaseIn };
        Assert.Equal(25, t.Sample(500).X, 3);
        t.Keys[0] = t.Keys[0] with { Easing = Easing.Step };
        Assert.Equal(0, t.Sample(999).X, 3);
        Assert.Equal(0, new KeyframeTrack().Sample(5).X);
    }
}

public class PaletteToolTests
{
    [Fact]
    public void Out_of_palette_snap_replace_and_ramp()
    {
        var s = new Sprite(2, 2);
        uint[] cel = s.Cel(0, 0);
        (cel[0], cel[1], cel[2]) = (0xFF000000, 0xFF101010, 0x80F0F0F0);   // black (in), near-black (out), half-transparent near-white (out)
        uint[] pal = [0xFF000000, 0xFFFFFFFF];
        var off = PaletteTools.OutOfPalette(s, pal);
        Assert.Equal(2, off.Count);
        Assert.Equal(2, PaletteTools.SnapToPalette(s, pal));
        Assert.Equal([0xFF000000u, 0xFF000000, 0x80FFFFFF, 0], cel);    // alpha kept
        Assert.Empty(PaletteTools.OutOfPalette(s, pal));
        Assert.Equal(2, PaletteTools.Replace(s, 0xFF000000, 0xFFFF0000));

        var ramp = PaletteTools.Ramp(0xFF3060C0, 5);
        Assert.Equal(5, ramp.Count);
        Assert.Equal(0xFF3060C0u, ramp[2], (a, b) => Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) <= 2);
        Assert.True(ColorUtil.ToHsv(ramp[0]).V < ColorUtil.ToHsv(ramp[4]).V);
    }
}

public class TilesetToolTests
{
    const uint R = 0xFFFF0000, B = 0xFF0000FF;

    [Fact]
    public void Duplicates_are_found_removed_and_map_remapped()
    {
        // 4 tiles of 1 px: R B R B  -> keep R(0) B(1); 2→0, 3→1
        var m = new Tilemap(4, 1, 1) { Tileset = new Tileset([R, B, R, B], 4, 1, 1) };
        int[] start = [3, 2, 1, -1];
        start.CopyTo(m.Layers[0].Tiles, 0);
        m.Terrains.Add(new TerrainRule());
        Assert.Equal([[0, 2], [1, 3]], TilesetTools.Duplicates(m.Tileset));
        Assert.Equal(2, TilesetTools.RemoveDuplicates(m));
        Assert.Equal(2, m.Tileset!.Count);
        Assert.Equal([1, 0, 1, -1], m.Layers[0].Tiles);
        Assert.Empty(m.Terrains);
        Assert.Equal(0, TilesetTools.RemoveDuplicates(m));
    }

    [Fact]
    public void Repeat_preview_tiles_the_block()
    {
        var ts = new Tileset([R, B], 2, 1, 1);
        var (px, w, h) = TilesetTools.Repeat(ts, 1, 0, 1, 1, 3);
        Assert.Equal((3, 3), (w, h));
        Assert.All(px, p => Assert.Equal(B, p));
    }
}

public class Autotile47Tests
{
    [Fact]
    public void There_are_47_masks_and_corners_need_both_edges()
    {
        Assert.Equal(47, Autotile.Masks47.Length);
        Assert.Equal(Autotile.N | Autotile.E, Autotile.Reduce(Autotile.N | Autotile.E | Autotile.NW));
        Assert.Equal(255, Autotile.Reduce(255));
        Assert.Equal(8 * 8 * 8 * 6, Autotile.Template(8, TerrainMode.Blob47).Length);
    }

    [Fact]
    public void Blob47_picks_corner_tiles_and_stable_variants()
    {
        // tileset 10 columns x 6 rows of 1 px tiles; block at 0
        var m = new Tilemap(5, 5, 1) { Tileset = new Tileset(new uint[10 * 6], 10, 6, 1) };
        var rule = new TerrainRule { Mode = TerrainMode.Blob47, Variants = [8, 9] };
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) Autotile.Apply(m, m.Layers[0], rule, x, y, on: true);
        var ts = m.Tileset;
        int full = Autotile.TileFor(ts, rule, 255);
        int center = m.Layers[0].Tiles[2 * 5 + 2];
        Assert.True(center == full || rule.Variants.Contains(center));  // fully surrounded: full tile or a variant
        Assert.Equal(Autotile.TileFor(ts, rule, Autotile.E | Autotile.S | Autotile.SE), m.Layers[0].Tiles[0]);   // top-left corner
        var before = (int[])m.Layers[0].Tiles.Clone();
        Autotile.Apply(m, m.Layers[0], rule, 0, 0, on: true);          // repaint: variants do not reshuffle
        Assert.Equal(before, m.Layers[0].Tiles);

        Autotile.Apply(m, m.Layers[0], rule, 1, 1, on: false);         // hole: the cell SE of it loses its NW corner
        Assert.Equal(Autotile.TileFor(ts, rule, 255 & ~Autotile.NW), m.Layers[0].Tiles[2 * 5 + 2]);
        Assert.Throws<InvalidOperationException>(() => Autotile.Validate(new Tileset(new uint[4 * 4], 4, 4, 1), rule));
    }

    [Fact]
    public void Terrain_rules_export_lists_every_mask()
    {
        var m = new Tilemap(1, 1, 1) { Tileset = new Tileset(new uint[8 * 6], 8, 6, 1) };
        m.Terrains.Add(new TerrainRule { Mode = TerrainMode.Blob47 });
        m.Terrains.Add(new TerrainRule());
        var root = JsonDocument.Parse(TerrainExport.Json(m)).RootElement;
        Assert.Equal(47, root.GetProperty("terrains")[0].GetProperty("tiles").EnumerateObject().Count());
        Assert.Equal(16, root.GetProperty("terrains")[1].GetProperty("tiles").EnumerateObject().Count());
    }
}

public class CharacterExportTests
{
    [Fact]
    public void States_frames_and_boxes_relative_to_origin()
    {
        var s = new Sprite(16, 16);
        s.AddFrame(1);
        s.Frames[1].DurationMs = 80;
        s.Tags.Add(new FrameTag { Name = "attack", From = 0, To = 1 });
        var c = new Character { OriginX = 8, OriginY = 16 };
        c.Animations["attack"] = "attack";
        c.Animations["missing"] = "nope";                                   // no such tag: left out
        c.Boxes.Add(new CharacterBox { Kind = BoxKind.Hit, State = "attack", Frame = 1, X = 12, Y = 6, W = 4, H = 4 });
        var root = JsonDocument.Parse(CharacterExport.Json(c, s, "hero", "hero.png")).RootElement;
        var states = root.GetProperty("states");
        Assert.False(states.TryGetProperty("missing", out _));
        var frames = states.GetProperty("attack").GetProperty("frames");
        Assert.Equal(80, frames[1].GetProperty("duration").GetInt32());
        Assert.Equal(0, frames[0].GetProperty("boxes").GetArrayLength());
        var box = frames[1].GetProperty("boxes")[0];
        Assert.Equal((4, -10), (box.GetProperty("x").GetInt32(), box.GetProperty("y").GetInt32()));
    }
}
