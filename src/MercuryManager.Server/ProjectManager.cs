using System.Security.Cryptography;
using System.Text.Json;
namespace MercuryManager.Server;

public sealed class ProjectManager(MusicWorkspaceStore assets)
{
    public sealed record Project(string Id, string Name, string? ContentRoot, string? SavedHash = null);
    private string Root => assets.ProjectRoot;
    private string DirectoryFor(string id) { if (!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Invalid project ID."); return Path.Combine(Root,id); }
    private string Metadata(string id) => Path.Combine(DirectoryFor(id),"project-info.json");
    private Project Read(string id) => JsonSerializer.Deserialize<Project>(File.ReadAllText(Metadata(id)))!;
    private void Store(Project project) { var path=Metadata(project.Id); File.WriteAllText(path+".tmp",JsonSerializer.Serialize(project)); File.Move(path+".tmp",path,true); }
    public object Status(string id) { var p=Read(id); return new {p.Id,p.Name,p.ContentRoot,dirty=p.SavedHash!=Hash(id),saved=p.SavedHash!=null}; }
    public object[] List() => Directory.GetDirectories(Root).Where(d=>File.Exists(Path.Combine(d,"project-info.json"))).Select(d=>Status(Path.GetFileName(d))).ToArray();
    public object Create(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>100)throw new ArgumentException("Project name required (maximum 100 characters).");
        var id=Guid.NewGuid().ToString("N"); Directory.CreateDirectory(DirectoryFor(id)); Store(new(id,name.Trim(),null)); return Status(id);
    }
    private string Hash(string id)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var p=Read(id);hash.AppendData(System.Text.Encoding.UTF8.GetBytes(p.Name+"\n"+p.ContentRoot));
        var working=Path.Combine(DirectoryFor(id),"working");
        if(Directory.Exists(working))foreach(var f in Directory.GetFiles(working,"*",SearchOption.AllDirectories).Where(f=>(Path.GetRelativePath(working,f).StartsWith("Imported"+Path.DirectorySeparatorChar)||Path.GetFileName(f) is "draft.json" or "additions.json")).Order(StringComparer.Ordinal))
        {hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(working,f)));hash.AppendData(File.ReadAllBytes(f));}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public object Save(string id)
    {
        lock(assets) {var p=Read(id);var dir=DirectoryFor(id);var working=Path.Combine(dir,"working");var staging=Path.Combine(dir,"saved-new");if(Directory.Exists(staging))Directory.Delete(staging,true);if(Directory.Exists(working))Copy(working,staging);else Directory.CreateDirectory(staging);var saved=Path.Combine(dir,"saved");var previous=Path.Combine(dir,"saved-old");if(Directory.Exists(previous))Directory.Delete(previous,true);if(Directory.Exists(saved))Directory.Move(saved,previous);try{Directory.Move(staging,saved);}catch{if(Directory.Exists(previous))Directory.Move(previous,saved);throw;}Store(p with {SavedHash=Hash(id)});File.WriteAllText(Path.Combine(dir,"project.mercury.json"),JsonSerializer.Serialize(Read(id)));return Status(id);}
    }
    public string[] ExportFiles(string id)
    {
        RequireSaved(id);var source=Path.Combine(DirectoryFor(id),"working","Imported");
        return Directory.GetFiles(source,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(source,f).Replace(Path.DirectorySeparatorChar,'/')).Order(StringComparer.Ordinal).ToArray();
    }
    public string[] ExportBase(string id,string mode,string? output)
    {
        RequireSaved(id);if(mode=="overwrite")return [];
        if(mode!="directory"||string.IsNullOrWhiteSpace(output)||!Path.IsPathFullyQualified(output))throw new ArgumentException("Absolute output directory required.");
        if(Path.GetFullPath(output)==Read(id).ContentRoot)throw new ArgumentException("Use overwrite mode.");
        var source=Path.Combine(DirectoryFor(id),"working","Imported");var written=new List<string>();
        foreach(var f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){var dest=Path.Combine(output,Path.GetRelativePath(source,f));MusicWorkspaceStore.RejectLinks(dest);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(f,dest,true);written.Add(dest);}
        return written.ToArray();
    }
    public void RequireSaved(string id) {var p=Read(id);if(p.SavedHash==null||p.SavedHash!=Hash(id))throw new InvalidOperationException("Save the project before exporting.");if(p.ContentRoot==null)throw new InvalidOperationException("Import game tables first.");}
    public object Import(string id,string path)
    {
        lock(assets)
        {
            var p=Read(id);path=Path.GetFullPath(path);if(!Directory.Exists(Path.Combine(path,"Table")))throw new ArgumentException("Select the game Content directory.");
            var dir=DirectoryFor(id);var staging=Path.Combine(dir,"import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                foreach(var folder in new[]{"Table","Message"})
                {
                    var source=Path.Combine(path,folder);if(!Directory.Exists(source))continue;
                    var dest=Path.Combine(staging,"Imported",folder);Directory.CreateDirectory(dest);
                    foreach(var file in Directory.GetFiles(source).Where(f=>Path.GetExtension(f) is ".uasset" or ".uexp")){MusicWorkspaceStore.RejectLinks(file);File.Copy(file,Path.Combine(dest,Path.GetFileName(file)));}
                }
                var music=Path.Combine(staging,"Imported","Table","MusicParameterTable.uasset");
                var asset=new UAssetAPI.UAsset(music,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
                var rows=asset.Exports.OfType<UAssetAPI.ExportTypes.DataTableExport>().Single().Table.Data.Count;
                foreach(var ext in new[]{".uasset",".uexp"})File.Copy(Path.ChangeExtension(music,ext),Path.Combine(staging,"MusicParameterTable"+ext));
                string Digest(string f)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
                File.WriteAllText(Path.Combine(staging,"manifest.json"),JsonSerializer.Serialize(new Workspace(id,"ue4.19",Digest(music),Digest(Path.ChangeExtension(music,".uexp")),rows,path)));
                var working=Path.Combine(dir,"working");if(Directory.Exists(working))Directory.Delete(working,true);Directory.Move(staging,working);Store(p with {ContentRoot=path});return Status(id);
            }
            finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
        }
    }
    private static void Copy(string source,string target) {Directory.CreateDirectory(target);foreach(var f in Directory.GetFiles(source)){MusicWorkspaceStore.RejectLinks(f);File.Copy(f,Path.Combine(target,Path.GetFileName(f)));}foreach(var d in Directory.GetDirectories(source)){MusicWorkspaceStore.RejectLinks(d);Copy(d,Path.Combine(target,Path.GetFileName(d)));}}
}
