namespace MercuryManager.Server;
public sealed class ResourceService(MusicWorkspaceStore assets)
{
    public string DraftRoot(string id)=>Path.Combine(assets.WorkspaceDirectory(id),"Resources");
    public static string Validate(string relative)
    {
        if(!(relative.EndsWith(".uasset",StringComparison.Ordinal)||relative.EndsWith(".awb",StringComparison.Ordinal))||relative.Length>256||relative.Contains('\\')||relative.Split('/').Any(s=>s.Length==0||s is "." or ".."||s.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-' and not '.'))||Path.IsPathRooted(relative))throw new ArgumentException("Invalid Content-relative asset path.");
        return relative;
    }
    public string Resolve(string id,string path)
    {
        Validate(path);var draft=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(draft);
        if(File.Exists(draft)){foreach(var ext in new[]{".uexp",".ubulk"})MusicWorkspaceStore.RejectLinks(Path.ChangeExtension(draft,ext));return draft;}
        var original=Path.Combine(assets.Get(id).ContentRoot??throw new InvalidOperationException("Import game first."),path);MusicWorkspaceStore.RejectLinks(original);
        foreach(var ext in new[]{".uexp",".ubulk"})MusicWorkspaceStore.RejectLinks(Path.ChangeExtension(original,ext));
        if(!File.Exists(original))throw new FileNotFoundException("Resource missing.");return original;
    }
    public object[] List(string id,string directory)
    {
        if(directory.Length>0)Validate(directory+"/folder.uasset");
        var roots=new[]{assets.Get(id).ContentRoot!,DraftRoot(id)};var items=new Dictionary<string,bool>(StringComparer.Ordinal);
        foreach(var root in roots){var dir=Path.Combine(root,directory);MusicWorkspaceStore.RejectLinks(dir);if(!Directory.Exists(dir))continue;
        foreach(var entry in Directory.EnumerateFileSystemEntries(dir)){MusicWorkspaceStore.RejectLinks(entry);bool folder=Directory.Exists(entry);if(folder||entry.EndsWith(".uasset",StringComparison.Ordinal)||entry.EndsWith(".awb",StringComparison.Ordinal)){var relative=Path.GetRelativePath(root,entry).Replace('\\','/');items[relative]=folder;}}}
        return items.OrderByDescending(x=>x.Value).ThenBy(x=>x.Key,StringComparer.Ordinal).Select(x=>(object)new{path=x.Key,name=Path.GetFileName(x.Key),directory=x.Value}).ToArray();
    }
    public object Info(string id,string path)
    {
        var source=Resolve(id,path);var a=new UAssetAPI.UAsset(source,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
        try{var n=a.Exports.Single() as UAssetAPI.ExportTypes.NormalExport??throw new InvalidDataException();var layout=TextureAuthoring.Inspect(n.Extras);return new{width=layout.Width,height=layout.Height,supported=true};}catch{return new{width=0,height=0,supported=false};}
    }
    public object Build(string id,string template,string target,byte[] image)
    {
        lock(assets){Validate(target);if(!target.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("Texture output must be uasset.");if(!target.StartsWith("UI/Textures/",StringComparison.Ordinal))throw new ArgumentException("Output must be under UI/Textures/.");if(image.Length>16*1024*1024)throw new ArgumentException("Upload too large.");
        var source=Resolve(id,template);var root=DraftRoot(id);var dest=Path.Combine(root,target);MusicWorkspaceStore.RejectLinks(dest);
        if(new[]{".uasset",".uexp",".ubulk"}.Any(ext=>File.Exists(Path.ChangeExtension(dest,ext))||File.Exists(Path.ChangeExtension(Path.Combine(assets.Get(id).ContentRoot!,target),ext))))throw new IOException("Target already exists; choose a new resource path.");
        var staging=Path.Combine(assets.WorkspaceDirectory(id),"texture-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try{var output=Path.Combine(staging,Path.GetFileName(target));TextureAuthoring.Build(source,image,target,output);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var created=new List<string>();try{foreach(var file in Directory.GetFiles(staging)){var targetFile=Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(file));MusicWorkspaceStore.RejectLinks(targetFile);File.Copy(file,targetFile,false);created.Add(targetFile);}}catch{foreach(var file in created)File.Delete(file);throw;}
        return new{path=target,workspacePath=dest,verified=true};}finally{Directory.Delete(staging,true);}}
    }
    public string[] Changes(string id){var root=DraftRoot(id);return Directory.Exists(root)?Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(root,f).Replace('\\','/')).Order(StringComparer.Ordinal).ToArray():[];}
    public string[] Write(string id,string output,bool backup)
    {
        var root=DraftRoot(id);if(!Directory.Exists(root))return [];var written=new List<string>();
        lock(assets){
        var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(source=>(Source:source,Destination:Path.Combine(output,Path.GetRelativePath(root,source)))).ToArray();
        var previous=new Dictionary<string,byte[]?>();var backups=new List<string>();
        foreach(var file in files){MusicWorkspaceStore.RejectLinks(file.Source);MusicWorkspaceStore.RejectLinks(file.Destination);MusicWorkspaceStore.RejectLinks(file.Destination+"_bak");MusicWorkspaceStore.RejectLinks(file.Destination+".mercury-tmp");if(File.Exists(file.Destination+".mercury-tmp")||(backup&&File.Exists(file.Destination)&&File.Exists(file.Destination+"_bak")))throw new IOException("Output staging or backup already exists.");previous[file.Destination]=File.Exists(file.Destination)?File.ReadAllBytes(file.Destination):null;}
        try{foreach(var file in files){Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);if(backup&&previous[file.Destination]!=null){File.Copy(file.Destination,file.Destination+"_bak",false);backups.Add(file.Destination+"_bak");}File.Copy(file.Source,file.Destination+".mercury-tmp",false);File.Move(file.Destination+".mercury-tmp",file.Destination,true);if(!File.ReadAllBytes(file.Source).SequenceEqual(File.ReadAllBytes(file.Destination)))throw new IOException("Resource copy verification failed.");written.Add(file.Destination);}}
        catch{foreach(var file in files){if(previous[file.Destination] is { } old)File.WriteAllBytes(file.Destination,old);else File.Delete(file.Destination);File.Delete(file.Destination+".mercury-tmp");}foreach(var file in backups)File.Delete(file);throw;}
        return written.ToArray();}
    }
}
public sealed record BuildTextureRequest(string Template,string Target,string ImageBase64);
