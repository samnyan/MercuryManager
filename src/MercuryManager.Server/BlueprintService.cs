using System.Text.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace MercuryManager.Server;

// Keep at most one parsed package; details reuse it without reparsing thousands of exports.
public sealed class BlueprintService
{
    private readonly object gate=new();
    private string? cachedPath;
    private string? stamp;
    private UAsset? cached;
    private UMGDocument? cachedModel;
    private UAsset Load(string path)
    {
        var files=new[]{path,Path.ChangeExtension(path,".uexp"),Path.ChangeExtension(path,".ubulk")};
        var signature=string.Join("|",files.Select(p=>File.Exists(p)?$"{new FileInfo(p).Length}:{File.GetLastWriteTimeUtc(p).Ticks}":"missing"));
        if(cachedPath!=path||stamp!=signature){cached=null;cachedModel=null;cachedPath=null;var asset=new UAsset(path,EngineVersion.VER_UE4_19);cached=asset;cachedPath=path;stamp=signature;}
        return cached!;
    }
    private static int ObjectIndex(NormalExport e,string name)=>e.Data.OfType<ObjectPropertyData>().FirstOrDefault(p=>p.Name.ToString()==name)?.Value.Index??0;
    private static JsonElement Json(JToken token)=>JsonSerializer.Deserialize<JsonElement>(token.ToString(Newtonsoft.Json.Formatting.None));
    private static JObject? ResolveAnimationTracks(int movieSceneIndex,UAsset asset)
    {
        if(movieSceneIndex<=0||movieSceneIndex>asset.Exports.Count)return null;
        if(asset.Exports[movieSceneIndex-1] is not NormalExport ms)return null;
        var info=new JObject();
        var playbackRange=ms.Data.OfType<StructPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="PlaybackRange");
        if(playbackRange!=null)
        {
            var upper=playbackRange.Value.OfType<StructPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="UpperBound");
            var val=upper?.Value.OfType<FloatPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Value")?.Value;
            if(val!=null)info["duration"]=val.Value;
        }
        var bindingsArray=ms.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="ObjectBindings");
        var bindingsList=new JArray();
        if(bindingsArray!=null)
        {
            foreach(var elem in bindingsArray.Value.OfType<StructPropertyData>())
            {
                var bindingName=elem.Value.OfType<StrPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="BindingName")?.Value?.ToString();
                var guidProp=elem.Value.OfType<StructPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="ObjectGuid");
                var guid=guidProp?.Value.OfType<GuidPropertyData>().FirstOrDefault()?.Value.ToString();
                var tracksProp=elem.Value.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="Tracks");
                var trackList=new JArray();
                if(tracksProp!=null)
                {
                    foreach(var trackObj in tracksProp.Value.OfType<ObjectPropertyData>())
                    {
                        var trackIdx=trackObj.Value.Index;
                        if(trackIdx>0&&trackIdx<=asset.Exports.Count&&asset.Exports[trackIdx-1] is NormalExport trackExport)
                        {
                            var propName=trackExport.Data.OfType<NamePropertyData>().FirstOrDefault(p=>p.Name.ToString()=="PropertyName")?.Value?.ToString();
                            var propPath=trackExport.Data.OfType<StrPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="PropertyPath")?.Value?.ToString();
                            var trackItem=new JObject{["trackName"]=trackExport.ObjectName.ToString(),["trackType"]=trackExport.GetExportClassType().ToString()};
                            if(!string.IsNullOrEmpty(propName))trackItem["property"]=propName;
                            if(!string.IsNullOrEmpty(propPath))trackItem["propertyPath"]=propPath;
                            trackList.Add(trackItem);
                        }
                    }
                }
                var bindingItem=new JObject{["name"]=bindingName??string.Empty,["guid"]=guid??string.Empty,["tracks"]=trackList};
                bindingsList.Add(bindingItem);
            }
        }
        info["bindings"]=bindingsList;
        return info;
    }
    private static JObject? ResolveWidgetAnimation(NormalExport wa,UAsset asset)
    {
        var msIdx=ObjectIndex(wa,"MovieScene");
        var msInfo=ResolveAnimationTracks(msIdx,asset)??new JObject();
        var bindings=new JArray();
        var animBindings=wa.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()=="AnimationBindings");
        if(animBindings!=null)
        {
            foreach(var item in animBindings.Value.OfType<StructPropertyData>())
            {
                var wName=(item.Value.FirstOrDefault(p=>p.Name.ToString()=="WidgetName") as NamePropertyData)?.Value?.ToString();
                var slotWName=(item.Value.FirstOrDefault(p=>p.Name.ToString()=="SlotWidgetName") as NamePropertyData)?.Value?.ToString();
                var isRoot=(item.Value.FirstOrDefault(p=>p.Name.ToString()=="bIsRootWidget") as BoolPropertyData)?.Value??false;
                var guid=(item.Value.FirstOrDefault(p=>p.Name.ToString()=="AnimationGuid") as StructPropertyData)?.Value.OfType<GuidPropertyData>().FirstOrDefault()?.Value.ToString();
                var bObj=new JObject{["widgetName"]=wName??string.Empty,["slotWidgetName"]=slotWName??string.Empty,["bIsRootWidget"]=isRoot};
                if(!string.IsNullOrEmpty(guid))bObj["guid"]=guid;
                bindings.Add(bObj);
            }
        }
        msInfo["widgetBindings"]=bindings;
        return msInfo;
    }
    public object Font(string path)
    {
        lock(gate)
        {
            var asset=Load(path);
            var font=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="Font")
                ??throw new ArgumentException("Resource is not a Font asset.");
            var resources=new List<UMGResource>();var diagnostics=new List<string>();
            var properties=new UMGPropertyReader(asset).Read(font,new[]{"FontCacheType","LegacyFontSize","CompositeFont"},resources,diagnostics);
            return new { schemaVersion=1, properties, resources=resources.Distinct().ToList(), diagnostics, preview="metadata-only" };
        }
    }
    public UMGDocument UMG(string path,string assetPath)
    {
        lock(gate)
        {
            var asset=Load(path);
            var revision=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path+"|"+stamp)));
            if(cachedModel==null||cachedModel.AssetPath!=assetPath)cachedModel=UMGModelBuilder.Build(asset,assetPath,revision);
            return cachedModel;
        }
    }
    public List<UMGAnimation> Animations(string path)
    {
        lock(gate) return UMGModelBuilder.Build(Load(path), path, string.Empty).Animations ?? [];
    }
    public object Read(string path)
    {
        lock(gate)
        {
            var asset=Load(path);
            var parents=new Dictionary<int,int>();var slots=new Dictionary<int,int>();
            for(var i=0;i<asset.Exports.Count;i++)
            {
                if(asset.Exports[i] is not NormalExport e)continue;
                var content=ObjectIndex(e,"Content");var parent=ObjectIndex(e,"Parent");
                if(content>0&&parent>0&&content<=asset.Exports.Count&&parent<=asset.Exports.Count){parents[content-1]=parent-1;slots[content-1]=i;}
                var slot=ObjectIndex(e,"Slot");
                if(slot>0&&slot<=asset.Exports.Count&&asset.Exports[slot-1] is NormalExport s){var owner=ObjectIndex(s,"Parent");if(owner>0&&owner<=asset.Exports.Count){parents[i]=owner-1;slots[i]=slot-1;}}
            }
            var nodes=asset.Exports.Select((e,i)=>
            {
                var type=e.GetExportClassType().ToString();var outer=e.OuterIndex.Index;
                var widget=outer>0&&outer<=asset.Exports.Count&&asset.Exports[outer-1].GetExportClassType().ToString()=="WidgetTree"&&!type.EndsWith("Slot",StringComparison.Ordinal);
                var group=widget?"widgets":type.Contains("Animation",StringComparison.Ordinal)||type.StartsWith("MovieScene",StringComparison.Ordinal)?"animations":type.EndsWith("Slot",StringComparison.Ordinal)?"slots":"objects";
                return new{index=i,name=e.ObjectName.ToString(),type,group,outerIndex=outer-1,parentIndex=parents.TryGetValue(i,out var parent)?(int?)parent:null,slotIndex=slots.TryGetValue(i,out var slot)?(int?)slot:null};
            }).ToArray();
            var references=new JArray();
            for(var i=0;i<asset.Imports.Count;i++){var r=GenericResourceViewer.ResolveReference(-(i+1),asset);if(r["ContentFile"]!=null)references.Add(r);}
            return new{nodes,references=Json(references),note="Serialized hierarchy and defaults only; runtime bindings and rendering are not evaluated."};
        }
    }
    public object Node(string path,int index)
    {
        lock(gate)
        {
            var asset=Load(path);
            if(index<0||index>=asset.Exports.Count)throw new ArgumentException("Invalid Blueprint export index.");
            var export=asset.Exports[index];var properties=new JObject();var references=new JArray();var internalReferences=new JArray();var seenInternal=new HashSet<int>();
            if(export is NormalExport normal)foreach(var p in normal.Data)
            {
                JToken value;
                try{value=JToken.Parse(asset.SerializeJsonObject(p));}
                catch(ArgumentNullException){value=new JObject{["Status"]="Property serializer unavailable",["Type"]=p.GetType().Name};}
                GenericResourceViewer.AnnotateReferences(value,asset);
                properties[$"{p.Name}[{p.ArrayIndex}]"]=value;
                foreach(var obj in (value is JContainer container?container.DescendantsAndSelf():Enumerable.Empty<JToken>()).OfType<JObject>())
                {
                    if(obj["ContentFile"]!=null)references.Add(obj.DeepClone());
                    if(obj["[Reference]"] is JObject reference&&reference["Index"]?.Value<int>() is int target&&target>0&&target<=asset.Exports.Count&&seenInternal.Add(target))
                    {
                        var linked=asset.Exports[target-1];
                        internalReferences.Add(new JObject{["index"]=target-1,["name"]=linked.ObjectName.ToString(),["type"]=linked.GetExportClassType().ToString(),["property"]=p.Name.ToString()});
                    }
                }
            }
            JObject? animationDetails = null;
            if (export.GetExportClassType().ToString() == "WidgetAnimation" && export is NormalExport wa)
            {
                animationDetails = ResolveWidgetAnimation(wa, asset);
            }
            else if (export.GetExportClassType().ToString() == "MovieScene")
            {
                animationDetails = ResolveAnimationTracks(index + 1, asset);
            }
            return new{name=export.ObjectName.ToString(),type=export.GetExportClassType().ToString(),properties=Json(properties),references=Json(references),internalReferences=Json(internalReferences),animation=animationDetails!=null?Json(animationDetails):(object?)null,opaqueBytes=export.Extras?.Length??0};
        }
    }
}
