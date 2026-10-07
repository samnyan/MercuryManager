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
    [Theory]
    [InlineData("BoostItemTable","IconTexturePath","CHANGE/uT_CH_SRicn_02","UI/Textures/CHANGE/uT_CH_SRicn_02.uasset")]
    [InlineData("EMoneyBrandTable","IconTexturePath","/Game/UI/Textures/EMoney/0001-01-00","UI/Textures/Emoney/0001-01-00.uasset")]
    [InlineData("UserPlateBackgroundTable","UserPlateBacgroundTextureName","uT_US_1","UI/Textures/USERPLATE/uT_US_1.uasset")]
    public void ExtendedFieldsResolve(string table,string field,string value,string expected)=>Assert.Equal(expected,TexturePreviewService.ResolveRelativePath(table,field,value));
    [Fact]public void MaterialIsNotAStaticTexture()=>Assert.Throws<InvalidDataException>(()=>TexturePreviewService.ResolveRelativePath("SugorokuUniqueParameterTable","SugorokuCenterImage","Material/Gate/uM_SR003"));
    [Fact]public void UnknownFieldsRejected()=>Assert.Throws<ArgumentException>(()=>TexturePreviewService.ResolveRelativePath("IconTable","Other","texture"));
}
