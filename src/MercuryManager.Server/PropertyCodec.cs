using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;

public static class PropertyCodec
{
    public static object? Value(PropertyData p) => p switch
    {
        StrPropertyData s => s.Value?.ToString(),
        BoolPropertyData b => b.Value,
        BytePropertyData b when b.ByteType == BytePropertyType.Byte => b.Value,
        IntPropertyData n => n.Value,
        Int8PropertyData n => n.Value,
        Int16PropertyData n => n.Value,
        EnumPropertyData n => n.Value.ToString(),
        UInt32PropertyData n => n.Value,
        Int64PropertyData n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UInt64PropertyData n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FloatPropertyData n => n.Value,
        ArrayPropertyData a => a.Value.Select(Value).ToArray(),
        StructPropertyData st => st.Value.ToDictionary(p=>p.Name.ToString(),Value),
        MapPropertyData m => m.Value.Select(pair=>new object?[]{Value(pair.Key),Value(pair.Value)}).ToArray(),
        _ => throw new InvalidDataException("Unsupported Message property " + p.Name + " " + p.GetType().Name)
    };
    public static void Set(PropertyData p,JsonElement value, UAsset? asset=null)
    {
        switch(p)
        {
            case StrPropertyData s: s.Value=value.ValueKind==JsonValueKind.Null ? null : new FString(value.GetString()!);break;
            case BoolPropertyData b: b.Value=value.GetBoolean();break;
            case BytePropertyData b when b.ByteType==BytePropertyType.Byte:b.Value=value.GetByte();break;
            case IntPropertyData n:n.Value=value.GetInt32();break;
            case Int8PropertyData n:n.Value=value.GetSByte();break;
            case Int16PropertyData n:n.Value=value.GetInt16();break;
            case EnumPropertyData n:
                var text=value.GetString()??throw new ArgumentException("Enum name required.");
                if(!text.StartsWith(n.EnumType+"::",StringComparison.Ordinal))throw new ArgumentException("Wrong enum type.");
                n.Value=FName.FromString(n.Name.Asset,text);break;
            case UInt32PropertyData n:n.Value=value.GetUInt32();break;
            case Int64PropertyData n:n.Value=value.ValueKind==JsonValueKind.String?long.Parse(value.GetString()!,System.Globalization.CultureInfo.InvariantCulture):value.GetInt64();break;
            case UInt64PropertyData n:n.Value=value.ValueKind==JsonValueKind.String?ulong.Parse(value.GetString()!,System.Globalization.CultureInfo.InvariantCulture):value.GetUInt64();break;
            case FloatPropertyData n:n.Value=value.GetSingle();if(!float.IsFinite(n.Value))throw new ArgumentException("Finite number required.");break;
            case StructPropertyData st:
                if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Struct object required.");
                foreach(var field in value.EnumerateObject()) Set(st.Value.SingleOrDefault(p=>p.Name.ToString()==field.Name)??throw new ArgumentException("Unknown struct field."),field.Value,asset);
                break;
            case MapPropertyData m:
                if(value.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Map entries array required.");
                var entries=new TMap<PropertyData,PropertyData>();
                foreach(var item in value.EnumerateArray())
                {
                    if(item.ValueKind!=JsonValueKind.Array||item.GetArrayLength()!=2)throw new ArgumentException("Map entry requires key and value.");
                    var mapDonor=m.Value.Count>0?m:asset?.Exports?.OfType<DataTableExport>().Single().Table.Data.SelectMany(r=>r.Value).OfType<MapPropertyData>().FirstOrDefault(other=>other.Name.ToString()==m.Name.ToString()&&other.Value.Count>0);
                    var key=mapDonor is not null?(PropertyData)mapDonor.Value.First().Key.Clone():CreateElement(m.KeyType,asset,p.Name);
                    var entry=mapDonor is not null?(PropertyData)mapDonor.Value.First().Value.Clone():CreateElement(m.ValueType,asset,p.Name);
                    Set(key,item[0],asset);Set(entry,item[1],asset);
                    if(entries.Any(e=>JsonSerializer.Serialize(Value(e.Key))==JsonSerializer.Serialize(Value(key))))throw new ArgumentException("Duplicate map key.");
                    entries.Add(key,entry);
                }
                m.Value=entries;break;
            case ArrayPropertyData a:
                if(value.ValueKind!=JsonValueKind.Array) throw new ArgumentException("Array JSON required.");
                var values=new List<PropertyData>();
                foreach(var item in value.EnumerateArray())
                {
                    PropertyData element=a.Value.Length>0 ? (PropertyData)a.Value[0].Clone() : CreateArrayElement(a,asset);
                    Set(element,item,asset);values.Add(element);
                }
                a.Value=values.ToArray();break;
            default:throw new ArgumentException("Unsupported Message edit.");
        }
    }
    private static PropertyData CreateElement(FName type,UAsset? asset,FName name) => type.ToString() switch
    {
        "IntProperty"=>new IntPropertyData(name), "StrProperty"=>new StrPropertyData(name), "BoolProperty"=>new BoolPropertyData(name),
        _=>throw new ArgumentException("Unsupported empty container element: "+type)
    };
    private static PropertyData CreateArrayElement(ArrayPropertyData array,UAsset? asset)
    {
        if(array.ArrayType.ToString()!="StructProperty")return CreateElement(array.ArrayType,asset,array.Name);
        var donor=asset?.Exports?.OfType<DataTableExport>().Single().Table.Data.SelectMany(r=>r.Value).OfType<ArrayPropertyData>()
            .Where(a=>a.Name.ToString()==array.Name.ToString()).SelectMany(a=>a.Value).OfType<StructPropertyData>().FirstOrDefault();
        if(donor is null)throw new ArgumentException("No structure schema available for "+array.Name);
        return (PropertyData)donor.Clone();
    }
}
