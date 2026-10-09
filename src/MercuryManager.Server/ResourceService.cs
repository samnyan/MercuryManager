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
    public object ImportJson(string id,string path,string json)
    {
        lock(assets)
        {
            var source=Resolve(id,path);var dest=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(dest);
            var stage=Path.Combine(assets.WorkspaceDirectory(id),"json-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                var output=Path.Combine(stage,Path.GetFileName(path));ResourceJson.Import(source,json,output);
                var files=Directory.GetFiles(stage).Select(f=>(Source:f,Target:Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(f)))).ToArray();
                var old=new Dictionary<string,byte[]?>();foreach(var f in files){MusicWorkspaceStore.RejectLinks(f.Target);old[f.Target]=File.Exists(f.Target)?File.ReadAllBytes(f.Target):null;}
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try{foreach(var f in files)File.Copy(f.Source,f.Target,true);}catch{foreach(var f in files){if(old[f.Target] is {} data)File.WriteAllBytes(f.Target,data);else File.Delete(f.Target);}throw;}
                return new{path,verified=true};
            }
            finally{Directory.Delete(stage,true);}
        }
    }
    public object ReplaceAudio(string id,string path,string bank,ushort waveId,byte[] hca)
    {
        lock(assets){if(!path.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("Open the paired CueSheet uasset.");if(hca.Length==0||hca.Length>128*1024*1024)throw new ArgumentException("Audio upload limit: 128 MiB.");
        var sheetPath=path;var resolved=CriAudio.Target(Resolve(id,path),p=>{sheetPath=p;return Resolve(id,p);});var sheet=resolved.Path;if(bank!=Path.GetFileName(bank)||bank.Contains('\\')||!bank.EndsWith(".awb",StringComparison.Ordinal))throw new ArgumentException("Invalid bank.");
        var bankPath=(Path.GetDirectoryName(sheetPath)?.Replace('\\','/')+"/"+bank).TrimStart('/');var originalBank=Resolve(id,bankPath);
        var stage=Path.Combine(assets.WorkspaceDirectory(id),"audio-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{var input=Path.Combine(stage,"input.hca");var raw=hca.AsSpan(0,Math.Min(4,hca.Length)).SequenceEqual("OggS"u8)?AudioInspection.DecodeOggToWav(hca):hca;File.WriteAllBytes(input,raw.AsSpan(0,Math.Min(4,raw.Length)).SequenceEqual("RIFF"u8)?HcaEncoding.Encode(raw):raw);var result=Path.Combine(stage,"output");CriAudioEditor.Replace(sheet,originalBank,waveId,input,result);
        var files=Directory.GetFiles(result).Select(f=>(Source:f,Destination:Path.Combine(DraftRoot(id),Path.GetDirectoryName(sheetPath)??"",Path.GetFileName(f)))).ToArray();FileTransaction.Copy(files,false);return new{path,waveId,verified=true};}
        finally{Directory.Delete(stage,true);}}
    }
    public object EditCue(string id,string path,CueEditRequest request)
    {
        lock(assets){var sheetPath=path;var sheet=CriAudio.Target(Resolve(id,path),p=>{sheetPath=p;return Resolve(id,p);}).Path;
        if(request.Bank!=Path.GetFileName(request.Bank)||request.Bank.Contains('\\'))throw new ArgumentException("Invalid bank.");var bankPath=((Path.GetDirectoryName(sheetPath)??"")+"/"+request.Bank).TrimStart('/');var bank=Resolve(id,bankPath);
        var stage=Path.Combine(assets.WorkspaceDirectory(id),"cue-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{var result=Path.Combine(stage,"output");if(request.Operation=="delete")CriCueEditor.Delete(sheet,bank,request.CueId,result);
        else if(request.Operation=="add"){string Input(string name,string? base64){var bytes=Convert.FromBase64String(base64??"");if(bytes.Length==0||bytes.Length>128*1024*1024)throw new ArgumentException("Audio upload limit: 128 MiB.");if(bytes.AsSpan(0,Math.Min(4,bytes.Length)).SequenceEqual("OggS"u8))bytes=AudioInspection.DecodeOggToWav(bytes);if(bytes.AsSpan(0,Math.Min(4,bytes.Length)).SequenceEqual("RIFF"u8))bytes=HcaEncoding.Encode(bytes);var f=Path.Combine(stage,name+".hca");File.WriteAllBytes(f,bytes);return f;}var sp=Input("speaker",request.SpeakerBase64);var hp=Input("headphone",request.HeadphoneBase64);CriCueEditor.Add(sheet,bank,request.TemplateId,request.CueId,request.Name??"",sp,hp,result);}
        else throw new ArgumentException("Unknown Cue operation.");
        FileTransaction.Copy(Directory.GetFiles(result).Select(f=>(Source:f,Destination:Path.Combine(DraftRoot(id),Path.GetDirectoryName(sheetPath)??"",Path.GetFileName(f)))).ToArray(),false);return new{verified=true};}
        finally{Directory.Delete(stage,true);}}
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
        FileTransaction.Copy(files,backup);written.AddRange(files.Select(f=>f.Destination));
        return written.ToArray();}
    }
}
public sealed record CueEditRequest(string Operation,string Bank,int CueId,int TemplateId=88,string? Name=null,string? SpeakerBase64=null,string? HeadphoneBase64=null);
public sealed record ReplaceAudioRequest(string Bank,ushort WaveId,string HcaBase64);
public sealed record BuildTextureRequest(string Template,string Target,string ImageBase64);
