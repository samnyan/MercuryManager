using System.Text.Json;
using Xunit;
using MercuryManager.Server;
using Microsoft.Extensions.Configuration;
namespace MercuryManager.Assets.Tests;
public sealed class ProjectManagerTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"mercury-project-tests-"+Guid.NewGuid().ToString("N"));
    private ProjectManager Manager()=>new(new MusicWorkspaceStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"workspace-root",root}}).Build()));
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [Fact] public void NewSaveReopenAndDraftDirty()
    {
        var manager=Manager();var project=Json(manager.Create("Test"));var id=project.GetProperty("id").GetString()!;
        Assert.True(project.GetProperty("dirty").GetBoolean());Assert.False(project.GetProperty("saved").GetBoolean());
        Assert.Throws<InvalidOperationException>(()=>manager.RequireSaved(id));
        Assert.False(Json(manager.Save(id)).GetProperty("dirty").GetBoolean());
        var working=Path.Combine(root,id,"working");Directory.CreateDirectory(working);File.WriteAllText(Path.Combine(working,"draft.json"),"{}");
        Assert.True(Json(manager.Status(id)).GetProperty("dirty").GetBoolean());
        manager.Save(id);Assert.Equal("{}",File.ReadAllText(Path.Combine(root,id,"saved","draft.json")));
        Assert.False(Json(Manager().Status(id)).GetProperty("dirty").GetBoolean());
        Assert.Single(manager.List());
    }
    [Fact] public void FailedImportPreservesProject()
    {
        var manager=Manager();var id=Json(manager.Create("Test")).GetProperty("id").GetString()!;manager.Save(id);
        Assert.Throws<ArgumentException>(()=>manager.Import(id,root));Assert.False(Json(manager.Status(id)).GetProperty("dirty").GetBoolean());
    }
    [Fact] public void ReadingLazyBaseDoesNotMakeProjectDirty()
    {
        var manager=Manager();var id=Json(manager.Create("Test")).GetProperty("id").GetString()!;manager.Save(id);
        var dir=Path.Combine(root,id,"working","Message","Test");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"Test.uasset"),"test");
        Assert.False(Json(manager.Status(id)).GetProperty("dirty").GetBoolean());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
