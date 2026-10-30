using System.Security.Cryptography;
using System.Text.Json;
namespace MercuryManager.Server;

public sealed class ProjectManager(MusicWorkspaceStore assets,GameContent content)
{
    public sealed record Project(string Id, string Name, string? ContentRoot, string? SavedHash = null, string? EngineVersion = null, int? CanvasWidth = null, int? CanvasHeight = null);
    private string Root => assets.ProjectRoot;
    private string DirectoryFor(string id) { if (!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Invalid project ID."); return Path.Combine(Root,id); }
    private string Metadata(string id) => Path.Combine(DirectoryFor(id),"project-info.json");
    private Project Read(string id) => JsonSerializer.Deserialize<Project>(File.ReadAllText(Metadata(id)))!;
    private void Store(Project project) { var path=Metadata(project.Id); File.WriteAllText(path+".tmp",JsonSerializer.Serialize(project)); File.Move(path+".tmp",path,true); }
    public object Status(string id) { var p=Read(id); return new {p.Id,p.Name,p.ContentRoot,p.EngineVersion,p.CanvasWidth,p.CanvasHeight,dirty=p.SavedHash!=Hash(id),saved=p.SavedHash!=null}; }
    // The picker needs metadata, not a full content digest of every project's AWBs.
    public object[] List() => Directory.GetDirectories(Root).Where(d=>File.Exists(Path.Combine(d,"project-info.json"))).Select(d=>{var p=Read(Path.GetFileName(d));return (object)new {p.Id,p.Name,p.ContentRoot,saved=p.SavedHash!=null};}).ToArray();
    public object SetCanvas(string id,int width,int height)
    {
        if(width<1||height<1||width>8192||height>8192)throw new ArgumentException("Canvas dimensions must be between 1 and 8192.");
        lock(assets){Store(Read(id) with {CanvasWidth=width,CanvasHeight=height});return Status(id);}
    }
    public object Create(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>100)throw new ArgumentException("Project name required (maximum 100 characters).");
        var id=Guid.NewGuid().ToString("N"); Directory.CreateDirectory(DirectoryFor(id)); Store(new(id,name.Trim(),null)); return Status(id);
    }
    private string Hash(string id)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var p=Read(id);hash.AppendData(System.Text.Encoding.UTF8.GetBytes(p.Name+"\n"+p.ContentRoot+(p.EngineVersion is null?"":"\n"+p.EngineVersion)));
        var working=Path.Combine(DirectoryFor(id),"working");
        if(Directory.Exists(working))foreach(var f in Directory.GetFiles(working,"*",SearchOption.AllDirectories).Where(f=>(Path.GetRelativePath(working,f).StartsWith("Resources"+Path.DirectorySeparatorChar)||Path.GetRelativePath(working,f).StartsWith("Imported"+Path.DirectorySeparatorChar)||Path.GetRelativePath(working,f).StartsWith("Registry"+Path.DirectorySeparatorChar)||Path.GetFileName(f) is "draft.json" or "additions.json")).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(working, f);
            var info = new FileInfo(f);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{rel}:{info.Length}:{info.LastWriteTimeUtc.Ticks}"));
            if (info.Length <= 4 * 1024 * 1024)
            {
                using var input = File.OpenRead(f);
                var buffer = new byte[65536];
                int count;
                while ((count = input.Read(buffer)) > 0) hash.AppendData(buffer, 0, count);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public object Save(string id)
    {
        lock(assets) {var p=Read(id);var dir=DirectoryFor(id);var working=Path.Combine(dir,"working");var staging=Path.Combine(dir,"saved-new");if(Directory.Exists(staging))Directory.Delete(staging,true);if(Directory.Exists(working))Copy(working,staging);else Directory.CreateDirectory(staging);var saved=Path.Combine(dir,"saved");var previous=Path.Combine(dir,"saved-old");if(Directory.Exists(previous))Directory.Delete(previous,true);if(Directory.Exists(saved))Directory.Move(saved,previous);try{Directory.Move(staging,saved);}catch{if(Directory.Exists(previous))Directory.Move(previous,saved);throw;}Store(p with {SavedHash=Hash(id)});File.WriteAllText(Path.Combine(dir,"project.mercury.json"),JsonSerializer.Serialize(Read(id)));return Status(id);}
    }
    public string[] ExportFiles(string id)
    {
        RequireSaved(id);var source=Path.Combine(DirectoryFor(id),"working","Imported");
        var resources=Path.Combine(DirectoryFor(id),"working","Resources");
        return Directory.GetFiles(source,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(source,f).Replace(Path.DirectorySeparatorChar,'/')).Concat(Directory.Exists(resources)?Directory.GetFiles(resources,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(resources,f).Replace(Path.DirectorySeparatorChar,'/')):[]).Distinct().Order(StringComparer.Ordinal).ToArray();
    }
    public string[] ExportBase(string id,string mode,string? output)
    {
        RequireSaved(id);if(mode=="overwrite"){if(GameContent.IsPacked(Read(id).ContentRoot!))throw new InvalidOperationException("PAK projects support directory export only; patch PAK export is not implemented.");return [];}
        if(mode!="directory"||string.IsNullOrWhiteSpace(output)||!Path.IsPathFullyQualified(output))throw new ArgumentException("Absolute output directory required.");
        if(Path.GetFullPath(output)==Read(id).ContentRoot)throw new ArgumentException("Use overwrite mode.");
        var source=Path.Combine(DirectoryFor(id),"working","Imported");var written=new List<string>();
        foreach(var f in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){var dest=Path.Combine(output,Path.GetRelativePath(source,f));MusicWorkspaceStore.RejectLinks(dest);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(f,dest,true);written.Add(dest);}
        return written.ToArray();
    }
    public void RequireSaved(string id) {var p=Read(id);if(p.SavedHash==null||p.SavedHash!=Hash(id))throw new InvalidOperationException("Save the project before exporting.");if(p.ContentRoot==null)throw new InvalidOperationException("Import game tables first.");}
    public object Import(string id,string path,string? engineVersion=null)
    {
        lock(assets)
        {
            var p=Read(id);path=Path.GetFullPath(path);engineVersion ??= AssetEngineVersion.Inspect(path).Version;AssetEngineVersion.Parse(engineVersion);if(!Directory.Exists(path)||!content.Files(path,"").Any(f=>f.EndsWith(".uasset",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Select a Content directory containing Unreal assets.");
            var dir=DirectoryFor(id);var staging=Path.Combine(dir,"import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                Directory.CreateDirectory(Path.Combine(staging,"Imported"));
                foreach(var folder in new[]{"Table","Message"})
                {
                    foreach(var relative in content.Files(path,folder).Where(f=>Path.GetExtension(f) is ".uasset" or ".uexp" or ".ubulk"))
                        content.Copy(path,relative,Path.Combine(staging,"Imported",relative));
                }
                var music=Path.Combine(staging,"Imported","Table","MusicParameterTable.uasset");
                var workspace=new Workspace(id,"ue"+engineVersion,"","",0,path);
                if(File.Exists(music)) {
                var asset=new UAssetAPI.UAsset(music,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
                var rows=asset.Exports.OfType<UAssetAPI.ExportTypes.DataTableExport>().Single().Table.Data.Count;
                foreach(var ext in new[]{".uasset",".uexp"})File.Copy(Path.ChangeExtension(music,ext),Path.Combine(staging,"MusicParameterTable"+ext));
                string Digest(string f)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
                workspace=workspace with {UassetHash=Digest(music),UexpHash=Digest(Path.ChangeExtension(music,".uexp")),RowCount=rows};
                }
                File.WriteAllText(Path.Combine(staging,"manifest.json"),JsonSerializer.Serialize(workspace));
                var working=Path.Combine(dir,"working");if(Directory.Exists(working))Directory.Delete(working,true);Directory.Move(staging,working);Store(p with {ContentRoot=path,EngineVersion=engineVersion});return Status(id);
            }
            finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
        }
    }
    private static void Copy(string source,string target) {Directory.CreateDirectory(target);foreach(var f in Directory.GetFiles(source)){MusicWorkspaceStore.RejectLinks(f);File.Copy(f,Path.Combine(target,Path.GetFileName(f)));}foreach(var d in Directory.GetDirectories(source)){if(Path.GetFileName(d)=="SourceCache")continue;MusicWorkspaceStore.RejectLinks(d);Copy(d,Path.Combine(target,Path.GetFileName(d)));}}
}
