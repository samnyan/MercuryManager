using System.Text.Json;
using MercuryManager.Server;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;
namespace MercuryManager.Assets.Tests;

public class PropertyCodecTests
{
    private static UAsset Asset()
    {
        var asset=new UAsset(EngineVersion.VER_UE4_19);
        asset.ClearNameIndexList();
        return asset;
    }
    [Fact] public void Constructed_properties_export_import_and_edit_without_game_assets()
    {
        var asset=Asset();
        PropertyData[] properties=[
            new StrPropertyData(new FName(asset,"Text")){Value=new FString("测试 text")},
            new FloatPropertyData(new FName(asset,"Level")){Value=3.5f},
            new IntPropertyData(new FName(asset,"Count")){Value=-42},
            new BoolPropertyData(new FName(asset,"Enabled")){Value=true},
            new UInt64PropertyData(new FName(asset,"Buffer")){Value=ulong.MaxValue},
            new Int64PropertyData(new FName(asset,"Time")){Value=long.MinValue},
            new Int8PropertyData(new FName(asset,"Season")){Value=-128},
            new Int16PropertyData(new FName(asset,"Stage")){Value=32767},
            new BytePropertyData(new FName(asset,"Byte")){ByteType=BytePropertyType.Byte,Value=255},
            new UInt32PropertyData(new FName(asset,"Id")){Value=uint.MaxValue}
        ];
        foreach(var property in properties)
        {
            var json=asset.SerializeJsonObject(property);
            var imported=asset.DeserializeJsonObject<PropertyData>(json);
            Assert.Equal(property.GetType(),imported.GetType());
            var value=JsonSerializer.SerializeToElement(PropertyCodec.Value(property));
            PropertyCodec.Set(imported,value,asset);
            Assert.Equal(value.GetRawText(),JsonSerializer.Serialize(PropertyCodec.Value(imported)));
        }
    }
    [Fact] public void Float_zero_and_fraction_remain_float_after_json_roundtrip()
    {
        var asset=Asset();var property=new FloatPropertyData(new FName(asset,"Level")){Value=3};
        foreach(var value in new[]{0f,9.7f,-0f})
        {
            PropertyCodec.Set(property,JsonSerializer.SerializeToElement(value),asset);
            var imported=asset.DeserializeJsonObject<FloatPropertyData>(asset.SerializeJsonObject(property));
            Assert.Equal(BitConverter.SingleToInt32Bits(value),BitConverter.SingleToInt32Bits(imported.Value));
        }
    }
    [Fact] public void Arrays_structures_and_maps_roundtrip_and_accept_edits()
    {
        var asset=Asset();var name=new FName(asset,"Values");
        var array=new ArrayPropertyData(name){ArrayType=new FName(asset,"IntProperty"),Value=[]};
        PropertyCodec.Set(array,JsonSerializer.SerializeToElement(new[]{1,2,3}),asset);
        var map=new MapPropertyData(new FName(asset,"Cultures")){KeyType=new FName(asset,"StrProperty"),ValueType=new FName(asset,"BoolProperty")};
        PropertyCodec.Set(map,JsonSerializer.SerializeToElement(new object[][]{["ja-JP",true],["en-US",false]}),asset);
        var structure=new StructPropertyData(name){StructType=new FName(asset,"SyntheticData"),Value=[new StrPropertyData(new FName(asset,"Text")){Value=new FString("before")},array,map]};
        PropertyCodec.Set(structure,JsonSerializer.SerializeToElement(new {Text="after"}),asset);
        var imported=asset.DeserializeJsonObject<StructPropertyData>(asset.SerializeJsonObject(structure));
        Assert.Equal(JsonSerializer.Serialize(PropertyCodec.Value(structure)),JsonSerializer.Serialize(PropertyCodec.Value(imported)));
        Assert.Throws<ArgumentException>(()=>PropertyCodec.Set(structure,JsonSerializer.SerializeToElement(new {Unknown=1}),asset));
        Assert.Throws<ArgumentException>(()=>PropertyCodec.Set(map,JsonSerializer.SerializeToElement(new object[][]{["x",true],["x",false]}),asset));
    }
    [Theory]
    [InlineData("ROW_0","ROW",1)]
    [InlineData("ROW_12","ROW",13)]
    [InlineData("ROW_01","ROW_01",0)]
    public void Row_names_preserve_unreal_number_encoding(string text,string value,int number)
    {
        var name=FName.FromString(Asset(),text);
        Assert.Equal(value,name.Value.ToString());Assert.Equal(number,name.Number);Assert.Equal(text,name.ToString());
    }
    [Fact] public void Numeric_overflow_is_rejected()
    {
        var value=new BytePropertyData(new FName(Asset(),"Byte")){ByteType=BytePropertyType.Byte};
        Assert.Throws<FormatException>(()=>PropertyCodec.Set(value,JsonSerializer.SerializeToElement(256)));
    }
}
