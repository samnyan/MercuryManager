using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace MercuryManager.Server;

internal sealed class UMGPropertyReader(UAsset asset)
{
    public static int Index(NormalExport? e,string name)=>e?.Data.OfType<ObjectPropertyData>().FirstOrDefault(p=>p.Name.ToString()==name)?.Value.Index??0;
    public static IEnumerable<int> Indices(NormalExport? e,string name)=>e?.Data.OfType<ArrayPropertyData>().FirstOrDefault(p=>p.Name.ToString()==name)?.Value.OfType<ObjectPropertyData>().Select(p=>p.Value.Index)??[];
    public Dictionary<string,object?> Read(NormalExport? export,IEnumerable<string> names,List<UMGResource> resources,List<string> issues)
    {
        var result=new Dictionary<string,object?>();
        if(export==null)return result;
        foreach(var name in names)
        {
            var property=export.Data.FirstOrDefault(p=>p.Name.ToString()==name);
            if(property==null)continue;
            try{result[name]=Value(JToken.Parse(asset.SerializeJsonObject(property)),name,resources);}
            catch(Exception ex) when(ex is Newtonsoft.Json.JsonException or ArgumentNullException or NotSupportedException){issues.Add($"Cannot decode {name}: {ex.GetType().Name}");}
        }
        return result;
    }
    private object? Value(JToken token,string role,List<UMGResource> resources)
    {
        if(token is JValue scalar){if(scalar.Type==JTokenType.String&&scalar.Value is string text&&(text=="+0"||text=="-0"))return 0d;return scalar.Value;}
        if(token is JArray array)
        {
            if(array.Count==1&&array[0] is JObject singleton&&singleton["Name"]?.ToString()==role)return Value(singleton,role,resources);
            if(array.All(t=>t is JObject o&&o["Name"]!=null))return array.OfType<JObject>().GroupBy(o=>o["Name"]!.ToString()).ToDictionary(g=>g.Key,g=>Value(g.First(),g.Key,resources));
            return array.Select(t=>Value(t,role,resources)).ToArray();
        }
        if(token is not JObject obj)return null;
        if(obj.TryGetValue("EnumType",out var enumType)&&obj.TryGetValue("EnumValue",out var enumVal))
        {
            var s=enumVal?.ToString();
            if(s!=null)
            {
                var idx=s.IndexOf("::",StringComparison.Ordinal);
                return idx>=0?s[(idx+2)..]:s;
            }
        }
        var type=obj["$type"]?.ToString()??"";
        if(type.Contains("ObjectPropertyData")&&obj["Value"]?.Type==JTokenType.Integer)
        {
            var index=obj["Value"]!.Value<int>();var reference=GenericResourceViewer.ResolveReference(index,asset);
            var path=reference["ContentFile"]?.ToString();var className=reference["Class"]?.ToString();
            if(path!=null)resources.Add(new(role,path,className));
            return new{exportIndex=index>0?(int?)(index-1):null,assetPath=path,className};
        }
        if(obj.TryGetValue("Value",out var value))
        {
            var decoded=Value(value,role,resources);
            if((type.Contains("EnumPropertyData")||type.Contains("BytePropertyData"))&&decoded is string enumName)
            {
                var separator=enumName.LastIndexOf("::",StringComparison.Ordinal);
                return separator>=0?enumName[(separator+2)..]:enumName;
            }
            return decoded;
        }
        return obj.Properties().Where(p=>!p.Name.StartsWith('$')).ToDictionary(p=>p.Name,p=>Value(p.Value,p.Name,resources));
    }
}
