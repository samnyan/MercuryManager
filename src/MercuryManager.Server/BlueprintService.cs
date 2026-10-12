using System.Text.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace MercuryManager.Server;

// Keep at most one parsed package; details reuse it without reparsing thousands of exports.
public sealed class BlueprintService
{
    private readonly object gate=new();
    private string? cachedPath;
    private string? stamp;
    private UAsset? cached;
    private UAsset Load(string path)
    {
        var files=new[]{path,Path.ChangeExtension(path,".uexp"),Path.ChangeExtension(path,".ubulk")};
        var signature=string.Join("|",files.Select(p=>File.Exists(p)?$"{new FileInfo(p).Length}:{File.GetLastWriteTimeUtc(p).Ticks}":"missing"));
        if(cachedPath!=path||stamp!=signature){cached=null;cachedPath=null;var asset=new UAsset(path,EngineVersion.VER_UE4_19);cached=asset;cachedPath=path;stamp=signature;}
        return cached!;
    }
    private static int ObjectIndex(NormalExport e,string name)=>e.Data.OfType<ObjectPropertyData>().FirstOrDefault(p=>p.Name.ToString()==name)?.Value.Index??0;
    private static JsonElement Json(JToken token)=>JsonSerializer.Deserialize<JsonElement>(token.ToString(Newtonsoft.Json.Formatting.None));
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
            var export=asset.Exports[index];var properties=new JObject();var references=new JArray();
            if(export is NormalExport normal)foreach(var p in normal.Data)
            {
                JToken value;
                try{value=JToken.Parse(asset.SerializeJsonObject(p));}
                catch(ArgumentNullException){value=new JObject{["Status"]="Property serializer unavailable",["Type"]=p.GetType().Name};}
                GenericResourceViewer.AnnotateReferences(value,asset);
                properties[$"{p.Name}[{p.ArrayIndex}]"]=value;
                foreach(var reference in (value is JContainer container?container.DescendantsAndSelf():Enumerable.Empty<JToken>()).OfType<JObject>().Where(o=>o["ContentFile"]!=null))references.Add(reference.DeepClone());
            }
            return new{name=export.ObjectName.ToString(),type=export.GetExportClassType().ToString(),properties=Json(properties),references=Json(references),opaqueBytes=export.Extras?.Length??0};
        }
    }
}
