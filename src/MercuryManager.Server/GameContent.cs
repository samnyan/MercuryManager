using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
namespace MercuryManager.Server;

// Original Content is read-only. PAK entries are materialized only for path-based readers.
public sealed class GameContent : IDisposable
{
    private readonly Dictionary<string,DefaultFileProvider> providers=new(StringComparer.Ordinal);
    private readonly object gate=new();
    public static bool IsPacked(string root)=>Directory.Exists(Path.Combine(root,"Paks"))&&Directory.EnumerateFiles(Path.Combine(root,"Paks"),"*.pak").Any();
    private DefaultFileProvider Provider(string root)
    {
        if(providers.TryGetValue(root,out var found))return found;
        foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"Paks")))MusicWorkspaceStore.RejectLinks(file);
        var p=new DefaultFileProvider(Path.Combine(root,"Paks"),SearchOption.TopDirectoryOnly,new VersionContainer(EGame.GAME_UE4_19),StringComparer.OrdinalIgnoreCase);
        try{p.Initialize();p.Mount();if(p.UnloadedVfs.Count>0)throw new InvalidDataException("PAK could not be mounted; encrypted archives require a key.");providers.Add(root,p);return p;}
        catch{p.Dispose();throw;}
    }
    private static string Prefix=>"Mercury/Content/";
    public string[] Files(string root,string directory)
    {
        lock(gate){var loose=Directory.Exists(Path.Combine(root,directory))?Directory.GetFiles(Path.Combine(root,directory),"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(root,f).Replace('\\','/')):[];
        var packed=IsPacked(root)?Provider(root).Files.Keys.Where(k=>k.StartsWith(Prefix+(directory.Length==0?"":directory+"/"),StringComparison.OrdinalIgnoreCase)).Select(k=>k[Prefix.Length..]):[];
        return loose.Concat(packed).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
    }
    public bool Exists(string root,string relative)
    {
        lock(gate){MusicWorkspaceStore.RejectLinks(Path.Combine(root,relative));return File.Exists(Path.Combine(root,relative))||(IsPacked(root)&&Provider(root).Files.ContainsKey(Prefix+relative));}
    }
    public void Copy(string root,string relative,string destination)
    {
        lock(gate){if(Path.IsPathRooted(relative)||relative.Contains('\\')||relative.Split('/').Any(s=>s.Length==0||s is "." or ".."))throw new ArgumentException("Invalid Content-relative path.");MusicWorkspaceStore.RejectLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var loose=Path.Combine(root,relative);MusicWorkspaceStore.RejectLinks(loose);
        if(File.Exists(loose)){File.Copy(loose,destination,true);return;}
        if(!IsPacked(root)||!Provider(root).Files.TryGetValue(Prefix+relative,out var entry))throw new FileNotFoundException("Resource missing: "+relative);
        File.WriteAllBytes(destination,entry.Read());}
    }
    public string Resolve(string root,string relative,string cache)
    {
        var loose=Path.Combine(root,relative);MusicWorkspaceStore.RejectLinks(loose);if(File.Exists(loose))return loose;
        lock(gate){var path=Path.Combine(cache,relative);MusicWorkspaceStore.RejectLinks(path);if(!Exists(root,relative))throw new FileNotFoundException("Resource missing: "+relative);
        foreach(var file in relative.EndsWith(".uasset",StringComparison.Ordinal)?new[]{relative,Path.ChangeExtension(relative,".uexp"),Path.ChangeExtension(relative,".ubulk")}:new[]{relative})
            if(Exists(root,file)&&!File.Exists(Path.Combine(cache,file)))Copy(root,file,Path.Combine(cache,file));
        return path;}
    }
    public void Dispose(){foreach(var p in providers.Values)p.Dispose();}
}
