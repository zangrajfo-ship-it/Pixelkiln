using System.Text.Json;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

public class TagTests
{
    static Sprite SixFrames()
    {
        var s = new Sprite(2, 2);
        for (int i = 1; i < 6; i++) s.AddFrame(i);
        s.Tags.Add(new FrameTag { Name = "idle", From = 0, To = 1 });
        s.Tags.Add(new FrameTag { Name = "walk", From = 2, To = 5 });
        return s;
    }

    [Fact]
    public void Tags_follow_inserted_and_removed_frames()
    {
        var s = SixFrames();
        s.AddFrame(3);                                   // inside walk
        Assert.Equal((2, 6), (s.Tags[1].From, s.Tags[1].To));
        Assert.Equal((0, 1), (s.Tags[0].From, s.Tags[0].To));
        s.AddFrame(0);                                   // before everything
        Assert.Equal((1, 2), (s.Tags[0].From, s.Tags[0].To));
        s.RemoveFrame(1);
        s.RemoveFrame(1);                                // idle had frames 1 and 2 -> gone
        Assert.Single(s.Tags);
        Assert.Equal("walk", s.Tags[0].Name);
        Assert.Equal((1, 5), (s.Tags[0].From, s.Tags[0].To));
    }

    [Fact]
    public void Tags_round_trip_clone_and_v1_loads()
    {
        var s = SixFrames();
        var t = ProjectFile.Deserialize(ProjectFile.Serialize(s));
        Assert.Equal(["idle", "walk"], t.Tags.Select(x => x.Name));
        Assert.Equal(5, t.Tags[1].To);
        var c = s.Clone();
        c.Tags[0].Name = "changed";
        Assert.Equal("idle", s.Tags[0].Name);

        var v1 = ProjectFile.Serialize(new Sprite(1, 1)).Replace("\"Version\": 2", "\"Version\": 1").Replace(",\n  \"Tags\": []", "");
        Assert.Empty(ProjectFile.Deserialize(v1).Tags);
    }

    [Fact]
    public void Sheet_json_has_frame_tags()
    {
        var s = SixFrames();
        var json = JsonDocument.Parse(SpriteSheet.Json(s, SpriteSheet.Build(s), "a.png", "a"));
        var tags = json.RootElement.GetProperty("meta").GetProperty("frameTags");
        Assert.Equal(2, tags.GetArrayLength());
        Assert.Equal("walk", tags[1].GetProperty("name").GetString());
        Assert.Equal(2, tags[1].GetProperty("from").GetInt32());
        Assert.Equal(5, tags[1].GetProperty("to").GetInt32());
    }
}
