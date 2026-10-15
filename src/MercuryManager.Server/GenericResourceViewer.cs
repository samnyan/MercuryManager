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
    public static object Read(string path,Func<string,string>? resolve=null)
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
                            ["MipIndex"]=index,
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
        var miExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString() is "MaterialInstanceConstant" or "MaterialInstance");
        if(miExport is not null)
        {
            try
            {
                var miProps=miExport.Data;
                string? parentObjName=null,parentPackage=null;
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="Parent") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData parentProp)
                {
                    var res=ResolveReference(parentProp.Value.Index,asset);
                    parentObjName=res["ObjectName"]?.ToString();
                    parentPackage=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                var scalarOverrides=new JArray();
                var scalars=new Dictionary<string,float>();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="ScalarParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData scalarArray)
                {
                    foreach(var item in scalarArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;float? pVal=null;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Objects.FloatPropertyData floatVal)
                                pVal=floatVal.Value;
                        }
                        if(pName!=null&&pVal!=null)
                        {
                            scalars[pName]=pVal.Value;
                            scalarOverrides.Add(new JObject{["ParameterName"]=pName,["Value"]=pVal.Value});
                        }
                    }
                }
                var vectorOverrides=new JArray();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="VectorParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vectorArray)
                {
                    foreach(var item in vectorArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;float r=0,g=0,b=0,a=1;bool hasVal=false;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                            {
                                r=linCol.Value.R;g=linCol.Value.G;b=linCol.Value.B;a=linCol.Value.A;hasVal=true;
                            }
                        }
                        if(pName!=null&&hasVal)
                            vectorOverrides.Add(new JObject{["ParameterName"]=pName,["R"]=r,["G"]=g,["B"]=b,["A"]=a});
                    }
                }
                var textures=new Dictionary<string,string>();
                var textureOverrides=new JArray();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="TextureParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData texArray)
                {
                    foreach(var item in texArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;string? tFile=null;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData objVal)
                            {
                                var res=ResolveReference(objVal.Value.Index,asset);
                                tFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                            }
                        }
                        if(pName!=null&&tFile!=null)
                        {
                            textures[pName]=tFile;
                            textureOverrides.Add(new JObject{["ParameterName"]=pName,["ContentFile"]=tFile});
                        }
                    }
                }
                var propOverrides=new JObject();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="BasePropertyOverrides") is UAssetAPI.PropertyTypes.Structs.StructPropertyData bpoStruct)
                {
                    foreach(var field in bpoStruct.Value)
                    {
                        var fn=field.Name.ToString();
                        if(field is UAssetAPI.PropertyTypes.Objects.BytePropertyData byteProp)
                            propOverrides[fn]=byteProp.EnumValue?.ToString()??byteProp.Value.ToString();
                        else if(field is UAssetAPI.PropertyTypes.Objects.BoolPropertyData boolProp)
                            propOverrides[fn]=boolProp.Value;
                        else if(field is UAssetAPI.PropertyTypes.Objects.FloatPropertyData floatProp)
                            propOverrides[fn]=floatProp.Value;
                    }
                }
                JObject? parentMatInfo=null;
                if(parentPackage!=null&&resolve!=null)
                {
                    try
                    {
                        var parentPath=resolve(parentPackage);
                        if(File.Exists(parentPath))
                        {
                            var parentAsset=new UAsset(parentPath,EngineVersion.VER_UE4_19);
                            parentMatInfo=ExtractMaterialInfo(parentAsset);
                            if(!textures.ContainsKey("tex"))
                            {
                                foreach(var imp in parentAsset.Imports.Where(i=>i.ClassName.ToString()=="Texture2D"))
                                {
                                    var res=ResolveReference(-(parentAsset.Imports.IndexOf(imp)+1),parentAsset);
                                    var cf=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                                    if(cf!=null){textures["tex"]=cf;break;}
                                }
                            }
                        }
                    }
                    catch{}
                }
                data["MaterialInstanceInfo"]=new JObject
                {
                    ["Name"]=miExport.ObjectName.ToString(),
                    ["Parent"]=parentObjName,
                    ["ParentPackage"]=parentPackage,
                    ["Scalars"]=JObject.FromObject(scalars),
                    ["Textures"]=JObject.FromObject(textures),
                    ["ScalarOverrides"]=scalarOverrides,
                    ["VectorOverrides"]=vectorOverrides,
                    ["TextureOverrides"]=textureOverrides,
                    ["BasePropertyOverrides"]=propOverrides,
                    ["ParentMaterialInfo"]=parentMatInfo
                };
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["MaterialInstanceError"]=ex.Message;
            }
        }
        var matExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="Material");
        if(matExport is not null)
        {
            try
            {
                data["MaterialInfo"]=ExtractMaterialInfo(asset);
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["MaterialError"]=ex.Message;
            }
        }
        var mfExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="MaterialFunction");
        if(mfExport is not null)
        {
            try
            {
                data["MaterialFunctionInfo"]=ExtractMaterialFunctionInfo(asset);
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["MaterialFunctionError"]=ex.Message;
            }
        }
        var mpcExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="MaterialParameterCollection");
        if(mpcExport is not null)
        {
            try
            {
                var scalarParams=new JArray();
                var vectorParams=new JArray();

                if(mpcExport.Data.FirstOrDefault(p=>p.Name.ToString()=="ScalarParameters") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData sArray)
                {
                    foreach(var item in sArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        var pName=item.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterName")?.RawValue?.ToString();
                        var defVal=(item.Value.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
                        var idVal=item.Value.FirstOrDefault(p=>p.Name.ToString()=="Id")?.RawValue?.ToString();
                        scalarParams.Add(new JObject{["ParameterName"]=pName??"Unnamed",["DefaultValue"]=defVal,["Id"]=idVal});
                    }
                }

                if(mpcExport.Data.FirstOrDefault(p=>p.Name.ToString()=="VectorParameters") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vArray)
                {
                    foreach(var item in vArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        var pName=item.Value.FirstOrDefault(p=>p.Name.ToString()=="ParameterName")?.RawValue?.ToString();
                        var idVal=item.Value.FirstOrDefault(p=>p.Name.ToString()=="Id")?.RawValue?.ToString();
                        float r=0,g=0,b=0,a=1;
                        if(item.Value.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                        {
                            r=linCol.Value.R;g=linCol.Value.G;b=linCol.Value.B;a=linCol.Value.A;
                        }
                        vectorParams.Add(new JObject{["ParameterName"]=pName??"Unnamed",["R"]=r,["G"]=g,["B"]=b,["A"]=a,["Id"]=idVal});
                    }
                }

                data["MpcInfo"]=new JObject
                {
                    ["Name"]=mpcExport.ObjectName.ToString(),
                    ["ScalarParameters"]=scalarParams,
                    ["VectorParameters"]=vectorParams
                };
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["MpcError"]=ex.Message;
            }
        }
        var movieExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="FileManaMovie");
        if(movieExport is not null)
        {
            try
            {
                var filePath=(movieExport.Data.FirstOrDefault(p=>p.Name.ToString()=="FilePath") as UAssetAPI.PropertyTypes.Objects.StrPropertyData)?.Value?.ToString();
                int width=0,height=0,totalFrames=0;float frameRate=0f;bool isAlpha=false;string? movieType=null;
                if(movieExport.Data.FirstOrDefault(p=>p.Name.ToString()=="VideoTracks") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vTracks && vTracks.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.StructPropertyData track)
                {
                    if(track.Value.FirstOrDefault(p=>p.Name.ToString()=="DisplayDimensions") is UAssetAPI.PropertyTypes.Structs.StructPropertyData dim && dim.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.IntPointPropertyData pt)
                    {
                        width=pt.Value[0];height=pt.Value[1];
                    }
                    else if(track.Value.FirstOrDefault(p=>p.Name.ToString()=="TextureDimensions") is UAssetAPI.PropertyTypes.Structs.StructPropertyData tDim && tDim.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.IntPointPropertyData tPt)
                    {
                        width=tPt.Value[0];height=tPt.Value[1];
                    }
                    frameRate=(track.Value.FirstOrDefault(p=>p.Name.ToString()=="FrameRate") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
                    totalFrames=(track.Value.FirstOrDefault(p=>p.Name.ToString()=="TotalFrames") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??0;
                    isAlpha=(track.Value.FirstOrDefault(p=>p.Name.ToString()=="bIsAlpha") as UAssetAPI.PropertyTypes.Objects.BoolPropertyData)?.Value??false;
                    movieType=(track.Value.FirstOrDefault(p=>p.Name.ToString()=="Type") as UAssetAPI.PropertyTypes.Objects.EnumPropertyData)?.Value?.ToString();
                }

                long usmBytes=0;bool usmExists=false;string? resolvedUsm=null;
                if(!string.IsNullOrEmpty(filePath)&&resolve!=null)
                {
                    try
                    {
                        resolvedUsm=resolve(filePath);
                        if(File.Exists(resolvedUsm))
                        {
                            usmExists=true;
                            usmBytes=new FileInfo(resolvedUsm).Length;
                        }
                    }
                    catch{}
                }

                float durationSec=frameRate>0?totalFrames/frameRate:0;
                data["MovieInfo"]=new JObject
                {
                    ["Name"]=movieExport.ObjectName.ToString(),
                    ["FilePath"]=filePath,
                    ["Width"]=width,
                    ["Height"]=height,
                    ["FrameRate"]=frameRate,
                    ["TotalFrames"]=totalFrames,
                    ["DurationSec"]=durationSec,
                    ["IsAlpha"]=isAlpha,
                    ["MovieType"]=movieType,
                    ["UsmExists"]=usmExists,
                    ["UsmBytes"]=usmBytes
                };
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["MovieError"]=ex.Message;
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
        var psExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="ParticleSystem");
        if(psExport is not null)
        {
            try
            {
                var psInfo=ReadParticleSystemData(path,resolve,asset);
                if(psInfo is not null)data["ParticleSystemInfo"]=psInfo;
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                data["ParticleSystemError"]=ex.Message;
            }
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
    internal static JObject ResolveReference(int index,UAsset asset)
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
    internal static void AnnotateReferences(JToken token,UAsset asset)
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
        var exports=new JArray();long budget=256L*1024*1024;
        JToken Serialize(object value){var json=asset.SerializeJsonObject(value);budget-=json.Length;if(budget<0)throw new InvalidDataException("Parsed data exceeds the 256 MiB viewer parse budget.");return JToken.Parse(json);}
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
        var data=new JObject{["Header"]=new JObject{["EngineProfile"]="UE4.19",["Note"]="Read-only; parser profile is not proof of original engine version.",["Exports"]=asset.Exports.Count,["Imports"]=asset.Imports.Count},["NameMap"]=new JArray(asset.GetNameMapIndexList().Select(n=>n.ToString())),["Imports"]=new JArray(asset.Imports.Select((i,index)=>{var res=ResolveReference(-(index+1),asset);return new JObject{["Index"]=-(index+1),["ObjectName"]=i.ObjectName.ToString(),["ClassPackage"]=i.ClassPackage.ToString(),["ClassName"]=i.ClassName.ToString(),["OuterIndex"]=i.OuterIndex.Index,["OuterName"]=ReferenceName(i.OuterIndex,asset),["Optional"]=i.bImportOptional,["ContentFile"]=res["ContentFile"],["Package"]=res["Package"]};})),["Exports"]=exports};
        // Convert Newtonsoft tokens to System.Text.Json values for the standard API envelope.
        return new{isTexture=asset.Exports.Any(e=>e.GetExportClassType().ToString()=="Texture2D"),data=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data.ToString(Newtonsoft.Json.Formatting.None))};
    }
    public static object DescribeParticles(UAsset asset)
    {
        JToken Serialize(object value) => JToken.Parse(asset.SerializeJsonObject(value));
        var exports=new JArray();
        foreach(var export in asset.Exports)
        {
            var props=new JObject();
            if(export is NormalExport normal)
                foreach(var p in normal.Data)
                    try { props[p.Name+"["+p.ArrayIndex+"]"]=Serialize(p); } catch { }
            var row=new JObject { ["Index"]=asset.Exports.IndexOf(export),["Name"]=export.ObjectName.ToString(),["Class"]=export.GetExportClassType().ToString(),["OuterIndex"]=export.OuterIndex.Index,["Properties"]=props };
            if(export.Extras is {Length:>0} bytes) row["OpaqueBytes"]=bytes.Length;
            exports.Add(row);
        }
        var type=asset.Exports.Select(e=>e.GetExportClassType().ToString()).FirstOrDefault(t=>t is "ParticleSystem" or "StaticMesh")??"Resource";
        return new { type, exports=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(exports.ToString(Newtonsoft.Json.Formatting.None)), imports=new JArray(asset.Imports.Select((i,index)=>new JObject{["Index"]=-(index+1),["ObjectName"]=i.ObjectName.ToString(),["ClassName"]=i.ClassName.ToString(),["Package"]=ReferenceName(i.OuterIndex,asset)})) };
    }
    public static JObject? ReadMaterialData(string path,Func<string,string>? resolve=null)
    {
        if(!File.Exists(path))return null;
        try
        {
            var asset=new UAsset(path,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
            var miExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString() is "MaterialInstanceConstant" or "MaterialInstance");
            if(miExport is not null)
            {
                var miProps=miExport.Data;
                string? parentObjName=null,parentPackage=null;
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="Parent") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData parentProp)
                {
                    var res=ResolveReference(parentProp.Value.Index,asset);
                    parentObjName=res["ObjectName"]?.ToString();
                    parentPackage=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                var scalarOverrides=new JArray();
                var scalars=new Dictionary<string,float>();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="ScalarParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData scalarArray)
                {
                    foreach(var item in scalarArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;float? pVal=null;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Objects.FloatPropertyData floatVal)
                                pVal=floatVal.Value;
                        }
                        if(pName!=null&&pVal!=null)
                        {
                            scalars[pName]=pVal.Value;
                            scalarOverrides.Add(new JObject{["ParameterName"]=pName,["Value"]=pVal.Value});
                        }
                    }
                }
                var vectorOverrides=new JArray();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="VectorParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData vectorArray)
                {
                    foreach(var item in vectorArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;float r=0,g=0,b=0,a=1;bool hasVal=false;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                            {
                                r=linCol.Value.R;g=linCol.Value.G;b=linCol.Value.B;a=linCol.Value.A;hasVal=true;
                            }
                        }
                        if(pName!=null&&hasVal)
                            vectorOverrides.Add(new JObject{["ParameterName"]=pName,["R"]=r,["G"]=g,["B"]=b,["A"]=a});
                    }
                }
                var textures=new Dictionary<string,string>();
                var textureOverrides=new JArray();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="TextureParameterValues") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData texArray)
                {
                    foreach(var item in texArray.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                    {
                        string? pName=null;string? tFile=null;
                        foreach(var field in item.Value)
                        {
                            if(field.Name.ToString()=="ParameterInfo" && field is UAssetAPI.PropertyTypes.Structs.StructPropertyData infoStruct)
                                pName=infoStruct.Value.FirstOrDefault(f=>f.Name.ToString()=="Name")?.RawValue?.ToString();
                            else if(field.Name.ToString()=="ParameterValue" && field is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData objVal)
                            {
                                var res=ResolveReference(objVal.Value.Index,asset);
                                tFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                            }
                        }
                        if(pName!=null&&tFile!=null)
                        {
                            textures[pName]=tFile;
                            textureOverrides.Add(new JObject{["ParameterName"]=pName,["ContentFile"]=tFile});
                        }
                    }
                }
                var propOverrides=new JObject();
                if(miProps.FirstOrDefault(p=>p.Name.ToString()=="BasePropertyOverrides") is UAssetAPI.PropertyTypes.Structs.StructPropertyData bpoStruct)
                {
                    foreach(var field in bpoStruct.Value)
                    {
                        var fn=field.Name.ToString();
                        if(field is UAssetAPI.PropertyTypes.Objects.BytePropertyData byteProp)
                            propOverrides[fn]=byteProp.EnumValue?.ToString()??byteProp.Value.ToString();
                        else if(field is UAssetAPI.PropertyTypes.Objects.BoolPropertyData boolProp)
                            propOverrides[fn]=boolProp.Value;
                        else if(field is UAssetAPI.PropertyTypes.Objects.FloatPropertyData floatProp)
                            propOverrides[fn]=floatProp.Value;
                    }
                }
                JObject? parentMatInfo=null;
                if(parentPackage!=null&&resolve!=null)
                {
                    try
                    {
                        var parentPath=resolve(parentPackage);
                        if(File.Exists(parentPath))
                        {
                            var parentAsset=new UAsset(parentPath,EngineVersion.VER_UE4_19);
                            parentMatInfo=ExtractMaterialInfo(parentAsset);
                            if(!textures.ContainsKey("tex"))
                            {
                                foreach(var imp in parentAsset.Imports.Where(i=>i.ClassName.ToString()=="Texture2D"))
                                {
                                    var res=ResolveReference(-(parentAsset.Imports.IndexOf(imp)+1),parentAsset);
                                    var cf=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                                    if(cf!=null){textures["tex"]=cf;break;}
                                }
                            }
                        }
                    }
                    catch{}
                }
                return new JObject
                {
                    ["Type"]="MaterialInstance",
                    ["MaterialInstanceInfo"]=new JObject
                    {
                        ["Name"]=miExport.ObjectName.ToString(),
                        ["Parent"]=parentObjName,
                        ["ParentPackage"]=parentPackage,
                        ["Scalars"]=JObject.FromObject(scalars),
                        ["Textures"]=JObject.FromObject(textures),
                        ["ScalarOverrides"]=scalarOverrides,
                        ["VectorOverrides"]=vectorOverrides,
                        ["TextureOverrides"]=textureOverrides,
                        ["BasePropertyOverrides"]=propOverrides,
                        ["ParentMaterialInfo"]=parentMatInfo
                    }
                };
            }
            var matExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="Material");
            if(matExport is not null)
            {
                var matInfo=ExtractMaterialInfo(asset);
                if(matInfo is not null)
                    return new JObject{["Type"]="Material",["MaterialInfo"]=matInfo};
            }
        }
        catch{}
        return null;
    }
    internal static JObject? ExtractMaterialInfo(UAsset asset)
    {
        var matExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="Material");
        if(matExport is null)return null;
        string? GetEnum(NormalExport e,string propName)=>(e.Data.FirstOrDefault(p=>p.Name.ToString()==propName) as UAssetAPI.PropertyTypes.Objects.BytePropertyData)?.EnumValue?.ToString();
        bool GetBool(NormalExport e,string propName)=>(e.Data.FirstOrDefault(p=>p.Name.ToString()==propName) as UAssetAPI.PropertyTypes.Objects.BoolPropertyData)?.Value??false;
        var domain=GetEnum(matExport,"MaterialDomain")??"MD_Surface";
        var blendMode=GetEnum(matExport,"BlendMode")??"BLEND_Opaque";
        var shadingModel=GetEnum(matExport,"ShadingModel")??"MSM_DefaultLit";
        var twoSided=GetBool(matExport,"TwoSided");
        var maskedOpaque=GetBool(matExport,"bCanMaskedBeAssumedOpaque");
        var stateId="";
        if(matExport.Data.FirstOrDefault(p=>p.Name.ToString()=="StateId") is UAssetAPI.PropertyTypes.Structs.StructPropertyData stateStruct && stateStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.GuidPropertyData guidProp)
            stateId=guidProp.Value.ToString();

        var scalarParams=new JArray();
        var vectorParams=new JArray();
        var textureNodes=new JArray();
        var funcCalls=new JArray();
        var allExpressions=new JArray();

        for(int idx=0;idx<asset.Exports.Count;idx++)
        {
            if(asset.Exports[idx] is not NormalExport exp)continue;
            var cls=exp.GetExportClassType().ToString();
            if(!cls.StartsWith("MaterialExpression",StringComparison.Ordinal))continue;
            var expName=exp.ObjectName.ToString();
            var pName=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="ParameterName") as UAssetAPI.PropertyTypes.Objects.NamePropertyData)?.Value?.ToString();

            if(cls.Contains("ScalarParameter",StringComparison.Ordinal))
            {
                var defVal=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
                var sMin=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="SliderMin") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value;
                var sMax=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="SliderMax") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value;
                scalarParams.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["ParameterName"]=pName??expName,["DefaultValue"]=defVal,["SliderMin"]=sMin,["SliderMax"]=sMax});
            }
            else if(cls.Contains("VectorParameter",StringComparison.Ordinal))
            {
                float r=0,g=0,b=0,a=1;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                {
                    r=linCol.Value.R;g=linCol.Value.G;b=linCol.Value.B;a=linCol.Value.A;
                }
                vectorParams.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["ParameterName"]=pName??expName,["R"]=r,["G"]=g,["B"]=b,["A"]=a});
            }
            else if(cls.Contains("Texture",StringComparison.Ordinal))
            {
                string? texObj=null,texFile=null;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="Texture") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData texProp)
                {
                    var res=ResolveReference(texProp.Value.Index,asset);
                    texObj=res["ObjectName"]?.ToString();
                    texFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                var samplerType=GetEnum(exp,"SamplerType");
                textureNodes.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["Class"]=cls,["ParameterName"]=pName,["TextureName"]=texObj,["ContentFile"]=texFile,["SamplerType"]=samplerType});
            }
            else if(cls.Contains("MaterialFunctionCall",StringComparison.Ordinal))
            {
                string? fnObj=null,fnFile=null;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="MaterialFunction") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData fnProp)
                {
                    var res=ResolveReference(fnProp.Value.Index,asset);
                    fnObj=res["ObjectName"]?.ToString();
                    fnFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                funcCalls.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["FunctionName"]=fnObj,["ContentFile"]=fnFile});
            }

            allExpressions.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["Class"]=cls,["ParameterName"]=pName});
        }

        var importedTextures=new JArray();
        for(int i=0;i<asset.Imports.Count;i++)
        {
            var imp=asset.Imports[i];
            if(imp.ClassName.ToString()=="Texture2D")
            {
                var res=ResolveReference(-(i+1),asset);
                importedTextures.Add(new JObject{["ObjectName"]=imp.ObjectName.ToString(),["ContentFile"]=res["ContentFile"],["Package"]=res["Package"]});
            }
        }

        return new JObject
        {
            ["Name"]=matExport.ObjectName.ToString(),
            ["MaterialDomain"]=domain,
            ["BlendMode"]=blendMode,
            ["ShadingModel"]=shadingModel,
            ["TwoSided"]=twoSided,
            ["CanMaskedBeAssumedOpaque"]=maskedOpaque,
            ["StateId"]=stateId,
            ["ScalarParameters"]=scalarParams,
            ["VectorParameters"]=vectorParams,
            ["TextureNodes"]=textureNodes,
            ["FunctionCalls"]=funcCalls,
            ["Expressions"]=allExpressions,
            ["ImportedTextures"]=importedTextures
        };
    }
    internal static JObject? ExtractMaterialFunctionInfo(UAsset asset)
    {
        var fnExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="MaterialFunction");
        if(fnExport is null)return null;

        var desc=(fnExport.Data.FirstOrDefault(p=>p.Name.ToString()=="Description") as UAssetAPI.PropertyTypes.Objects.StrPropertyData)?.Value?.ToString();
        var stateId="";
        if(fnExport.Data.FirstOrDefault(p=>p.Name.ToString()=="StateId") is UAssetAPI.PropertyTypes.Structs.StructPropertyData stateStruct && stateStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.GuidPropertyData guidProp)
            stateId=guidProp.Value.ToString();

        var scalarParams=new JArray();
        var vectorParams=new JArray();
        var textureNodes=new JArray();
        var funcCalls=new JArray();
        var allExpressions=new JArray();

        string? GetEnum(NormalExport e,string propName)=>(e.Data.FirstOrDefault(p=>p.Name.ToString()==propName) as UAssetAPI.PropertyTypes.Objects.BytePropertyData)?.EnumValue?.ToString();

        for(int idx=0;idx<asset.Exports.Count;idx++)
        {
            if(asset.Exports[idx] is not NormalExport exp)continue;
            var cls=exp.GetExportClassType().ToString();
            if(!cls.StartsWith("MaterialExpression",StringComparison.Ordinal))continue;
            var expName=exp.ObjectName.ToString();
            var pName=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="ParameterName") as UAssetAPI.PropertyTypes.Objects.NamePropertyData)?.Value?.ToString();

            if(cls.Contains("ScalarParameter",StringComparison.Ordinal))
            {
                var defVal=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
                var sMin=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="SliderMin") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value;
                var sMax=(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="SliderMax") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value;
                scalarParams.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["ParameterName"]=pName??expName,["DefaultValue"]=defVal,["SliderMin"]=sMin,["SliderMax"]=sMax});
            }
            else if(cls.Contains("VectorParameter",StringComparison.Ordinal))
            {
                float r=0,g=0,b=0,a=1;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="DefaultValue") is UAssetAPI.PropertyTypes.Structs.StructPropertyData colStruct && colStruct.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.LinearColorPropertyData linCol)
                {
                    r=linCol.Value.R;g=linCol.Value.G;b=linCol.Value.B;a=linCol.Value.A;
                }
                vectorParams.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["ParameterName"]=pName??expName,["R"]=r,["G"]=g,["B"]=b,["A"]=a});
            }
            else if(cls.Contains("Texture",StringComparison.Ordinal))
            {
                string? texObj=null,texFile=null;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="Texture") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData texProp)
                {
                    var res=ResolveReference(texProp.Value.Index,asset);
                    texObj=res["ObjectName"]?.ToString();
                    texFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                var samplerType=GetEnum(exp,"SamplerType");
                textureNodes.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["Class"]=cls,["ParameterName"]=pName,["TextureName"]=texObj,["ContentFile"]=texFile,["SamplerType"]=samplerType});
            }
            else if(cls.Contains("MaterialFunctionCall",StringComparison.Ordinal))
            {
                string? fnObj=null,fnFile=null;
                if(exp.Data.FirstOrDefault(p=>p.Name.ToString()=="MaterialFunction") is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData fnProp)
                {
                    var res=ResolveReference(fnProp.Value.Index,asset);
                    fnObj=res["ObjectName"]?.ToString();
                    fnFile=res["ContentFile"]?.ToString()??res["Package"]?.ToString();
                }
                funcCalls.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["FunctionName"]=fnObj,["ContentFile"]=fnFile});
            }

            allExpressions.Add(new JObject{["ExportIndex"]=idx,["NodeName"]=expName,["Class"]=cls,["ParameterName"]=pName});
        }

        return new JObject
        {
            ["Name"]=fnExport.ObjectName.ToString(),
            ["Description"]=desc,
            ["StateId"]=stateId,
            ["ScalarParameters"]=scalarParams,
            ["VectorParameters"]=vectorParams,
            ["TextureNodes"]=textureNodes,
            ["FunctionCalls"]=funcCalls,
            ["Expressions"]=allExpressions
        };
    }
    private static JObject ParseDistribution(UAssetAPI.PropertyTypes.Structs.StructPropertyData? distStruct,UAsset asset)
    {
        var res=new JObject();
        if(distStruct is null)return res;
        res["StructType"]=distStruct.StructType?.ToString();
        float? minVal=null,maxVal=null;
        float[]? minVec=null,maxVec=null;
        JArray? tableVals=null;
        byte? tableOp=null,entryCount=null,entryStride=null;

        foreach(var prop in distStruct.Value)
        {
            var pn=prop.Name.ToString();
            if(pn=="MinValue" && prop is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fMin)minVal=fMin.Value;
            else if(pn=="MaxValue" && prop is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fMax)maxVal=fMax.Value;
            else if(pn=="MinValueVec" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData sMinVec && sMinVec.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.VectorPropertyData vMin)
                minVec=[(float)vMin.Value.X,(float)vMin.Value.Y,(float)vMin.Value.Z];
            else if(pn=="MaxValueVec" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData sMaxVec && sMaxVec.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.VectorPropertyData vMax)
                maxVec=[(float)vMax.Value.X,(float)vMax.Value.Y,(float)vMax.Value.Z];
            else if(pn=="Distribution" && prop is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData distRef && distRef.Value.Index!=0)
            {
                var r=ResolveReference(distRef.Value.Index,asset);
                res["DistributionObject"]=r["ObjectName"]?.ToString();
            }
            else if(pn=="Table" && prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData tableStruct)
            {
                // Tagged serialization omits zero-valued lookup time fields.
                res["TimeScale"]=0f;
                res["TimeBias"]=0f;
                foreach(var tf in tableStruct.Value)
                {
                    var tfn=tf.Name.ToString();
                    if(tfn=="Op" && tf is UAssetAPI.PropertyTypes.Objects.BytePropertyData bOp)tableOp=bOp.Value;
                    else if(tfn=="EntryCount" && tf is UAssetAPI.PropertyTypes.Objects.BytePropertyData bCnt)entryCount=bCnt.Value;
                    else if(tfn=="EntryStride" && tf is UAssetAPI.PropertyTypes.Objects.BytePropertyData bStr)entryStride=bStr.Value;
                    else if((tfn=="TimeScale" || tfn=="TimeBias") && tf is UAssetAPI.PropertyTypes.Objects.FloatPropertyData timeField)
                        res[tfn]=timeField.Value;
                    else if(tfn=="Values" && tf is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData valsArr)
                        tableVals=new JArray(valsArr.Value.OfType<UAssetAPI.PropertyTypes.Objects.FloatPropertyData>().Select(fv=>fv.Value));
                }
            }
        }

        if(minVec!=null||maxVec!=null)
        {
            var mn=minVec??maxVec??[0f,0f,0f];
            var mx=maxVec??minVec??[0f,0f,0f];
            bool same=mn[0]==mx[0]&&mn[1]==mx[1]&&mn[2]==mx[2];
            res["Kind"]=same?"ConstantVector":"UniformVector";
            res["MinVector"]=new JArray(mn);
            res["MaxVector"]=new JArray(mx);
            res["Summary"]=same?$"({mn[0]:0.##}, {mn[1]:0.##}, {mn[2]:0.##})":$"({mn[0]:0.##}, {mn[1]:0.##}, {mn[2]:0.##}) ~ ({mx[0]:0.##}, {mx[1]:0.##}, {mx[2]:0.##})";
        }
        else if(minVal.HasValue||maxVal.HasValue)
        {
            var mn=minVal??maxVal??0f;
            var mx=maxVal??minVal??0f;
            bool same=mn==mx;
            res["Kind"]=same?"ConstantFloat":"UniformFloat";
            res["MinValue"]=mn;
            res["MaxValue"]=mx;
            res["Summary"]=same?$"{mn:0.###}":$"{mn:0.###} ~ {mx:0.###}";
        }
        else if(tableVals is {Count:>0})
        {
            if(tableVals.Count==3 && entryStride==3)
            {
                var v0=tableVals[0]!.Value<float>();var v1=tableVals[1]!.Value<float>();var v2=tableVals[2]!.Value<float>();
                res["Kind"]="ConstantVector";
                res["MinVector"]=new JArray(v0,v1,v2);
                res["MaxVector"]=new JArray(v0,v1,v2);
                res["Summary"]=$"({v0:0.##}, {v1:0.##}, {v2:0.##})";
            }
            else if(tableVals.Count==1)
            {
                var v0=tableVals[0]!.Value<float>();
                res["Kind"]="ConstantFloat";
                res["MinValue"]=v0;
                res["MaxValue"]=v0;
                res["Summary"]=$"{v0:0.###}";
            }
            else
            {
                res["Kind"]="CurveTable";
                res["Summary"]=$"Table[{tableVals.Count}]";
            }
        }
        else
        {
            res["Kind"]="Default";
            res["Summary"]="0";
        }
        if(tableVals is {Count:>0})res["TableValues"]=tableVals;
        if(tableOp.HasValue)res["TableOp"]=tableOp.Value;
        if(entryCount.HasValue)res["EntryCount"]=entryCount.Value;
        if(entryStride.HasValue)res["EntryStride"]=entryStride.Value;
        return res;
    }

    private static JObject ParseParticleModule(int exportIndex1Based,UAsset asset)
    {
        if(exportIndex1Based<=0||exportIndex1Based>asset.Exports.Count||asset.Exports[exportIndex1Based-1] is not NormalExport mod)
            return new JObject();
        var cls=mod.GetExportClassType().ToString();
        var name=mod.ObjectName.ToString();
        string category=cls switch
        {
            _ when cls.Contains("Required",StringComparison.Ordinal)=>"Required",
            _ when cls.Contains("Spawn",StringComparison.Ordinal)=>"Spawn",
            _ when cls.Contains("TypeData",StringComparison.Ordinal)=>"TypeData",
            _ when cls.Contains("Lifetime",StringComparison.Ordinal)=>"Lifetime",
            _ when cls.Contains("Size",StringComparison.Ordinal)=>"Size",
            _ when cls.Contains("Velocity",StringComparison.Ordinal)=>"Velocity",
            _ when cls.Contains("Color",StringComparison.Ordinal)=>"Color",
            _ when cls.Contains("Location",StringComparison.Ordinal)=>"Location",
            _ when cls.Contains("Rotation",StringComparison.Ordinal)=>"Rotation",
            _ when cls.Contains("Acceleration",StringComparison.Ordinal)=>"Acceleration",
            _ when cls.Contains("Parameter",StringComparison.Ordinal)=>"Parameter",
            _ when cls.Contains("SubUV",StringComparison.Ordinal)=>"SubUV",
            _ when cls.Contains("Orbit",StringComparison.Ordinal)=>"Orbit",
            _=>"Module"
        };

        var parsedProps=new JArray();
        var distributions=new JObject();
        var references=new JObject();
        var rawProps=new JObject();
        string summary="";

        foreach(var prop in mod.Data)
        {
            var pn=prop.Name.ToString();
            try
            {
                var rawToken=JToken.Parse(asset.SerializeJsonObject(prop));
                AnnotateReferences(rawToken,asset);
                rawProps[pn]=rawToken;
            }
            catch{}

            if(pn=="LODValidity")continue;

            if(prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData sp)
            {
                var st=sp.StructType?.ToString()??"";
                if(st is "RawDistributionFloat" or "RawDistributionVector")
                {
                    var d=ParseDistribution(sp,asset);
                    distributions[pn]=d;
                    parsedProps.Add(new JObject{["Name"]=pn,["Kind"]=d["Kind"],["Value"]=d["Summary"],["Distribution"]=d});
                    if(string.IsNullOrEmpty(summary))summary=$"{pn}: {d["Summary"]}";
                }
                else if(st=="Vector" && sp.Value.FirstOrDefault() is UAssetAPI.PropertyTypes.Structs.VectorPropertyData vp)
                {
                    var vecStr=$"({vp.Value.X:0.##}, {vp.Value.Y:0.##}, {vp.Value.Z:0.##})";
                    parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Vector",["Value"]=vecStr,["Vector"]=new JArray(vp.Value.X,vp.Value.Y,vp.Value.Z)});
                }
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.ObjectPropertyData op)
            {
                var r=ResolveReference(op.Value.Index,asset);
                var objName=r["ObjectName"]?.ToString()??"None";
                var file=r["ContentFile"]?.ToString()??r["Package"]?.ToString();
                var refObj=new JObject{["Index"]=op.Value.Index,["ObjectName"]=objName,["ContentFile"]=file,["Class"]=r["Class"]};
                references[pn]=refObj;
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Object",["Value"]=objName,["ContentFile"]=file,["Reference"]=refObj});
                if(pn is "Material" or "Mesh")summary=objName;
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.FloatPropertyData fp)
            {
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Float",["Value"]=$"{fp.Value:0.###}",["Number"]=fp.Value});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.IntPropertyData ip)
            {
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Int",["Value"]=ip.Value.ToString(),["Number"]=ip.Value});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.BoolPropertyData bp)
            {
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Bool",["Value"]=bp.Value?"true":"false",["Bool"]=bp.Value});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.BytePropertyData byp)
            {
                var ev=byp.EnumValue?.ToString()??byp.Value.ToString();
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Enum",["Value"]=ev});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.EnumPropertyData ep)
            {
                var ev=ep.Value?.ToString()??"None";
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Enum",["Value"]=ev});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.NamePropertyData np)
            {
                parsedProps.Add(new JObject{["Name"]=pn,["Kind"]="Name",["Value"]=np.Value?.ToString()??""});
            }
            else if(prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData ap && pn=="BurstList")
            {
                var bursts=new JArray();
                foreach(var item in ap.Value.OfType<UAssetAPI.PropertyTypes.Structs.StructPropertyData>())
                {
                    int count=(item.Value.FirstOrDefault(f=>f.Name.ToString()=="Count") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??0;
                    int countLow=(item.Value.FirstOrDefault(f=>f.Name.ToString()=="CountLow") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??-1;
                    float time=(item.Value.FirstOrDefault(f=>f.Name.ToString()=="Time") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
                    bursts.Add(new JObject{["Count"]=count,["CountLow"]=countLow,["Time"]=time});
                }
                parsedProps.Add(new JObject{["Name"]="BurstList",["Kind"]="BurstList",["Value"]=$"{bursts.Count} Burst(s)",["Bursts"]=bursts});
                if(bursts.Count>0)summary=$"Rate: {distributions["Rate"]?["Summary"]??"0"} | Bursts: {bursts.Count}";
            }
        }

        return new JObject
        {
            ["ExportIndex"]=exportIndex1Based-1,
            ["Name"]=name,
            ["Class"]=cls,
            ["Category"]=category,
            ["Summary"]=summary,
            ["Properties"]=parsedProps,
            ["Distributions"]=distributions,
            ["References"]=references,
            ["RawProperties"]=rawProps,
            ["CutoutConfiguration"]=cls == "ParticleModuleRequired"
                ? JObject.FromObject(LegacyParticleCutoutConfiguration.Inspect(mod)) : null
        };
    }

    public static JObject? ReadParticleSystemData(string path,Func<string,string>? resolve=null,UAsset? loadedAsset=null)
    {
        try
        {
            var asset=loadedAsset??(File.Exists(path)?new UAsset(path,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19):null);
            if(asset==null)return null;
            var info=ExtractParticleSystemInfo(asset);
            if(info!=null&&resolve!=null)
            {
                var materials=new JObject();
                // 收集粒子发射器中的材质引用
                var emitters=info["Emitters"] as JArray;
                if(emitters!=null)
                {
                    foreach(var em in emitters)
                    {
                        var lods=em["LODLevels"] as JArray;
                        if(lods==null)continue;
                        foreach(var lod in lods)
                        {
                            var req=lod["RequiredModule"] as JObject;
                            var matRef=req?["References"]?["Material"] as JObject;
                            var cFile=matRef?["ContentFile"]?.ToString();
                            if(!string.IsNullOrEmpty(cFile)&&materials[cFile]==null)
                            {
                                try
                                {
                                    var resolved=resolve(cFile);
                                    var mData=ReadMaterialData(resolved,resolve);
                                    if(mData!=null)materials[cFile]=mData;
                                }
                                catch {}
                            }
                        }
                    }
                }
                if(materials.Count>0)info["Materials"]=materials;
            }
            return info;
        }
        catch { return null; }
    }

    internal static JObject? ExtractParticleSystemInfo(UAsset asset)
    {
        var psExport=asset.Exports.OfType<NormalExport>().FirstOrDefault(e=>e.GetExportClassType().ToString()=="ParticleSystem");
        if(psExport is null)return null;

        var warmupTime=(psExport.Data.FirstOrDefault(p=>p.Name.ToString()=="WarmupTime") as UAssetAPI.PropertyTypes.Objects.FloatPropertyData)?.Value??0f;
        var resetPeak=(psExport.Data.FirstOrDefault(p=>p.Name.ToString()=="bShouldResetPeakCounts") as UAssetAPI.PropertyTypes.Objects.BoolPropertyData)?.Value??false;
        var lodDistances=new JArray();
        if(psExport.Data.FirstOrDefault(p=>p.Name.ToString()=="LODDistances") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData lodDistArr)
        {
            foreach(var d in lodDistArr.Value.OfType<UAssetAPI.PropertyTypes.Objects.FloatPropertyData>())
                lodDistances.Add(d.Value);
        }

        var emitters=new JArray();
        if(psExport.Data.FirstOrDefault(p=>p.Name.ToString()=="Emitters") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData emittersArr)
        {
            foreach(var emObjProp in emittersArr.Value.OfType<UAssetAPI.PropertyTypes.Objects.ObjectPropertyData>())
            {
                int emIdx=emObjProp.Value.Index;
                if(emIdx<=0||emIdx>asset.Exports.Count||asset.Exports[emIdx-1] is not NormalExport emExport)continue;

                var emitterName=(emExport.Data.FirstOrDefault(p=>p.Name.ToString()=="EmitterName") as UAssetAPI.PropertyTypes.Objects.NamePropertyData)?.Value?.ToString()
                    ??(emExport.Data.FirstOrDefault(p=>p.Name.ToString()=="EmitterName") as UAssetAPI.PropertyTypes.Objects.StrPropertyData)?.Value?.ToString()
                    ??emExport.ObjectName.ToString();
                var initAlloc=(emExport.Data.FirstOrDefault(p=>p.Name.ToString()=="InitialAllocationCount") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??0;

                var lodLevels=new JArray();
                string emitterType="Sprite";
                int peakParticles=0;

                if(emExport.Data.FirstOrDefault(p=>p.Name.ToString()=="LODLevels") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData lodArr)
                {
                    foreach(var lodObjProp in lodArr.Value.OfType<UAssetAPI.PropertyTypes.Objects.ObjectPropertyData>())
                    {
                        int lodIdx=lodObjProp.Value.Index;
                        if(lodIdx<=0||lodIdx>asset.Exports.Count||asset.Exports[lodIdx-1] is not NormalExport lodExport)continue;

                        int level=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="Level") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??0;
                        int peak=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="PeakActiveParticles") as UAssetAPI.PropertyTypes.Objects.IntPropertyData)?.Value??0;
                        bool enabled=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="bEnabled") as UAssetAPI.PropertyTypes.Objects.BoolPropertyData)?.Value??true;
                        if(peak>peakParticles)peakParticles=peak;

                        int reqIdx=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="RequiredModule") as UAssetAPI.PropertyTypes.Objects.ObjectPropertyData)?.Value.Index??0;
                        int spawnIdx=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="SpawnModule") as UAssetAPI.PropertyTypes.Objects.ObjectPropertyData)?.Value.Index??0;
                        int tdIdx=(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="TypeDataModule") as UAssetAPI.PropertyTypes.Objects.ObjectPropertyData)?.Value.Index??0;

                        var reqMod=reqIdx>0?ParseParticleModule(reqIdx,asset):null;
                        var spawnMod=spawnIdx>0?ParseParticleModule(spawnIdx,asset):null;
                        var tdMod=tdIdx>0?ParseParticleModule(tdIdx,asset):null;

                        if(tdMod is not null)
                        {
                            var tdCls=tdMod["Class"]?.ToString()??"";
                            if(tdCls.Contains("Mesh",StringComparison.Ordinal))emitterType="Mesh";
                            else if(tdCls.Contains("Gpu",StringComparison.Ordinal))emitterType="GPU";
                            else if(tdCls.Contains("Ribbon",StringComparison.Ordinal))emitterType="Ribbon";
                            else if(tdCls.Contains("Beam",StringComparison.Ordinal))emitterType="Beam";
                        }

                        var modList=new JArray();
                        if(lodExport.Data.FirstOrDefault(p=>p.Name.ToString()=="Modules") is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData modsArr)
                        {
                            foreach(var mProp in modsArr.Value.OfType<UAssetAPI.PropertyTypes.Objects.ObjectPropertyData>())
                            {
                                if(mProp.Value.Index>0)
                                    modList.Add(ParseParticleModule(mProp.Value.Index,asset));
                            }
                        }

                        lodLevels.Add(new JObject
                        {
                            ["ExportIndex"]=lodIdx-1,
                            ["Name"]=lodExport.ObjectName.ToString(),
                            ["Level"]=level,
                            ["Enabled"]=enabled,
                            ["PeakActiveParticles"]=peak,
                            ["RequiredModule"]=reqMod,
                            ["SpawnModule"]=spawnMod,
                            ["TypeDataModule"]=tdMod,
                            ["Modules"]=modList
                        });
                    }
                }

                emitters.Add(new JObject
                {
                    ["ExportIndex"]=emIdx-1,
                    ["Name"]=emExport.ObjectName.ToString(),
                    ["EmitterName"]=emitterName,
                    ["Class"]=emExport.GetExportClassType().ToString(),
                    ["EmitterType"]=emitterType,
                    ["InitialAllocationCount"]=initAlloc,
                    ["PeakActiveParticles"]=peakParticles,
                    ["LODLevels"]=lodLevels
                });
            }
        }

        return new JObject
        {
            ["ExportIndex"]=asset.Exports.IndexOf(psExport),
            ["Name"]=psExport.ObjectName.ToString(),
            ["WarmupTime"]=warmupTime,
            ["ResetPeakCounts"]=resetPeak,
            ["LODDistances"]=lodDistances,
            ["Emitters"]=emitters
        };
    }
}
