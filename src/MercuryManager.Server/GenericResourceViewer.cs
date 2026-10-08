using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using System.Reflection;
namespace MercuryManager.Server;

public static class GenericResourceViewer
{
    private static string ReferenceName(FPackageIndex index,UAsset asset)
    {
        if(index.Index==0)return "None";
        if(index.Index<0 && -(long)index.Index<=asset.Imports.Count)return asset.Imports[-index.Index-1].ObjectName.ToString();
        if(index.Index>0 && index.Index<=asset.Exports.Count)return asset.Exports[index.Index-1].ObjectName.ToString();
        return "Unresolved";
    }
    private static JToken FieldValue(object? value,UAsset asset)
    {
        if(value is null)return JValue.CreateNull();
        if(value is FPackageIndex index)return new JValue($"{index.Index} / {ReferenceName(index,asset)}");
        if(value is FName or FString || value.GetType().IsEnum)return new JValue(value.ToString());
        if(value is byte[] bytes)return new JObject{["Bytes"]=bytes.Length,["HexPreview"]=Convert.ToHexString(bytes.AsSpan(0,Math.Min(bytes.Length,128)))};
        var json=asset.SerializeJsonObject(value);
        if(json.Length>1024*1024)return new JValue("Parsed field exceeds display limit");
        return JToken.Parse(json);
    }
    private static JObject Fields(object value,UAsset asset)
    {
        var result=new JObject();foreach(var field in value.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))result[field.Name]=FieldValue(field.GetValue(value),asset);return result;
    }
    public static object Read(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Asset header exceeds viewer limits.");
        var companion=Path.ChangeExtension(path,".uexp");if(File.Exists(companion)&&new FileInfo(companion).Length>128*1024*1024)throw new InvalidDataException("Asset export exceeds viewer limits.");
        var asset=new UAsset(path,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
        var described=System.Text.Json.JsonSerializer.SerializeToElement(Describe(asset));
        var data=JObject.Parse(described.GetProperty("data").GetRawText());
        if(described.GetProperty("isTexture").GetBoolean())
        {
            try
            {
                var texture=TextureAuthoring.Read(path,out var provider);
                using(provider)
                {
                    var platform=texture.PlatformData;
                    var node=((JArray)data["Exports"]!).OfType<JObject>().Single(n=>n["Name"]?.ToString()==texture.Name);
                    node["CustomSerialization"]=new JObject
                    {
                        ["Parser"]="CUE4Parse Texture2D",["SizeX"]=platform.SizeX,["SizeY"]=platform.SizeY,
                        ["PixelFormat"]=platform.PixelFormat,["PackedData"]=platform.PackedData,
                        ["FirstMipToSerialize"]=platform.FirstMipToSerialize,
                        ["Mips"]=new JArray(platform.Mips.Select((m,index)=>new JObject
                        {
                            ["Name"]=$"Mip {index} ({m.SizeX} × {m.SizeY})",
                            ["SizeX"]=m.SizeX,["SizeY"]=m.SizeY,["SizeZ"]=m.SizeZ,
                            ["BulkData"]=m.BulkData is null?JValue.CreateNull():new JObject
                            {
                                ["Flags"]=m.BulkData.Header.BulkDataFlags.ToString(),
                                ["ElementCount"]=m.BulkData.Header.ElementCount,
                                ["SizeOnDisk"]=m.BulkData.Header.SizeOnDisk,
                                ["OffsetInFile"]=m.BulkData.Header.OffsetInFile.ToString()
                            }
                        })),
                        ["RawBytes"]=asset.Exports.First(e=>e.ObjectName.ToString()==texture.Name).Extras?.Length??0
                    };
                }
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                foreach(var node in ((JArray)data["Exports"]!).OfType<JObject>().Where(n=>n["Class"]?.ToString()=="Texture2D"))
                    if(node["CustomSerialization"] is JObject raw)raw["TextureParseError"]=ex.Message;
            }
        }
        if(asset.Exports.Any(e=>e.GetExportClassType().ToString()=="SoundAtomCueSheet"))
        {
            try
            {
                var parsed=JObject.Parse(System.Text.Json.JsonSerializer.Serialize(CriAudio.DescribeSheet(path)));
                foreach(var node in ((JArray)data["Exports"]!).OfType<JObject>().Where(n=>n["Class"]?.ToString()=="SoundAtomCueSheet"))node["CustomSerialization"]=parsed.DeepClone();
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {foreach(var node in ((JArray)data["Exports"]!).OfType<JObject>().Where(n=>n["Class"]?.ToString()=="SoundAtomCueSheet"))if(node["CustomSerialization"] is JObject raw)raw["CueSheetParseError"]=ex.Message;}
        }
        return new{isTexture=described.GetProperty("isTexture").GetBoolean(),data=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data.ToString(Newtonsoft.Json.Formatting.None))};
    }
    public static byte[] ReadRaw(string path,int exportIndex)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Asset header exceeds viewer limits.");
        var companion=Path.ChangeExtension(path,".uexp");
        if(File.Exists(companion)&&new FileInfo(companion).Length>128*1024*1024)throw new InvalidDataException("Asset export exceeds viewer limits.");
        var asset=new UAsset(path,EngineVersion.VER_UE4_19);
        if(exportIndex<0||exportIndex>=asset.Exports.Count)throw new InvalidDataException("Invalid export index.");
        return asset.Exports[exportIndex].Extras??Array.Empty<byte>();
    }
    private static JObject ResolveReference(int index,UAsset asset)
    {
        var result=new JObject{["Index"]=index,["ObjectName"]=ReferenceName(new FPackageIndex(index),asset)};
        if(index==0){result["Status"]="Null reference";return result;}
        var seen=new HashSet<int>();var chain=new JArray();string? package=null;int current=index;
        while(current!=0&&seen.Add(current))
        {
            if(current<0&&-(long)current<=asset.Imports.Count)
            {
                var import=asset.Imports[-current-1];var name=import.ObjectName.ToString();
                chain.Add(new JObject{["Index"]=current,["Name"]=name});
                if(import.ClassName.ToString()=="Package")package=name;
                if(current==index)result["Class"]=import.ClassName.ToString();
                current=import.OuterIndex.Index;
            }
            else if(current>0&&current<=asset.Exports.Count)
            {var export=asset.Exports[current-1];chain.Add(new JObject{["Index"]=current,["Name"]=export.ObjectName.ToString()});current=export.OuterIndex.Index;}
            else{result["Status"]="Unresolved index";break;}
        }
        result["OuterChain"]=chain;
        if(package is not null){result["Package"]=package;if(package.StartsWith("/Game/",StringComparison.Ordinal))result["ContentFile"]=package[6..]+".uasset";}
        else if(index>0)result["Scope"]="Current asset export";
        return result;
    }
    private static void AnnotateReferences(JToken token,UAsset asset)
    {
        if(token is JObject obj)
        {
            foreach(var property in obj.Properties().ToArray())AnnotateReferences(property.Value,asset);
            if(obj["$type"]?.ToString().Contains("ObjectPropertyData",StringComparison.Ordinal)==true&&obj["Value"]?.Type==JTokenType.Integer)
                obj["[Reference]"]=ResolveReference(obj["Value"]!.Value<int>(),asset);
        }
        else if(token is JArray array)foreach(var child in array)AnnotateReferences(child,asset);
    }
    public static object Describe(UAsset asset)
    {
        var exports=new JArray();int budget=4*1024*1024;
        JToken Serialize(object value){var json=asset.SerializeJsonObject(value);budget-=json.Length;if(budget<0)throw new InvalidDataException("Parsed data exceeds viewer limits.");return JToken.Parse(json);}
        foreach(var export in asset.Exports)
        {
            var node=new JObject{["Name"]=export.ObjectName.ToString(),["Class"]=export.GetExportClassType().ToString(),["Parser"]=export.GetType().Name,["SerialOffset"]=export.SerialOffset.ToString(),["SerialSize"]=export.SerialSize.ToString(),["Flags"]=export.ObjectFlags.ToString()};
            if(export is NormalExport normal){var props=new JObject();foreach(var p in normal.Data){var value=Serialize(p);AnnotateReferences(value,asset);props[p.Name+"["+p.ArrayIndex+"]"]=value;}node["Properties"]=props;}
            if(export is PropertyExport property && property.Property is not null)node["UProperty"]=Fields(property.Property,asset);
            // Surface parsed fields of specialized serializers, not just tagged UObject properties.
            var specialized=new JObject();
            for(var type=export.GetType();type!=null&&type!=typeof(NormalExport)&&type!=typeof(Export);type=type.BaseType)
                foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    if(field.Name is not "Property" and not "Table")specialized[field.Name]=FieldValue(field.GetValue(export),asset);
            if(specialized.Count>0)node["ParsedData"]=specialized;
            if(export is DataTableExport table)node["Rows"]=Serialize(table.Table.Data);
            if(export is RawExport raw)node["RawData"]=new JObject{["Status"]="Unparsed export",["Bytes"]=raw.Data.Length,["HexPreview"]=Convert.ToHexString(raw.Data.AsSpan(0,Math.Min(128,raw.Data.Length)))};
            if(export.Extras is {Length:>0} extras)node["CustomSerialization"]=new JObject{["Status"]="Opaque bytes (not parsed by this viewer)",["Bytes"]=extras.Length,["HexPreview"]=Convert.ToHexString(extras.AsSpan(0,Math.Min(128,extras.Length)))};
            exports.Add(node);
        }
        var data=new JObject{["Header"]=new JObject{["EngineProfile"]="UE4.19",["Note"]="Read-only; parser profile is not proof of original engine version.",["Exports"]=asset.Exports.Count,["Imports"]=asset.Imports.Count},["NameMap"]=new JArray(asset.GetNameMapIndexList().Select(n=>n.ToString())),["Imports"]=new JArray(asset.Imports.Select((i,index)=>new JObject{["Index"]=-(index+1),["ObjectName"]=i.ObjectName.ToString(),["ClassPackage"]=i.ClassPackage.ToString(),["ClassName"]=i.ClassName.ToString(),["OuterIndex"]=i.OuterIndex.Index,["OuterName"]=ReferenceName(i.OuterIndex,asset),["Optional"]=i.bImportOptional})),["Exports"]=exports};
        // Convert Newtonsoft tokens to System.Text.Json values for the standard API envelope.
        return new{isTexture=asset.Exports.Any(e=>e.GetExportClassType().ToString()=="Texture2D"),data=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data.ToString(Newtonsoft.Json.Formatting.None))};
    }
}
