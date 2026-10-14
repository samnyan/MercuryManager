using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MercuryManager.Server;
using Xunit;
namespace MercuryManager.Assets.Tests;
public sealed class ResourceResolutionTests
{
    [Theory]
    [InlineData("Sound/Bgm/MER_BGM.uasset","MER_BGM.awb","Sound/Bgm/MER_BGM.awb")]
    [InlineData("Sound/Voice/Sheet.uasset","Voice.awb","Sound/Voice/Voice.awb")]
    [InlineData("Sheet.uasset","Bank.awb","Bank.awb")]
    public void SiblingsKeepContentSeparators(string sheet,string bank,string expected)
        =>Assert.Equal(expected,ResourceService.Sibling(sheet,bank));
    [Theory]
    [InlineData("../Bank.awb")]
    [InlineData("Sound\\Bank.awb")]
    [InlineData("/Bank.awb")]
    public void SiblingsRejectUnsafeBank(string bank)
        =>Assert.Throws<ArgumentException>(()=>ResourceService.Sibling("Sound/Bgm/Sheet.uasset",bank));
    [Fact]
    public void ResolvesGameThenDraftAndAllowsOriginalPreview()
    {
        var root=Path.Combine(Path.GetTempPath(),"mercury-resource-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new MusicWorkspaceStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"workspace-root",Path.Combine(root,"workspace")}}).Build());
            var id=Guid.NewGuid().ToString("N");var content=Path.Combine(root,"Content");Directory.CreateDirectory(content);
            var working=Path.Combine(store.ProjectRoot,id,"working");Directory.CreateDirectory(working);
            File.WriteAllText(Path.Combine(store.ProjectRoot,id,"project-info.json"),"{}");
            File.WriteAllText(Path.Combine(working,"manifest.json"),JsonSerializer.Serialize(new Workspace(id,"ue4.19","","",0,content)));
            var service=new ResourceService(store,new GameContent());const string path="Sound/Bgm/MER_BGM.awb";
            var game=Path.Combine(content,path);Directory.CreateDirectory(Path.GetDirectoryName(game)!);File.WriteAllText(game,"original");
            Assert.Equal(game,service.Resolve(id,path));Assert.Equal(new ResourceService.ResourceSources("game",false,true),service.Sources(id,path));
            var draft=Path.Combine(service.DraftRoot(id),path);Directory.CreateDirectory(Path.GetDirectoryName(draft)!);File.WriteAllText(draft,"edited");
            Assert.Equal(draft,service.Resolve(id,path));Assert.Equal(draft,service.Resolve(id,path,"project"));Assert.Equal(game,service.Resolve(id,path,"game"));
            Assert.Equal(new ResourceService.ResourceSources("project",true,true),service.Sources(id,path));
            File.Delete(draft);Assert.Equal(game,service.Resolve(id,path,"project"));
            Assert.Throws<ArgumentException>(()=>service.Resolve(id,path,"invalid"));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public void ResolvesEngineMaterialDependencyWithTrailingSlashContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mercury-engine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var installation = Path.Combine(root, "GameInstallation");
            var contentWithSlash = Path.Combine(installation, "Game", "Content") + Path.DirectorySeparatorChar;
            var engineTarget = Path.Combine(installation, "Engine", "Content", "EngineResources", "DefaultTexture.uasset");
            Directory.CreateDirectory(Path.GetDirectoryName(engineTarget)!);
            File.WriteAllText(engineTarget, "engine");

            var store = new MusicWorkspaceStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "workspace-root", Path.Combine(root, "workspace") } }).Build());
            var id = Guid.NewGuid().ToString("N");
            var working = Path.Combine(store.ProjectRoot, id, "working");
            Directory.CreateDirectory(working);
            File.WriteAllText(Path.Combine(store.ProjectRoot, id, "project-info.json"), "{}");
            File.WriteAllText(Path.Combine(working, "manifest.json"), JsonSerializer.Serialize(new Workspace(id, "ue4.19", "", "", 0, contentWithSlash)));

            var service = new ResourceService(store, new GameContent());
            var resolved = service.ResolveMaterialDependency(id, "/Engine/EngineResources/DefaultTexture.uasset");
            Assert.Equal(engineTarget, resolved);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
