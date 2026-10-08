using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;

public static class ResourceJson
{
    public static string Export(string path)=>Load(path).SerializeJson(Newtonsoft.Json.Formatting.Indented);
    private static UAsset Load(string path)
    {
        if(!path.EndsWith(".uasset",StringComparison.Ordinal))throw new ArgumentException("JSON requires a uasset.");
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Asset too large.");
        var companion=Path.ChangeExtension(path,".uexp");if(File.Exists(companion)&&new FileInfo(companion).Length>128*1024*1024)throw new InvalidDataException("Asset export too large.");
        return new UAsset(path,EngineVersion.VER_UE4_19);
    }
    public static void Import(string source,string json,string output)
    {
        if(json.Length>32*1024*1024)throw new ArgumentException("JSON too large.");
        var original=Load(source);var baseline=JObject.Parse(original.SerializeJson());
        using var reader=new Newtonsoft.Json.JsonTextReader(new StringReader(json)){MaxDepth=128};
        var document=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
        if(reader.Read())throw new InvalidDataException("Trailing JSON content.");
        // Only permit type names already present in this asset's trusted API serialization.
        var allowed=baseline.Descendants().OfType<JProperty>().Where(p=>p.Name=="$type").Select(p=>p.Value.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach(var p in document.Descendants().OfType<JProperty>())
            if(p.Name=="$type"&&!allowed.Contains(p.Value.ToString()))throw new InvalidDataException("JSON contains an unsupported type.");
        if(!JToken.DeepEquals(document["$type"],baseline["$type"]))throw new InvalidDataException("Not a compatible UAssetAPI JSON document.");
        var asset=UAsset.DeserializeJson(json)??throw new InvalidDataException("Invalid asset JSON.");
        if(asset.Exports.Count!=original.Exports.Count)throw new InvalidDataException("Changing export count is not supported by basic JSON editing.");
        for(int i=0;i<asset.Exports.Count;i++)
            if(asset.Exports[i].ObjectName.ToString()!=original.Exports[i].ObjectName.ToString()||asset.Exports[i].GetExportClassType().ToString()!=original.Exports[i].GetExportClassType().ToString())throw new InvalidDataException("Changing export identity is not supported.");
        asset.FilePath=source;asset.Write(output);
        var reread=Load(output);
        for(int i=0;i<original.Exports.Count;i++)
        {
            var before=original.Exports[i];var after=reread.Exports[i];
            if(before.Extras is {Length:>0} && (!before.Extras.SequenceEqual(after.Extras??[])||before.SerialOffset+before.SerialSize-before.Extras.Length!=after.SerialOffset+after.SerialSize-(after.Extras?.Length??0)))
                throw new InvalidDataException("Opaque CustomSerialization changed or relocated; this edit requires a specialized writer.");
        }
        if(!reread.VerifyBinaryEquality())throw new InvalidDataException("Rebuilt asset failed binary roundtrip verification.");
        foreach(var ext in new[]{".ubulk"}){var file=Path.ChangeExtension(source,ext);if(File.Exists(file))File.Copy(file,Path.ChangeExtension(output,ext),true);}
    }
}
public sealed record ImportResourceJsonRequest(string Json);
