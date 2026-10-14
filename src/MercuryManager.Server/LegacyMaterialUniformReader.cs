using System.Numerics;
using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace MercuryManager.Server;

public sealed record LegacyShaderEntry(string Type,int StartOffset,int EndOffset);
public sealed record LegacyUniformSet(LegacyUniformNode[][] Arrays,Guid[] Collections,bool[] Flags,int EndOffset);
public sealed record LegacyUniformNode(string Type,string? ParameterName,byte? Association,int? ParameterIndex,float[] Values,int[] Metadata,LegacyUniformNode[] Children);

public sealed record LegacyCookedMaterial(
    LegacyUniformSet Uniforms,
    IReadOnlyList<LegacyShaderEntry> Shaders,
    IReadOnlyList<string> ReferencedTextures,
    int ShadingModelPlatform,
    string FriendlyName,
    string MaterialDescription,
    IReadOnlyList<LegacyVertexFactoryShaderMap>? VertexFactoryMaps = null);

/// <summary>UE4.19 typed uniform stream reader. Caller must establish array boundaries from the shader map.</summary>
public sealed class LegacyMaterialUniformReader(UAsset asset,byte[] data,int offset,int renderingVersion)
{
    public int Position {get;private set;}=offset;
    private int nodes;

    public static LegacyCookedMaterial? ReadCookedMaterial(UAsset asset, NormalExport export)
    {
        var data = export.Extras ?? [];
        if (data.Length == 0) return null;
        var reader = new LegacyMaterialUniformReader(asset, data, 0, 11);
        int Count()
        {
            int count = reader.Int();
            if (count < 0 || count > 4096) throw new InvalidDataException("Material header count out of range.");
            return count;
        }
        void Skip(int bytes) { reader.Require(bytes); reader.Position += bytes; }
        void Array(int stride) { Skip(checked(Count() * stride)); }
        bool Flag()
        {
            int value = reader.Int();
            if (value is not (0 or 1)) throw new InvalidDataException("Invalid material resource flag.");
            return value != 0;
        }
        int resources = Count();
        if (resources == 0) return null;
        // Preserve existing first-resource selection. Additional quality/platform resources
        // are not traversed; callers must not claim permutation selection support.
        if (!Flag()) throw new NotSupportedException("Uncooked material resource is not supported.");
        if (!Flag()) return null;
        Skip(28); // QualityLevel, BaseMaterialId, FeatureLevel, extra flag.
        Array(37); // Static switches.
        Array(49); // Static component masks.
        Array(41); // Terrain weights.
        if (Count() != 0) throw new NotSupportedException("Static material layers require a versioned serializer.");
        Array(16); // Referenced functions.
        Array(16); // Referenced collections.
        Array(32); // Shader type dependencies.
        Array(28); // Pipeline dependencies.
        Array(28); // Vertex factory dependencies.
        Skip(40); // Texture references and base property hashes.
        int platform = reader.Int();
        int length = reader.Int();
        if (Math.Abs((long)length) > 1024 * 1024) throw new InvalidDataException("Material friendly name exceeds limits.");
        int stringBytes = checked(length < 0 ? -length * 2 : length);
        reader.Require(stringBytes);
        if (stringBytes > 0 && (data[reader.Position + stringBytes - 1] != 0 ||
            (length < 0 && data[reader.Position + stringBytes - 2] != 0)))
            throw new InvalidDataException("Unterminated material friendly name.");
        string friendlyName = length < 0
            ? System.Text.Encoding.Unicode.GetString(data, reader.Position, Math.Max(0, stringBytes - 2))
            : System.Text.Encoding.UTF8.GetString(data, reader.Position, Math.Max(0, stringBytes - 1));
        Skip(stringBytes);
        var uniforms = reader.ReadSet();
        long fileOffset = checked(export.SerialOffset + export.SerialSize - data.Length);
        var shaders = reader.ReadShaderDirectory(fileOffset);
        IReadOnlyList<LegacyVertexFactoryShaderMap> vfMaps = [];
        try
        {
            var vfReader = new LegacyVertexFactoryShaderMapReader(asset, data, reader.Position);
            vfMaps = vfReader.Read(fileOffset);
        }
        catch
        {
            // If the material has uncooked or unsupported VF maps, retain empty maps rather than failing basic material load.
        }
        var textures = new List<string>();
        foreach (var arr in export.Data.OfType<UAssetAPI.PropertyTypes.Objects.ArrayPropertyData>()
                     .Where(p => p.Name.ToString() == "ReferencedTextures"))
        {
            foreach (var item in arr.Value)
            {
                if (item is not UAssetAPI.PropertyTypes.Objects.ObjectPropertyData reference)
                    throw new InvalidDataException("Invalid referenced texture entry.");
                var resolved = GenericResourceViewer.ResolveReference(reference.Value.Index, asset);
                // Preserve array indices, including unresolved entries; never compact texture slots.
                textures.Add(resolved["ContentFile"]?.ToString() ?? string.Empty);
            }
        }
        return new(uniforms, shaders, textures, platform, friendlyName, string.Empty, vfMaps);
    }
    public LegacyUniformNode[] ReadArray()
    {
        int count=Int();if(count<0||count>4096)throw new InvalidDataException("Uniform array count out of range.");
        var result=new LegacyUniformNode[count];for(int i=0;i<count;i++)result[i]=Read(0);return result;
    }
    public LegacyUniformSet ReadSet()
    {
        var arrays=new LegacyUniformNode[9][];
        for(int i=0;i<5;i++)arrays[i]=ReadArray();
        int count=Int();
        if(count<0||count>4096)throw new InvalidDataException("Uniform collection count out of range.");
        var collections=new Guid[count];
        for(int i=0;i<count;i++)
        {
            Require(16);collections[i]=new Guid(data.AsSpan(Position,16));Position+=16;
        }
        for(int i=5;i<9;i++)arrays[i]=ReadArray();
        var flags=new bool[9];
        for(int i=0;i<flags.Length;i++)
        {
            int value=Int();
            if(value is not (0 or 1))throw new InvalidDataException("Invalid serialized uniform flag.");
            flags[i]=value!=0;
        }
        return new(arrays,collections,flags,Position);
    }
    /// <summary>Read the non-pipeline shader directory after the uniform set and material description.</summary>
    public LegacyShaderEntry[] ReadShaderDirectory(long extrasFileOffset)
    {
        int length=Int();
        if(length==int.MinValue||Math.Abs((long)length)>1024*1024)throw new InvalidDataException("Material description exceeds limits.");
        int bytes=checked(length<0?-length*2:length);Require(bytes);
        if(bytes>0&&(data[Position+bytes-1]!=0||(length<0&&data[Position+bytes-2]!=0)))
            throw new InvalidDataException("Unterminated material description.");
        Position+=bytes;
        int count=Int();if(count<0||count>4096)throw new InvalidDataException("Shader directory count out of range.");
        var entries=new LegacyShaderEntry[count];
        for(int i=0;i<count;i++)
        {
            string type=Name();long end=(long)Int()-extrasFileOffset;
            if(end<Position||end>data.Length)throw new InvalidDataException("Shader entry boundary out of range.");
            entries[i]=new(type,Position,(int)end);Position=(int)end;
        }
        int pipelines=Int();
        if(pipelines!=0)throw new NotSupportedException("Legacy shader pipeline entries are not supported yet.");
        return entries;
    }
    private LegacyUniformNode Read(int depth)
    {
        if(depth>64||++nodes>16384)throw new InvalidDataException("Uniform expression limits exceeded.");
        var type=Name();string? parameter=null;byte? association=null;int? index=null;
        float[] values=[];int[] metadata=[];LegacyUniformNode[] children=[];
        switch(type)
        {
            case "FMaterialUniformExpressionScalarParameter":
            case "FMaterialUniformExpressionVectorParameter":
            case "FMaterialUniformExpressionTextureParameter":
                parameter=Name();association=Byte();index=Int();
                if(type.EndsWith("ScalarParameter"))values=[Float()];
                else if(type.EndsWith("VectorParameter"))values=Color();
                else metadata=[Int(),Int()];
                break;
            case "FMaterialUniformExpressionFmod":children=[Read(depth+1),Read(depth+1)];break;
            case "FMaterialUniformExpressionTexture":metadata=[Int(),Int()];break;
            case "FMaterialUniformExpressionTime":break;
            case "FMaterialUniformExpressionRealTime":break;
            case "FMaterialUniformExpressionMax":children=[Read(depth+1),Read(depth+1)];break;
            case "FMaterialUniformExpressionAbs":
            case "FMaterialUniformExpressionCeil":children=[Read(depth+1)];break;
            case "FMaterialUniformExpressionClamp":children=[Read(depth+1),Read(depth+1),Read(depth+1)];break;
            case "FMaterialUniformExpressionConstant":values=Color();metadata=[Byte()];break;
            case "FMaterialUniformExpressionFoldedMath":
                children=[Read(depth+1),Read(depth+1)];int operation=Byte();metadata=[operation,renderingVersion>=11?Int():15];break;
            case "FMaterialUniformExpressionAppendVector":children=[Read(depth+1),Read(depth+1)];metadata=[Int()];break;
            case "FMaterialUniformExpressionTrigMath":children=[Read(depth+1),Read(depth+1)];metadata=[Byte()];break;
            case "FMaterialUniformExpressionComponentSwizzle":children=[Read(depth+1)];metadata=[Byte(),Byte(),Byte(),Byte(),Byte()];break;
            default:throw new NotSupportedException($"Unsupported legacy uniform type {type} at {Position}.");
        }
        return new(type,parameter,association,index,values,metadata,children);
    }
    private string Name()
    {
        int index=Int(),number=Int();if(index<0||index>=asset.GetNameMapIndexList().Count||number<0)throw new InvalidDataException("Invalid uniform FName.");
        var name=asset.GetNameReference(index).ToString();return number==0?name:$"{name}_{number-1}";
    }
    private byte Byte(){Require(1);return data[Position++];}
    private int Int(){Require(4);int value=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Position));Position+=4;return value;}
    private float Float(){float value=BitConverter.Int32BitsToSingle(Int());if(!float.IsFinite(value))throw new InvalidDataException("Non-finite uniform constant.");return value;}
    private float[] Color()=>[Float(),Float(),Float(),Float()];
    private void Require(int count){if(Position<0||Position>data.Length-count)throw new InvalidDataException("Truncated uniform expression.");}
}
