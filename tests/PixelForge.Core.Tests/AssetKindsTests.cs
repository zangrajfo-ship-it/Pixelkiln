using PixelForge.Core;

namespace PixelForge.Core.Tests;

public sealed class AssetKindsTests() : TempDirTest("kinds")
{
    [Fact]
    public void Every_type_is_registered_once_with_its_extensions()
    {
        Assert.Equal(Enum.GetValues<AssetType>().Order(), AssetKinds.All.Select(k => k.Type).Order());
        Assert.Equal(AssetType.Scene, Project.TypesByExtension[".PXSCENE"]);
        Assert.Null(AssetKinds.ForExtension(".txt"));
        Assert.Throws<InvalidOperationException>(() => AssetKinds.Register(AssetKinds.Of(AssetType.Sprite)));
    }

    [Fact]
    public void Replacing_an_asset_updates_every_user_playlists_and_profiles()
    {
        var p = Project.Create(_dir, "G");
        ProjectFile.Save(new Sprite(8, 8), P("a.pxf"));
        ProjectFile.Save(new Sprite(16, 16), P("b.pxf"));
        var a = p.Add(P("a.pxf"));
        var b = p.Add(P("b.pxf"));
        Character.Save(new Character { SpriteAssetId = a.Id }, P("hero.pxchar"));
        var hero = p.Add(P("hero.pxchar"));
        var s = new Scene();
        s.Instances.Add(new SceneInstance { AssetId = a.Id, LayerId = s.Layers[0].Id });
        s.Instances.Add(new SceneInstance { AssetId = hero.Id, LayerId = s.Layers[0].Id });
        Scene.Save(s, P("level.pxscene"));
        var level = p.Add(P("level.pxscene"));
        WavWriter.Save(P("x.wav"), new float[10]);
        var wav = p.Add(P("x.wav"));
        p.Settings.ExportProfiles.Add(new ExportProfile { Assets = [a.Id, b.Id] });

        var changed = p.ReplaceReferences(a.Id, b.Id);
        Assert.Equal(new[] { hero.Id, level.Id }.Order(), changed.Select(x => x.Id).Order());
        Assert.Equal(b.Id, Character.Load(P("hero.pxchar")).SpriteAssetId);
        Assert.Equal([b.Id, hero.Id], Scene.Load(P("level.pxscene")).Instances.Select(i => i.AssetId));
        Assert.Equal([b.Id], p.Settings.ExportProfiles[0].Assets!);
        Assert.Empty(p.UsersOf(a.Id));
        Assert.Throws<InvalidOperationException>(() => p.ReplaceReferences(b.Id, wav.Id));   // a sprite cannot become a sound
    }
}
