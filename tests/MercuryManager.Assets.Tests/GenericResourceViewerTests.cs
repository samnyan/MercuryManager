using MercuryManager.Server;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Xunit;
using System.Text.Json;
namespace MercuryManager.Assets.Tests;
public class GenericResourceViewerTests
{
    [Fact]public void ExposesSectionsAndPropertiesWithoutOpaquePayloadExpansion()
    {
        var a=new UAsset();a.ClearNameIndexList();a.Imports=[];
        a.Exports=[new NormalExport(a,new byte[4096]){ClassIndex=new FPackageIndex(0),ObjectName=new FName(a,"Example"),Data=[new StrPropertyData(new FName(a,"Title")){Value=new FString("hello") }]}];
        var result=JsonSerializer.SerializeToElement(GenericResourceViewer.Describe(a));var data=result.GetProperty("data");
        Assert.True(data.TryGetProperty("Header",out _));Assert.True(data.TryGetProperty("NameMap",out _));Assert.True(data.TryGetProperty("Imports",out _));
        var e=data.GetProperty("Exports")[0];Assert.Equal("Example",e.GetProperty("Name").GetString());Assert.Equal(4096,e.GetProperty("CustomSerialization").GetProperty("Bytes").GetInt32());Assert.Equal(256,e.GetProperty("CustomSerialization").GetProperty("HexPreview").GetString()!.Length);Assert.True(e.GetProperty("Properties").TryGetProperty("Title[0]",out _));
    }
}
