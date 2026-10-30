using Microsoft.Extensions.Configuration;
using MercuryManager.Server;
using Xunit;

public class AssetRegistryEditorTests
{
    private const string Sample = "/media/Vostro3/Arcade/SDFE_3.07/WindowsNoEditor/Mercury/AssetRegistry.bin";
    [Fact]
    public void RealRegistry_RoundTrips_AndEditsUnicodeWithoutChangingOtherData()
    {
        if (!File.Exists(Sample)) return;
        var bytes=File.ReadAllBytes(Sample);
        var document=AssetRegistryEditor.Read(bytes);
        Assert.Equal(8365,document.Entries.Length);
        Assert.Equal(bytes,AssetRegistryEditor.Apply(bytes,new(document.Revision,[])));
        var entry=document.Entries.First(x=>x.Tags.Length>0);
        var tag=entry.Tags[0];
        var changed=AssetRegistryEditor.Apply(bytes,new(document.Revision,[new(entry.Index,tag.Name,"测试 🚀 asset")]));
        var result=AssetRegistryEditor.Read(changed);
        Assert.Equal("测试 🚀 asset",result.Entries[entry.Index].Tags[0].Value);
        Assert.Equal(document.Entries.Length,result.Entries.Length);
        Assert.Throws<InvalidOperationException>(()=>AssetRegistryEditor.Apply(changed,new(document.Revision,[])));
    }
    [Fact]
    public void RealRegistry_RebuildsAndPerformsCrud()
    {
        if (!File.Exists(Sample)) return;
        var bytes=File.ReadAllBytes(Sample); var doc=AssetRegistryEditor.Read(bytes);
        var original=AssetRegistryEditor.Apply(bytes,new(doc.Revision,Entries:doc.Entries));
        Assert.Equal(doc.Entries.Length,AssetRegistryEditor.Read(original).Entries.Length);
        Assert.Equal(bytes,original);

        var entries=doc.Entries.ToList(); var first=entries[0];
        entries[0]=first with { PackagePath="/Game/HermesFolder", AssetClass="HermesAssetClass", Tags=[..first.Tags,new("HermesNewTag", "测试 🚀")] };
        entries.RemoveAt(1);
        entries.Add(first with { Index=-1, ObjectPath="/Game/HermesNew/HermesNew.HermesNew",PackagePath="/Game/HermesNew",PackageName="/Game/HermesNew/HermesNew",AssetName="HermesNew",Tags=[new("NewTag_12","new value")],ChunkIds=[4],PackageFlags=42 });
        var changed=AssetRegistryEditor.Apply(bytes,new(doc.Revision,Entries:entries.ToArray()));
        var read=AssetRegistryEditor.Read(changed);
        using var archive=new CUE4Parse.UE4.Readers.FByteArchive("AssetRegistry.bin",changed,new CUE4Parse.UE4.Versions.VersionContainer(CUE4Parse.UE4.Versions.EGame.GAME_UE4_19));
        var independent=new CUE4Parse.UE4.AssetRegistry.FAssetRegistryState(archive);
        Assert.Equal(entries.Count, independent.PreallocatedAssetDataBuffers.Length);
        Assert.Equal("HermesNew", independent.PreallocatedAssetDataBuffers.Last().AssetName.ToString());
        Assert.Equal("new value", independent.PreallocatedAssetDataBuffers.Last().TagsAndValues.Single().Value);
        var deleted=AssetRegistryEditor.Apply(changed,new(AssetRegistryEditor.Read(changed).Revision,Entries:read.Entries.Where(e=>e.AssetName!="HermesNew").ToArray()));
        var reread=AssetRegistryEditor.Read(deleted);
        Assert.Equal(doc.Entries.Length-1,reread.Entries.Length);
        Assert.DoesNotContain(reread.Entries,e=>e.AssetName=="HermesNew");
        Assert.Equal(doc.Entries.Length,read.Entries.Length);
        Assert.Equal("测试 🚀",read.Entries[0].Tags.Last().Value);
        Assert.Equal("NewTag_12",read.Entries.Last().Tags[0].Name);
        Assert.Equal(42u,read.Entries.Last().PackageFlags);
    }
    [Fact]
    public void RealRegistry_RejectsObjectPathOnlyChange()
    {
        if(!File.Exists(Sample)) return;
        var bytes=File.ReadAllBytes(Sample);
        var doc=AssetRegistryEditor.Read(bytes);
        var entries=doc.Entries.ToArray();
        entries[0]=entries[0] with { ObjectPath=entries[0].ObjectPath+"_Wrong" };
        Assert.Throws<ArgumentException>(()=>AssetRegistryEditor.Apply(bytes,new(doc.Revision,Entries:entries)));
    }
    public sealed class AssetRegistryServiceTests
    {
        private static readonly string Sample="/media/Vostro3/Arcade/SDFE_3.07/WindowsNoEditor/Mercury/AssetRegistry.bin";
        [Fact]
        public void Save_PinsSourceHashAndRejectsSourceChangesWithoutTouchingDraft()
        {
            if(!File.Exists(Sample)) return;
            var root=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")??Environment.CurrentDirectory,"registry-service-"+Guid.NewGuid().ToString("N"));
            var content=Path.Combine(root,"game","Content"); Directory.CreateDirectory(content);
            var original=Path.Combine(root,"game","AssetRegistry.bin"); var bytes=File.ReadAllBytes(Sample); File.WriteAllBytes(original,bytes);
            var workspaceRoot=Path.Combine(root,"workspace"); Directory.CreateDirectory(workspaceRoot); var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"workspace-root",workspaceRoot}}).Build();
            var store=new MusicWorkspaceStore(config);
            var id=Guid.NewGuid().ToString("N"); var projectDir=Path.Combine(workspaceRoot,id); Directory.CreateDirectory(projectDir); var workspace=Path.Combine(projectDir,"working"); Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(projectDir,"project-info.json"),System.Text.Json.JsonSerializer.Serialize(new ProjectManager.Project(id,"test",content.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,EngineVersion:"4.19")));
            var service=new AssetRegistryService(store); var doc=service.Read(id);
            var edited=doc.Entries.ToArray(); edited[0]=edited[0] with { Tags=[..edited[0].Tags,new("TestTag","first")] };
            service.Save(id,new(doc.Revision,Entries:edited));
            var draft=Path.Combine(workspace,"Registry","AssetRegistry.bin"); var saved=File.ReadAllBytes(draft);
            Assert.Equal(bytes,File.ReadAllBytes(original));
            var projects=new ProjectManager(store,null!);
            projects.Save(id);
            Assert.Equal(saved,File.ReadAllBytes(Path.Combine(projectDir,"saved","Registry","AssetRegistry.bin")));
            projects.RequireSaved(id);
            var next=service.Read(id); var nextEntries=next.Entries.ToArray();
            nextEntries[0]=nextEntries[0] with { Tags=[..nextEntries[0].Tags,new("SnapshotDirty","yes")] };
            service.Save(id,new(next.Revision,Entries:nextEntries));
            Assert.Throws<InvalidOperationException>(()=>projects.RequireSaved(id));
            saved=File.ReadAllBytes(draft);
            Assert.Equal(AssetRegistryEditor.ContentHash(bytes),File.ReadAllText(Path.Combine(workspace,"Registry","AssetRegistry.source.sha256")));
            File.WriteAllBytes(original,[..bytes,0x42]);
            var current=service.Read(id);
            var retry=current.Entries.ToArray(); retry[0]=retry[0] with { Tags=[..retry[0].Tags,new("TestTag2","second")] };
            Assert.Throws<InvalidOperationException>(()=>service.Save(id,new(current.Revision,Entries:retry)));
            Assert.Equal(saved,File.ReadAllBytes(draft));
            Directory.Delete(root,true);
        }
    }

    [Fact]
    public void RejectsUnsupportedHeader() => Assert.Throws<NotSupportedException>(()=>AssetRegistryEditor.Read(new byte[32]));
}
