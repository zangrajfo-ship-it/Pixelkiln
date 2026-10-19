using System.Diagnostics;
using PixelForge.Core;

namespace PixelForge.Core.Tests;

/// <summary>Each test works in its own temporary folder.</summary>
public sealed class ProjectTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "pf-proj-" + Guid.NewGuid().ToString("N"));

    public ProjectTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string P(params string[] parts) => Path.Combine([_dir, .. parts]);

    string NewSprite(string rel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        ProjectFile.Save(new Sprite(4, 4), P(rel));
        return P(rel);
    }

    [Fact]
    public void Create_add_save_load_round_trip()
    {
        var p = Project.Create(_dir, "Game");
        var a = p.Add(NewSprite("hero.pxf"));
        a.Tags.Add("player");
        a.Favorite = true;
        p.Save();

        var q = Project.Load(P("Game.pfproj"));
        Assert.Equal(p.Id, q.Id);
        var b = Assert.Single(q.Assets);
        Assert.Equal((a.Id, AssetType.Sprite, "hero.pxf", "hero"), (b.Id, b.Type, b.Path, b.Name));
        Assert.True(b.Favorite);
        Assert.Equal(["player"], b.Tags);
        Assert.Same(b, q.Add(P("hero.pxf")));                         // adding twice returns the same entry
        Assert.Throws<IOException>(() => Project.Create(_dir, "Game"));
    }

    [Fact]
    public void Rename_and_move_keep_the_id_and_the_file()
    {
        var p = Project.Create(_dir, "Game");
        var a = p.Add(NewSprite("hero.pxf"));
        p.Rename(a.Id, "player");
        Assert.True(File.Exists(P("player.pxf")));
        Assert.False(File.Exists(P("hero.pxf")));
        p.Move(a.Id, Path.Combine("chars", "good"));
        Assert.Equal(Path.Combine("chars", "good", "player.pxf"), p.Find(a.Id)!.Path);
        Assert.Equal(P("chars", "good", "player.pxf"), p.Resolve(a.Id));
        Assert.Equal(a.Id, Project.Load(P("Game.pfproj")).Assets.Single().Id);

        var b = p.Add(NewSprite("other.pxf"));
        p.Rename(b.Id, "x");
        p.Rename(b.Id, "x");                                         // same name: nothing to do
        Assert.True(File.Exists(P("x.pxf")));
        Assert.Throws<ArgumentException>(() => p.Rename(b.Id, "bad/name"));
        Assert.Throws<ArgumentException>(() => p.Move(b.Id, ".."));
    }

    [Fact]
    public void Rename_onto_existing_file_is_refused()
    {
        var p = Project.Create(_dir, "Game");
        var a = p.Add(NewSprite("a.pxf"));
        p.Add(NewSprite("b.pxf"));
        Assert.Throws<IOException>(() => p.Rename(a.Id, "b"));
        Assert.True(File.Exists(P("a.pxf")));
    }

    [Fact]
    public void Remove_moves_file_to_trash_and_duplicate_gets_new_id()
    {
        var p = Project.Create(_dir, "Game");
        var a = p.Add(NewSprite("hero.pxf"));
        var copy = p.Duplicate(a.Id);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal("hero copy.pxf", copy.Path);
        Assert.Equal("hero copy 2.pxf", p.Duplicate(a.Id).Path);

        p.Remove(a.Id);
        Assert.Null(p.Find(a.Id));
        Assert.False(File.Exists(P("hero.pxf")));
        Assert.Single(Directory.GetFiles(P(".trash")));            // recoverable, not deleted
    }

    [Fact]
    public void Health_missing_unregistered_damaged_broken_and_users()
    {
        var p = Project.Create(_dir, "Game");
        var tiles = p.Add(NewSprite("tiles.pxf"));
        var gone = p.Add(NewSprite("gone.pxf"));
        File.Delete(P("gone.pxf"));
        NewSprite("loose.pxf");
        File.WriteAllText(P("bad.pxs"), "{ not json");
        var bad = p.Add(P("bad.pxs"));
        Directory.CreateDirectory(P(".trash"));
        NewSprite(Path.Combine(".trash", "old.pxf"));                // hidden folders are ignored

        var map = new Tilemap(2, 2, 4) { TilesetAssetId = tiles.Id };
        Tilemap.Save(map, P("level.pxm"));
        var level = p.Add(P("level.pxm"));
        var broken = new Tilemap(2, 2, 4) { TilesetAssetId = Guid.NewGuid() };
        Tilemap.Save(broken, P("broken.pxm"));
        var brokenEntry = p.Add(P("broken.pxm"));

        Assert.Equal([gone.Id], p.MissingAssets().Select(a => a.Id));
        Assert.Equal([P("loose.pxf")], p.UnregisteredFiles());
        var damaged = Assert.Single(p.DamagedAssets());
        Assert.Equal(bad.Id, damaged.Asset.Id);
        Assert.Contains("bad.pxs", damaged.Problem);
        Assert.Equal([level.Id], p.UsersOf(tiles.Id).Select(a => a.Id));
        Assert.Equal([brokenEntry.Id], p.BrokenReferences().Select(b => b.From.Id));
    }

    [Fact]
    public void Create_from_folder_registers_existing_files_unchanged()
    {
        NewSprite("a.pxf");
        NewSprite(Path.Combine("sub", "b.pxf"));
        Song.Save(new Song(), P("music.pxs"));
        byte[] before = File.ReadAllBytes(P("a.pxf"));
        var p = Project.CreateFromFolder(_dir, "Game");
        Assert.Equal(3, p.Assets.Count);
        Assert.Equal(before, File.ReadAllBytes(P("a.pxf")));
    }

    [Fact]
    public void Bad_manifests_are_rejected_with_reasons()
    {
        var p = Project.Create(_dir, "Game");
        var a = p.Add(NewSprite("a.pxf"));
        string json = File.ReadAllText(p.FilePath);

        File.WriteAllText(p.FilePath, json.Replace("\"a.pxf\"", "\"..\\\\outside.pxf\""));
        Assert.Contains("outside", Assert.Throws<InvalidDataException>(() => Project.Load(p.FilePath)).Message);

        p.Assets.Add(new AssetEntry { Id = a.Id, Type = AssetType.Sprite, Path = "b.pxf" });
        p.Save();
        Assert.Contains("more than once", Assert.Throws<InvalidDataException>(() => Project.Load(p.FilePath)).Message);

        File.WriteAllText(p.FilePath, "{\"Version\": 99}");
        Assert.Contains("newer", Assert.Throws<InvalidDataException>(() => Project.Load(p.FilePath)).Message);
        Assert.Throws<InvalidOperationException>(() => p.Add(Path.Combine(Path.GetTempPath(), "elsewhere.pxf")));
    }

    [Fact]
    public void Tileset_comes_from_asset_or_falls_back_to_embedded_copy()
    {
        var p = Project.Create(_dir, "Game");
        var s = new Sprite(4, 2);
        s.Cel(0, 0)[0] = 0xFFFF0000;
        ProjectFile.Save(s, P("tiles.pxf"));
        var tiles = p.Add(P("tiles.pxf"));

        var map = new Tilemap(1, 1, 2) { TilesetAssetId = tiles.Id, Tileset = new Tileset(new uint[4], 2, 2, 2) };
        var reloaded = Tilemap.Deserialize(Tilemap.Serialize(map));
        Assert.Equal(tiles.Id, reloaded.TilesetAssetId);
        Assert.True(AssetLoader.RefreshTileset(reloaded, p));
        Assert.Equal((4, 0xFFFF0000u), (reloaded.Tileset!.Width, reloaded.Tileset.Pixels[0]));

        p.Remove(tiles.Id);
        var offline = Tilemap.Deserialize(Tilemap.Serialize(map));
        Assert.False(AssetLoader.RefreshTileset(offline, p));
        Assert.Equal(2, offline.Tileset!.Width);                      // embedded copy kept
    }

    [Fact]
    [Trait("Category", "Load")]
    public void Search_over_5000_assets_is_fast()
    {
        var p = Project.Create(_dir, "Big");
        var rnd = new Random(1);
        for (int i = 0; i < 5000; i++)
            p.Assets.Add(new AssetEntry { Type = (AssetType)(i % 5), Path = Path.Combine($"folder{i % 50}", $"asset{i}.pxf"), Tags = [$"tag{rnd.Next(100)}"], Favorite = i % 97 == 0 });
        p.Search("asset4").ToList();                                 // warm up
        var sw = Stopwatch.StartNew();
        var hits = p.Search("folder7 asset4").ToList();
        var tagged = p.Search("tag42", AssetType.Sprite).ToList();
        sw.Stop();
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Contains("asset4", h.Path));
        Assert.All(tagged, t => Assert.Equal(AssetType.Sprite, t.Type));
        Assert.True(sw.ElapsedMilliseconds < 100, $"search took {sw.ElapsedMilliseconds} ms");
    }
}
