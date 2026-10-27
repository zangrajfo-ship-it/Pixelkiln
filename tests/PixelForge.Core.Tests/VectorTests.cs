using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class VectorTests() : TempDirTest("vec")
{
    static VShape Rect(double x, double y, double w, double h, uint c = 0xFFFF0000) =>
        new() { Kind = VShapeKind.Rect, X = x, Y = y, W = w, H = h, Fill = VPaint.Solid(c) };

    static T Sta<T>(Func<T> f) => Core.Sta.Run(f);

    [Fact]
    public void Round_trip_keeps_shapes_paints_and_groups()
    {
        var d = new VectorDoc { Width = 32, Height = 16 };
        d.Shapes.Add(Rect(1, 2, 3, 4));
        d.Shapes.Add(new VShape
        {
            Kind = VShapeKind.Group, Name = "g",
            Children = [VectorGeometry.Polygon(8, 8, 4, 5, star: true), new VShape { Kind = VShapeKind.Ellipse, X = 0, Y = 0, W = 4, H = 4,
                Fill = new VPaint(VPaintKind.Linear, 0, [new VStop(0, 0xFF000000), new VStop(1, 0x80FFFFFF)], 0, 0, 4, 0) }],
        });
        string f = P("a.pxvec");
        VectorDoc.Save(d, f);
        var e = VectorDoc.Load(f);
        Assert.Equal((32.0, 16.0, 2), (e.Width, e.Height, e.Shapes.Count));
        Assert.Equal(10, e.Shapes[1].Children[0].Contours[0].Nodes.Count);
        Assert.Equal(0x80FFFFFFu, e.Shapes[1].Children[1].Fill!.Stops![1].Color);
        Assert.Throws<InvalidDataException>(() => VectorDoc.Deserialize(VectorDoc.Serialize(d).Replace(d.Shapes[1].Id.ToString(), d.Shapes[0].Id.ToString())));
    }

    [Fact]
    public void Boolean_operations_give_the_expected_areas()
    {
        Sta(() =>
        {
            var a = Rect(0, 0, 10, 10);
            var b = Rect(5, 0, 10, 10);
            double Area(VShape s) => VectorGeometry.Of(s).GetArea(0.01, System.Windows.Media.ToleranceType.Absolute);
            Assert.Equal(150, Area(VectorGeometry.Combine([a, b], VectorGeometry.BoolOp.Union)), 1);
            Assert.Equal(50, Area(VectorGeometry.Combine([a, b], VectorGeometry.BoolOp.Intersect)), 1);
            Assert.Equal(50, Area(VectorGeometry.Combine([a, b], VectorGeometry.BoolOp.Subtract)), 1);
            Assert.Equal(100, Area(VectorGeometry.Combine([a, b], VectorGeometry.BoolOp.Exclude)), 1);
            var hole = VectorGeometry.Combine([Rect(0, 0, 10, 10), Rect(3, 3, 4, 4)], VectorGeometry.BoolOp.Subtract);
            Assert.Equal(2, hole.Contours.Count);                         // outline and hole
            Assert.Equal(84, Area(hole), 1);
            Assert.Equal(0xFFFF0000u, hole.Fill!.Color);                  // keeps the first shape's look
            return 0;
        });
    }

    [Fact]
    public void Align_distribute_move_and_scale()
    {
        var s = new List<VShape> { Rect(0, 0, 2, 2), Rect(10, 5, 2, 2), Rect(3, 9, 2, 2) };
        VectorGeometry.AlignShapes(s, VectorGeometry.Align.Left);
        Assert.All(s, x => Assert.Equal(0, x.X));
        VectorGeometry.AlignShapes(s, VectorGeometry.Align.Bottom);
        Assert.All(s, x => Assert.Equal(9, x.Y));
        var row = new List<VShape> { Rect(0, 0, 2, 2), Rect(3, 0, 2, 2), Rect(20, 0, 2, 2) };
        VectorGeometry.Distribute(row, horizontal: true);
        Assert.Equal(10, row[1].X, 6);                                   // widths 6 in a span of 22: two gaps of 8
        var p = VectorGeometry.Polygon(10, 10, 5, 4);
        VectorGeometry.Move(p, 1, 2);
        Assert.Equal((11.0, 7.0), (p.Contours[0].Nodes[0].X, p.Contours[0].Nodes[0].Y));
        VectorGeometry.Scale(p, 11, 12, 2, 2);
        Assert.Equal(2.0, p.Contours[0].Nodes[0].Y);                     // 12 + (7 - 12) * 2
    }

    [Fact]
    public void Svg_import_reads_the_common_subset_and_reports_the_rest()
    {
        const string svg = """
            <?xml version="1.0"?>
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10" viewBox="0 0 40 20">
              <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#000"/><stop offset="100%" stop-color="#fff" stop-opacity="0.5"/></linearGradient></defs>
              <rect x="0" y="0" width="40" height="20" fill="url(#g)"/>
              <g transform="translate(10 0)" style="fill: rgb(255,0,0); stroke: blue; stroke-width: 2">
                <circle cx="5" cy="5" r="5"/>
                <path d="M0 10 L10 10 Q15 15 20 10 A5 5 0 0 1 30 10 Z" fill-rule="evenodd" opacity="0.5"/>
              </g>
              <text x="1" y="1">hi</text>
              <image href="x.png"/>
            </svg>
            """;
        var r = Sta(() => Svg.Import(svg));
        Assert.Equal((20.0, 10.0), (r.Doc.Width, r.Doc.Height));
        Assert.Equal(2, r.Doc.Shapes.Count);
        var bg = r.Doc.Shapes[0];
        Assert.Equal(VPaintKind.Linear, bg.Fill!.Kind);
        Assert.Equal((0.0, 20.0), (bg.Fill.X1, bg.Fill.X2));                // object bounding box mapped to document units
        Assert.Equal(0x80FFFFFFu, bg.Fill.Stops![1].Color);
        var group = r.Doc.Shapes[1];
        Assert.Equal(VShapeKind.Group, group.Kind);
        var circle = group.Children[0];
        Assert.Equal(0xFFFF0000u, circle.Fill!.Color);
        Assert.Equal(0xFF0000FFu, circle.Stroke!.Color);
        Assert.Equal(1, circle.StrokeWidth, 3);                            // 2 px in a half-size view box
        var b = VectorGeometry.Bounds(circle);
        Assert.Equal((5.0, 0.0, 5.0, 5.0), (Math.Round(b.X, 2), Math.Round(b.Y, 2), Math.Round(b.Width, 2), Math.Round(b.Height, 2)));
        Assert.True(group.Children[1].EvenOdd);
        Assert.Equal(0.5, group.Children[1].Opacity);
        Assert.Contains("<text>", r.Skipped);
        Assert.Contains("<image>", r.Skipped);
        Assert.Throws<InvalidDataException>(() => Svg.Import("<html/>"));
        Assert.Throws<InvalidDataException>(() => Svg.Import("""<!DOCTYPE svg [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><svg>&x;</svg>"""));
    }

    [Fact]
    public void Svg_export_imports_back_to_the_same_geometry()
    {
        var d = new VectorDoc { Width = 50, Height = 30 };
        d.Shapes.Add(Rect(5, 5, 20, 10));
        d.Shapes[0].Radius = 2;
        d.Shapes.Add(new VShape { Kind = VShapeKind.Ellipse, X = 30, Y = 5, W = 10, H = 20, Stroke = VPaint.Solid(0xFF00FF00), StrokeWidth = 3,
            Fill = new VPaint(VPaintKind.Radial, 0, [new VStop(0, 0xFFFFFFFF), new VStop(1, 0xFF000000)], 35, 15, 0, 0, 10) });
        d.Shapes.Add(VectorGeometry.Polygon(10, 22, 6, 6));
        d.Shapes[2].Fill = VPaint.Solid(0x800000FF);
        string svg = Svg.Export(d);
        Assert.Contains("<radialGradient", svg);
        var back = Sta(() => Svg.Import(svg)).Doc;
        Assert.Equal(3, back.Shapes.Count);
        Sta(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                var a = VectorGeometry.Bounds(d.Shapes[i]);
                var b = VectorGeometry.Bounds(back.Shapes[i]);
                Assert.Equal(a.X, b.X, 1); Assert.Equal(a.Width, b.Width, 1); Assert.Equal(a.Height, b.Height, 1);
            }
            return 0;
        });
        Assert.Equal(0x800000FFu, back.Shapes[2].Fill!.Color);
        Assert.Equal(VPaintKind.Radial, back.Shapes[1].Fill!.Kind);
    }

    [Fact]
    public void Rasterize_scales_and_can_be_aliased_for_pixel_art()
    {
        var d = new VectorDoc { Width = 10, Height = 10 };
        d.Shapes.Add(new VShape { Kind = VShapeKind.Ellipse, X = 0, Y = 0, W = 10, H = 10, Fill = VPaint.Solid(0xFFFF0000) });
        var smooth = VectorGeometry.Rasterize(d, 40, 40);
        var crisp = VectorGeometry.Rasterize(d, 40, 40, aliased: true);
        Assert.Equal(0xFFFF0000u, smooth[20 * 40 + 20]);
        Assert.Equal(0u, smooth[0]);
        Assert.Contains(smooth, p => p >> 24 is > 0 and < 255);           // soft edge
        Assert.DoesNotContain(crisp, p => p >> 24 is > 0 and < 255);      // no half-transparent pixels
        var p = Project.Create(_dir, "g");
        VectorDoc.Save(d, P("v.pxvec"));
        var a = p.Add(P("v.pxvec"));
        Assert.Equal(AssetType.Vector, a.Type);
        Assert.NotNull(AssetKinds.Of(AssetType.Vector).Picture!(p.FullPath(a), p));
    }

    [Fact]
    public void Paintings_and_drawings_work_as_pictures_in_scenes_and_exports()
    {
        var p = Project.Create(_dir, "g");
        var v = new VectorDoc { Width = 8, Height = 4 };
        v.Shapes.Add(Rect(0, 0, 8, 4, 0xFF00FF00));
        VectorDoc.Save(v, P("sky.pxvec"));
        var d = new PaintDoc(6, 3);
        d.Layers[0].TileFor(0, 0)[0] = 0xFFFF0000;
        PaintDoc.Save(d, P("hills.pxpaint"));
        var sky = p.Add(P("sky.pxvec"));
        var hills = p.Add(P("hills.pxpaint"));
        Assert.True(AssetLoader.IsPicture(sky.Type) && AssetLoader.IsPicture(hills.Type));
        Assert.Equal((8, 4), AssetLoader.LoadImage(p.FullPath(sky)) is var (_, w, h) ? (w, h) : default);

        var s = new Scene { Width = 16, Height = 16 };
        s.Layers.Insert(0, new SceneLayer { Kind = SceneLayerKind.Image, AssetId = sky.Id });
        s.Layers.Insert(1, new SceneLayer { Kind = SceneLayerKind.Image, AssetId = hills.Id, OffsetX = 10 });
        var px = SceneRenderer.Render(s, new SceneAssets(p), 0, 0, 16, 8, 0);
        Assert.Equal(0xFF00FF00u, px[1]);
        Assert.Equal(0xFFFF0000u, px[10]);
        Scene.Save(s, P("level.pxscene"));
        p.Add(P("level.pxscene"));

        var godot = Exporter.Plan(p, new ExportProfile());
        Assert.True(godot.Ok, string.Join("; ", godot.Errors));
        Assert.Contains("sky.png", godot.Files.Keys);
        Assert.Contains("sky.svg", godot.Files.Keys);
        Assert.Contains("hills.png", godot.Files.Keys);
        var generic = Exporter.Plan(p, new ExportProfile { Target = ExportTarget.Generic, OutputFolder = "export/g" });
        Assert.Contains("\"hills.png\"", generic.Text("level_scene.json"));
    }
}
