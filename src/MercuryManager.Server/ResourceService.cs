namespace MercuryManager.Server;
public sealed class ResourceService(MusicWorkspaceStore assets,GameContent content)
{
    public string DraftRoot(string id)=>Path.Combine(assets.WorkspaceDirectory(id),"Resources");
    private static bool IsSupportedExtension(string path)=>path.EndsWith(".uasset",StringComparison.Ordinal)||path.EndsWith(".awb",StringComparison.Ordinal)||path.EndsWith(".mer",StringComparison.Ordinal);
    public static string Validate(string relative)
    {
        if(!(IsSupportedExtension(relative)||relative.EndsWith(".usm",StringComparison.Ordinal))||relative.Length>256||relative.Contains('\\')||relative.Split('/').Any(s=>s.Length==0||s is "." or ".."||s.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-' and not '.'))||Path.IsPathRooted(relative))throw new ArgumentException("Invalid Content-relative asset path.");
        return relative;
    }
    // Content-relative paths always use '/', including on Windows.
    public static string Sibling(string path,string name)
    {
        Validate(path);AudioBanks.ValidateName(name);
        return Validate(path[..(path.LastIndexOf('/')+1)]+name);
    }
    public sealed record ResourceSources(string Selected,bool Project,bool Game);
    public ResourceSources Sources(string id,string path)
    {
        Validate(path);var draft=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(draft);
        var root=assets.Get(id).ContentRoot;var original=root is null?null:Path.Combine(root,path);
        if(original is not null)MusicWorkspaceStore.RejectLinks(original);
        bool project=File.Exists(draft),game=root is not null&&content.Exists(root,path);
        return new(project?"project":"game",project,game);
    }
    public sealed record AudioResource(string Path,string? CueName,string Relative);
    public AudioResource AudioTarget(string id,string path,string? source=null)
    {
        string relative=path;
        var target=CriAudio.Target(Resolve(id,path,source),p=>{relative=p;return Resolve(id,p,source);});
        return new(target.Path,target.CueName,relative);
    }
    public string Resolve(string id,string path,string? source=null)
    {
        if(source is not (null or "project" or "game"))throw new ArgumentException("Invalid resource source.");
        Validate(path);var draft=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(draft);
        if(source!="game"&&File.Exists(draft)){foreach(var ext in new[]{".uexp",".ubulk"})MusicWorkspaceStore.RejectLinks(Path.ChangeExtension(draft,ext));return draft;}
        return content.Resolve(assets.Get(id).ContentRoot??throw new InvalidOperationException("Import game first."),path,Path.Combine(assets.WorkspaceDirectory(id),"SourceCache"));
    }
    public string ResolveMaterialDependency(string id, string path)
    {
        if (!path.StartsWith("/Engine/", StringComparison.Ordinal)) return Resolve(id, path);
        var relative = Validate(path[8..]);
        var root = assets.Get(id).ContentRoot ?? throw new InvalidOperationException("Import game first.");
        // 只接受标准安装布局，不搜索任意父目录或回退到系统上的其他安装。
        var cleanRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var gameDirectory = Directory.GetParent(cleanRoot);
        var installation = gameDirectory?.Parent;
        if (Path.GetFileName(cleanRoot) != "Content" || installation == null)
            throw new NotSupportedException("Engine dependency requires a standard game Content installation.");
        var target = Path.Combine(installation.FullName, "Engine", "Content", relative);
        MusicWorkspaceStore.RejectLinks(target);
        foreach (var ext in new[] { ".uexp", ".ubulk" }) MusicWorkspaceStore.RejectLinks(Path.ChangeExtension(target, ext));
        if (!File.Exists(target)) throw new NotSupportedException("Engine material dependency is unavailable.");
        return target;
    }
    public object[] List(string id,string directory)
    {
        if(directory.Length>0)Validate(directory+"/folder.uasset");
        var draftRoot=DraftRoot(id);var items=new Dictionary<string,bool>(StringComparer.Ordinal);
        foreach(var root in new[]{assets.Get(id).ContentRoot!,draftRoot}){var dir=Path.Combine(root,directory);MusicWorkspaceStore.RejectLinks(dir);if(!Directory.Exists(dir))continue;
        foreach(var entry in Directory.EnumerateFileSystemEntries(dir)){MusicWorkspaceStore.RejectLinks(entry);bool folder=Directory.Exists(entry);if(folder||IsSupportedExtension(entry)){var relative=Path.GetRelativePath(root,entry).Replace('\\','/');if(root==draftRoot)items[relative]=folder;else items.TryAdd(relative,folder);}}}
        var prefix=directory.Length==0?"":directory+"/";
        var originalRoot=assets.Get(id).ContentRoot!;
        if(GameContent.IsPacked(originalRoot))foreach(var relative in content.Files(originalRoot,directory))
        {
            if(!relative.StartsWith(prefix,StringComparison.Ordinal))continue;
            var rest=relative[prefix.Length..];var slash=rest.IndexOf('/');
            if(slash>=0)items.TryAdd(prefix+rest[..slash],true);
            else if(IsSupportedExtension(relative))items.TryAdd(relative,false);
        }
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
        var bankPath=Sibling(sheetPath,bank);var originalBank=Resolve(id,bankPath);
        var stage=Path.Combine(assets.WorkspaceDirectory(id),"audio-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{var input=Path.Combine(stage,"input.hca");var raw=hca.AsSpan(0,Math.Min(4,hca.Length)).SequenceEqual("OggS"u8)?AudioInspection.DecodeOggToWav(hca):hca;File.WriteAllBytes(input,raw.AsSpan(0,Math.Min(4,raw.Length)).SequenceEqual("RIFF"u8)?HcaEncoding.Encode(raw):raw);var result=Path.Combine(stage,"output");CriAudioEditor.Replace(sheet,originalBank,waveId,input,result);
        var files=Directory.GetFiles(result).Select(f=>(Source:f,Destination:Path.Combine(DraftRoot(id),Path.GetDirectoryName(sheetPath)??"",Path.GetFileName(f)))).ToArray();FileTransaction.Copy(files,false);return new{path,waveId,verified=true};}
        finally{Directory.Delete(stage,true);}}
    }
    public object EditCue(string id,string path,CueEditRequest request)
    {
        lock(assets){var sheetPath=path;var sheet=CriAudio.Target(Resolve(id,path),p=>{sheetPath=p;return Resolve(id,p);}).Path;
        if(request.Bank!=Path.GetFileName(request.Bank)||request.Bank.Contains('\\'))throw new ArgumentException("Invalid bank.");var bankPath=Sibling(sheetPath,request.Bank);var bank=Resolve(id,bankPath);
        var stage=Path.Combine(assets.WorkspaceDirectory(id),"cue-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{var result=Path.Combine(stage,"output");if(request.Operation=="delete")CriCueEditor.Delete(sheet,bank,request.CueId,result);
        else if(request.Operation=="add"){string Input(string name,string? base64){var bytes=Convert.FromBase64String(base64??"");if(bytes.Length==0||bytes.Length>128*1024*1024)throw new ArgumentException("Audio upload limit: 128 MiB.");if(bytes.AsSpan(0,Math.Min(4,bytes.Length)).SequenceEqual("OggS"u8))bytes=AudioInspection.DecodeOggToWav(bytes);if(bytes.AsSpan(0,Math.Min(4,bytes.Length)).SequenceEqual("RIFF"u8))bytes=HcaEncoding.Encode(bytes);var f=Path.Combine(stage,name+".hca");File.WriteAllBytes(f,bytes);return f;}var sp=Input("speaker",request.SpeakerBase64);var hp=Input("headphone",request.HeadphoneBase64);CriCueEditor.Add(sheet,bank,request.TemplateId,request.CueId,request.Name??"",sp,hp,result);}
        else throw new ArgumentException("Unknown Cue operation.");
        FileTransaction.Copy(Directory.GetFiles(result).Select(f=>(Source:f,Destination:Path.Combine(DraftRoot(id),Path.GetDirectoryName(sheetPath)??"",Path.GetFileName(f)))).ToArray(),false);return new{verified=true};}
        finally{Directory.Delete(stage,true);}}
    }
    public object Info(string id,string path)
    {
        if(!path.EndsWith(".uasset",StringComparison.Ordinal))return new{width=0,height=0,mips=0,supported=false};
        var source=Resolve(id,path);var a=new UAssetAPI.UAsset(source,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
        try{var n=a.Exports.Single() as UAssetAPI.ExportTypes.NormalExport??throw new InvalidDataException();var layout=TextureAuthoring.Inspect(n.Extras);return new{width=layout.Width,height=layout.Height,mips=layout.Mips.Count,supported=true};}catch{return new{width=0,height=0,mips=0,supported=false};}
    }
    public object ReadText(string id,string path,string? source=null)
    {
        Validate(path);
        if(!path.EndsWith(".mer",StringComparison.Ordinal))throw new ArgumentException("Only .mer text files are supported.");
        var resolved=Resolve(id,path,source);
        if(new FileInfo(resolved).Length>8*1024*1024)throw new InvalidDataException("Text file exceeds 8 MiB limit.");
        var text=File.ReadAllText(resolved,System.Text.Encoding.UTF8);
        var headers=new Dictionary<string,string>();
        int bodyLines=0;bool inBody=false;
        foreach(var rawLine in text.Split('\n'))
        {
            var line=rawLine.Trim();
            if(line.Length==0)continue;
            if(line.StartsWith("#BODY",StringComparison.OrdinalIgnoreCase)){inBody=true;continue;}
            if(!inBody&&line.StartsWith('#'))
            {
                var space=line.IndexOfAny([' ','\t']);
                if(space>1)headers[line[1..space].Trim()]=line[(space+1)..].Trim();
                else headers[line[1..].Trim()]="";
            }
            else if(inBody)bodyLines++;
        }
        return new{path,content=text,headers,eventCount=bodyLines,sources=Sources(id,path)};
    }
    public object SaveText(string id,string path,string contentText)
    {
        lock(assets)
        {
            Validate(path);
            if(!path.EndsWith(".mer",StringComparison.Ordinal))throw new ArgumentException("Only .mer text files are supported.");
            if(contentText.Length>8*1024*1024)throw new ArgumentException("Text content exceeds 8 MiB limit.");
            var dest=Path.Combine(DraftRoot(id),path);
            MusicWorkspaceStore.RejectLinks(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest,contentText,new System.Text.UTF8Encoding(false));
            return new{path,verified=true,sources=Sources(id,path)};
        }
    }
    public object SaveMaterialParams(string id,string path,SaveMaterialParamsRequest request)
    {
        lock(assets)
        {
            Validate(path);
            if(!path.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("Only .uasset files are supported.");
            var source=Resolve(id,path);var dest=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(dest);
            var stage=Path.Combine(assets.WorkspaceDirectory(id),"mat-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                var asset=new UAssetAPI.UAsset(source,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
                var scalars=request.Scalars??new Dictionary<string,float>();
                var vectors=request.Vectors??new Dictionary<string,VectorColorValue>();

                var miExport=asset.Exports.OfType<UAssetAPI.ExportTypes.NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString() is "MaterialInstanceConstant" or "MaterialInstance");
                if(miExport is not null)
                {
                    if(scalars.Count>0 && miExport.Data.FirstOrDefault(p=>p.Name.ToString()=="ScalarParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData sArray)
                    {
                        foreach(var item in sArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                        {
                            string? pName=(item.Value.FirstOrDefault(f=>f.Name.ToString()=="ParameterInfo") as UAssetAPI.PropertyTypes.Structs.StructPropertyData)?.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            if(pName!=null&&scalars.TryGetValue(pName,out var newVal)&&item.Value.FirstOrDefault(f=>f.Name.ToString()=="ParameterValue") is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fProp)
                                fProp.Value=newVal;
                        }
                    }
                    if(vectors.Count>0 && miExport.Data.FirstOrDefault(p=>p.Name.ToString()=="VectorParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vArray)
                    {
                        foreach(var item in vArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                        {
                            string? pName=(item.Value.FirstOrDefault(f=>f.Name.ToString()=="ParameterInfo") as UAssetAPI.PropertyTypes.Structs.StructPropertyData)?.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            if(pName!=null&&vectors.TryGetValue(pName,out var newCol)&&item.Value.FirstOrDefault(f=>f.Name.ToString()=="ParameterValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                                linCol.Value=new UAssetAPI.UnrealTypes.FLinearColor(newCol.R,newCol.G,newCol.B,newCol.A);
                        }
                    }
                }

                foreach(var exp in asset.Exports.OfType<UAssetAPI.ExportTypes.NormalExport>())
                {
                    var cls=exp.GetExportClassType().ToString();
                    if(!cls.StartsWith("MaterialExpression",StringComparison.Ordinal))continue;
                    var pName=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="ParameterName") as UAssetAPI.PropertyTypes.Objects.NamePropertyData)?.Value?.ToString()??exp.ObjectName.ToString();
                    if(cls.Contains("ScalarParameter",StringComparison.Ordinal)&&scalars.TryGetValue(pName,out var sVal))
                    {
                        if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fProp)
                            fProp.Value=sVal;
                    }
                    else if(cls.Contains("VectorParameter",StringComparison.Ordinal)&&vectors.TryGetValue(pName,out var vCol))
                    {
                        if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                            linCol.Value=new UAssetAPI.UnrealTypes.FLinearColor(vCol.R,vCol.G,vCol.B,vCol.A);
                    }
                }

                var mpcExport=asset.Exports.OfType<UAssetAPI.ExportTypes.NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="MaterialParameterCollection");
                if(mpcExport is not null)
                {
                    if(scalars.Count>0 && mpcExport.Data.FirstOrDefault(p=>p.Name.ToString()=="ScalarParameters") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData sArray)
                    {
                        foreach(var item in sArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                        {
                            var pName=item.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterName")?.RawValue?.ToString();
                            if(pName!=null&&scalars.TryGetValue(pName,out var sVal)&&item.Value.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fProp)
                                fProp.Value=sVal;
                        }
                    }
                    if(vectors.Count>0 && mpcExport.Data.FirstOrDefault(p=>p.Name.ToString()=="VectorParameters") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vArray)
                    {
                        foreach(var item in vArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                        {
                            var pName=item.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterName")?.RawValue?.ToString();
                            if(pName!=null&&vectors.TryGetValue(pName,out var vCol)&&item.Value.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                                linCol.Value=new UAssetAPI.UnrealTypes.FLinearColor(vCol.R,vCol.G,vCol.B,vCol.A);
                        }
                    }
                }

                var output=Path.Combine(stage,Path.GetFileName(path));
                asset.Write(output);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var files=Directory.GetFiles(stage).Select(f=>(Source:f,Destination:Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(f)))).ToArray();
                FileTransaction.Copy(files,false);
                return new{path,verified=true,sources=Sources(id,path)};
            }
            finally{Directory.Delete(stage,true);}
        }
    }
    public object Build(string id,string template,string target,byte[] image)
    {
        lock(assets){Validate(target);if(!target.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("Texture output must be uasset.");if(!target.StartsWith("UI/Textures/",StringComparison.Ordinal))throw new ArgumentException("Output must be under UI/Textures/.");if(image.Length>16*1024*1024)throw new ArgumentException("Upload too large.");
        var source=Resolve(id,template);var root=DraftRoot(id);var dest=Path.Combine(root,target);MusicWorkspaceStore.RejectLinks(dest);
        bool overwrite=string.Equals(template,target,StringComparison.Ordinal);
        if(!overwrite&&new[]{".uasset",".uexp",".ubulk"}.Any(ext=>File.Exists(Path.ChangeExtension(dest,ext))||content.Exists(assets.Get(id).ContentRoot!,Path.ChangeExtension(target,ext))))throw new IOException("Target already exists; choose a new resource path.");
        var staging=Path.Combine(assets.WorkspaceDirectory(id),"texture-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try{var output=Path.Combine(staging,Path.GetFileName(target));TextureAuthoring.Build(source,image,target,output);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if(overwrite)
        {
            var files=Directory.GetFiles(staging).Select(f=>(Source:f,Destination:Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(f)))).ToArray();
            FileTransaction.Copy(files,false);
        }
        else
        {
            var created=new List<string>();try{foreach(var file in Directory.GetFiles(staging)){var targetFile=Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(file));MusicWorkspaceStore.RejectLinks(targetFile);File.Copy(file,targetFile,false);created.Add(targetFile);}}catch{foreach(var file in created)File.Delete(file);throw;}
        }
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
    public object SaveWidgetProperty(string id,string path,SaveWidgetPropertyRequest request)
    {
        lock(assets)
        {
            Validate(path);
            if(!path.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("Only .uasset files are supported.");
            var source=Resolve(id,path);var dest=Path.Combine(DraftRoot(id),path);MusicWorkspaceStore.RejectLinks(dest);
            var stage=Path.Combine(assets.WorkspaceDirectory(id),"umg-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                var asset=new UAssetAPI.UAsset(source,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
                if(request.ExportIndex<0||request.ExportIndex>=asset.Exports.Count)
                    throw new ArgumentException($"ExportIndex {request.ExportIndex} is out of bounds.");
                if(asset.Exports[request.ExportIndex] is not UAssetAPI.ExportTypes.NormalExport normal)
                    throw new ArgumentException($"Export #{request.ExportIndex} is not a NormalExport.");

                if(!string.IsNullOrEmpty(request.Visibility))
                {
                    var visProp=normal.Data.OfType<UAssetAPI.PropertyTypes.Objects.BytePropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Visibility");
                    var visName=request.Visibility.StartsWith("ESlateVisibility::",StringComparison.Ordinal)?request.Visibility:("ESlateVisibility::"+request.Visibility);
                    if(visProp!=null)
                    {
                        visProp.EnumValue=new UAssetAPI.UnrealTypes.FName(asset,visName);
                    }
                    else
                    {
                        var newByte=new UAssetAPI.PropertyTypes.Objects.BytePropertyData(new UAssetAPI.UnrealTypes.FName(asset,"Visibility"));
                        newByte.ByteType=UAssetAPI.PropertyTypes.Objects.BytePropertyType.FName;
                        newByte.EnumType=new UAssetAPI.UnrealTypes.FName(asset,"ESlateVisibility");
                        newByte.EnumValue=new UAssetAPI.UnrealTypes.FName(asset,visName);
                        normal.Data.Add(newByte);
                    }
                }

                if(request.RenderOpacity.HasValue)
                {
                    var opProp=normal.Data.OfType<UAssetAPI.PropertyTypes.Objects.FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="RenderOpacity");
                    if(opProp!=null)
                    {
                        opProp.Value=(float)request.RenderOpacity.Value;
                    }
                    else
                    {
                        var newOp=new UAssetAPI.PropertyTypes.Objects.FloatPropertyData(new UAssetAPI.UnrealTypes.FName(asset,"RenderOpacity"));
                        newOp.Value=(float)request.RenderOpacity.Value;
                        normal.Data.Add(newOp);
                    }
                }

                var output=Path.Combine(stage,Path.GetFileName(path));
                asset.Write(output);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var files=Directory.GetFiles(stage).Select(f=>(Source:f,Destination:Path.Combine(Path.GetDirectoryName(dest)!,Path.GetFileName(f)))).ToArray();
                FileTransaction.Copy(files,false);
                return new{path,exportIndex=request.ExportIndex,verified=true,sources=Sources(id,path)};
            }
            finally{Directory.Delete(stage,true);}
        }
    }
}
public sealed record SaveWidgetPropertyRequest(int ExportIndex,string? Visibility=null,double? RenderOpacity=null);
public sealed record CueEditRequest(string Operation,string Bank,int CueId,int TemplateId=88,string? Name=null,string? SpeakerBase64=null,string? HeadphoneBase64=null);
public sealed record ReplaceAudioRequest(string Bank,ushort WaveId,string HcaBase64);
public sealed record BuildTextureRequest(string Template,string Target,string ImageBase64);
public sealed record SaveTextRequest(string Content);
public sealed record VectorColorValue(float R,float G,float B,float A=1f);
public sealed record SaveMaterialParamsRequest(Dictionary<string,float>? Scalars,Dictionary<string,VectorColorValue>? Vectors);
