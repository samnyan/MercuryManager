using MercuryManager.Server;
using Xunit;
namespace MercuryManager.Assets.Tests;
public sealed class TexturePreviewTests
{
    [Theory]
    [InlineData("MusicParameterTable","JacketAssetName","S02/uT_J_S02_240","UI/Textures/JACKET/S02/uT_J_S02_240.uasset")]
    [InlineData("IconTable","IconTextureName","S01/uT_UICN_S01_06_003","UI/Textures/USERICON/S01/uT_UICN_S01_06_003.uasset")]
    public void KnownFieldsResolve(string table,string field,string value,string expected)=>Assert.Equal(expected,TexturePreviewService.ResolveRelativePath(table,field,value));
    [Theory]
    [InlineData("../secret")][InlineData("/etc/passwd")][InlineData("S01\\texture")][InlineData("S01/../texture")][InlineData("")][InlineData("S01//texture")][InlineData("Texture.foo")]
    public void UnsafePathsRejected(string value)=>Assert.Throws<ArgumentException>(()=>TexturePreviewService.ResolveRelativePath("IconTable","IconTextureName",value));
    [Fact]public void UnknownFieldsRejected()=>Assert.Throws<ArgumentException>(()=>TexturePreviewService.ResolveRelativePath("IconTable","Other","texture"));
}
