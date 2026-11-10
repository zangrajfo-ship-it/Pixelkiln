using System.Text.Json.Nodes;

namespace Pixelkiln.Core.Tests;

/// <summary>
/// A damaged or hand-edited file must end in one readable <see cref="InvalidDataException"/> (the editors show its
/// message), never in another exception that would close the app, and never in a document that breaks later.
/// </summary>
public class DamagedFileTests : TempDirTest
{
    public DamagedFileTests() : base("damaged") { }

    /// <summary>Loads a file and uses the result the way an editor does right after opening it.</summary>
    static void Open(string path)
    {
        switch (Path.GetExtension(path))
        {
            case ".pxf":
                var s = ProjectFile.Load(path);
                for (int f = 0; f < s.Frames.Count; f++)
                {
                    Compositor.Flatten(s, f);
                    Assert.InRange(s.Frames[f].DurationMs, 1, int.MaxValue);   // a timer interval: zero or less cannot be played
                }
                ProjectFile.Serialize(s);
                break;
            case ".pxm":
                var m = Tilemap.Load(path);
                m.Render();
                Tilemap.Serialize(m);
                break;
            case ".pxchar":
                Character.Serialize(Character.Load(path));
                break;
            case ".pfproj":
                Project.Load(path);
                break;
        }
    }

    /// <summary>The file with one value changed: every value in turn becomes null, a wrong type, an extreme number or goes missing.</summary>
    static IEnumerable<(string What, string Json)> Damaged(string json)
    {
        string[] replacements = ["null", "-1", "0", "2147483647", "1e99", "\"\"", "\"x\"", "[]", "{}", "true"];
        var paths = new List<string[]>();
        Walk(JsonNode.Parse(json)!, []);
        foreach (var path in paths)
        {
            foreach (string r in replacements)
            {
                var root = JsonNode.Parse(json)!;
                Set(root, path, JsonNode.Parse(r));
                yield return ($"{string.Join("/", path)} = {r}", root.ToJsonString());
            }
            var without = JsonNode.Parse(json)!;
            Set(without, path, null, remove: true);
            yield return ($"{string.Join("/", path)} removed", without.ToJsonString());
        }

        void Walk(JsonNode node, string[] at)
        {
            if (at.Length > 0) paths.Add(at);
            if (node is JsonObject o)
                foreach (var kv in o.Where(kv => kv.Value is not null)) Walk(kv.Value!, [.. at, kv.Key]);
            else if (node is JsonArray a)
                for (int i = 0; i < Math.Min(2, a.Count); i++)   // long arrays (tiles) repeat one kind of value
                    if (a[i] is { } item) Walk(item, [.. at, $"{i}"]);
        }

        static void Set(JsonNode root, string[] path, JsonNode? value, bool remove = false)
        {
            var parent = root;
            foreach (string step in path[..^1]) parent = parent is JsonArray arr ? arr[int.Parse(step)]! : parent[step]!;
            string last = path[^1];
            if (parent is JsonArray list)
            {
                if (remove) list.RemoveAt(int.Parse(last)); else list[int.Parse(last)] = value;
            }
            else if (remove) ((JsonObject)parent).Remove(last);
            else parent[last] = value;
        }
    }

    string ProjectFixture()
    {
        var p = Project.Create(P("game"), "Game");
        File.Copy(TestFiles.Fixture("sprite.v2.pxf"), P("game", "hero.pxf"));
        p.Add(P("game", "hero.pxf"));
        p.Save();
        return p.FilePath;
    }

    [Theory]
    [InlineData("sprite.v2.pxf")]
    [InlineData("current/map.v6.pxm")]
    [InlineData("current/hero.v2.pxchar")]
    [InlineData(".pfproj")]
    public void A_file_with_one_wrong_value_opens_or_is_refused_with_a_readable_message(string fixture)
    {
        string source = fixture == ".pfproj" ? ProjectFixture() : TestFiles.Fixture(fixture);
        string json = File.ReadAllText(source), file = P("damaged" + Path.GetExtension(source));
        var wrong = new List<string>();
        int refused = 0, opened = 0;
        foreach (var (what, damaged) in Damaged(json))
        {
            File.WriteAllText(file, damaged);
            try { Open(file); opened++; }
            catch (InvalidDataException ex)
            {
                Assert.Contains(Path.GetFileName(file), ex.Message);   // the message names the file
                refused++;
            }
            catch (Exception ex) { wrong.Add($"{what}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} damaged files were not handled:\n" + string.Join("\n", wrong.Take(30)));
        Assert.True(refused > 0 && opened > 0, $"refused {refused}, opened {opened}");   // the test really tried both kinds
    }

    [Theory]
    [InlineData("sprite.v2.pxf")]
    [InlineData("current/map.v6.pxm")]
    [InlineData("current/hero.v2.pxchar")]
    [InlineData(".pfproj")]
    public void A_cut_off_empty_or_foreign_file_is_refused_with_a_readable_message(string fixture)
    {
        string source = fixture == ".pfproj" ? ProjectFixture() : TestFiles.Fixture(fixture);
        string json = File.ReadAllText(source), file = P("damaged" + Path.GetExtension(source));
        var contents = new List<string> { "", " ", "null", "[]", "42", "\"text\"", "{", "not json at all", "\0\0\0\0", "﻿", "PK\u0003\u0004 zip", "{\"Version\": \"two\"}" };
        for (int i = 1; i < 20; i++) contents.Add(json[..(json.Length * i / 20)]);   // a save that stopped part of the way
        foreach (string text in contents)
        {
            File.WriteAllText(file, text);
            var ex = Assert.Throws<InvalidDataException>(() => Open(file));
            Assert.Contains(Path.GetFileName(file), ex.Message);
        }
    }

    [Fact]
    public void A_file_from_a_newer_version_says_so()
    {
        string file = P("new.pxf");
        File.WriteAllText(file, File.ReadAllText(TestFiles.Fixture("sprite.v2.pxf")).Replace("\"Version\": 2", "\"Version\": 99"));
        var ex = Assert.Throws<InvalidDataException>(() => ProjectFile.Load(file));
        Assert.Contains("newer", ex.Message);
    }
}
