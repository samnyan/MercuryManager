using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
namespace MercuryManager.Server;

public static class GenericResourceViewer
{
    public static object Read(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Asset header exceeds viewer limits.");
        var companion=Path.ChangeExtension(path,".uexp");if(File.Exists(companion)&&new FileInfo(companion).Length>128*1024*1024)throw new InvalidDataException("Asset export exceeds viewer limits.");
        var asset=new UAsset(path,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
        return Describe(asset);
    }
    public static object Describe(UAsset asset)
    {
        var exports=new JArray();int budget=4*1024*1024;
        JToken Serialize(object value){var json=asset.SerializeJsonObject(value);budget-=json.Length;if(budget<0)throw new InvalidDataException("Parsed data exceeds viewer limits.");return JToken.Parse(json);}
        foreach(var export in asset.Exports)
        {
            var node=new JObject{["Name"]=export.ObjectName.ToString(),["Class"]=export.GetExportClassType().ToString(),["Parser"]=export.GetType().Name,["SerialOffset"]=export.SerialOffset.ToString(),["SerialSize"]=export.SerialSize.ToString(),["Flags"]=export.ObjectFlags.ToString()};
            if(export is NormalExport normal){var props=new JObject();foreach(var p in normal.Data)props[p.Name+"["+p.ArrayIndex+"]"]=Serialize(p);node["Properties"]=props;}
            if(export is DataTableExport table)node["Rows"]=Serialize(table.Table.Data);
            if(export is RawExport raw)node["RawData"]=new JObject{["Status"]="Unparsed export",["Bytes"]=raw.Data.Length,["HexPreview"]=Convert.ToHexString(raw.Data.AsSpan(0,Math.Min(128,raw.Data.Length)))};
            if(export.Extras is {Length:>0} extras)node["CustomSerialization"]=new JObject{["Status"]="Opaque bytes (not parsed by this viewer)",["Bytes"]=extras.Length,["HexPreview"]=Convert.ToHexString(extras.AsSpan(0,Math.Min(128,extras.Length)))};
            exports.Add(node);
        }
        var data=new JObject{["Header"]=new JObject{["EngineProfile"]="UE4.19",["Note"]="Read-only; parser profile is not proof of original engine version.",["Exports"]=asset.Exports.Count,["Imports"]=asset.Imports.Count},["NameMap"]=new JArray(asset.GetNameMapIndexList().Select(n=>n.ToString())),["Imports"]=Serialize(asset.Imports),["Exports"]=exports};
        // Convert Newtonsoft tokens to System.Text.Json values for the standard API envelope.
        return new{isTexture=asset.Exports.Any(e=>e.GetExportClassType().ToString()=="Texture2D"),data=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data.ToString(Newtonsoft.Json.Formatting.None))};
    }
}
