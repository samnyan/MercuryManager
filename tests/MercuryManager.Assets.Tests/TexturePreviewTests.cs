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

    [Fact]
    public void ResolvesViewPerlinNoiseGradientResourceSlot()
    {
        // Buffer 1 (bits 2) has Texture 0 (token: Buffer 1, Res 35, Reg 0) and Sampler 0 (token: Buffer 1, Res 36, Reg 0).
        // Buffer 2 (bits 4) has Material Texture 0 (token: Buffer 2, Res 0, Reg 1) and Sampler 0 (token: Buffer 2, Res 1, Reg 1).
        // ResourceBits = 0x6.
        // Array 0: SRVs, Array 1: Samplers, Array 2: ?, Array 3: ?, Array 4: Textures
        var samplersArray = new uint[] { 0, 3, 5, 0x01002400, 0xFFFFFFFF, 0x02000101, 0xFFFFFFFF };
        var texturesArray = new uint[] { 0, 3, 5, 0x01002300, 0xFFFFFFFF, 0x02000001, 0xFFFFFFFF };
        var arrays = new uint[][] { [], samplersArray, [], [], texturesArray };
        var table = new LegacyShaderResourceTable(0x6, arrays);

        var materialBindings = new LegacyMaterialShaderBindings(2, true, 0, 0, 1, 0, 0, 0);
        var expressions = new LegacyMaterialTextureExpression[]
        {
            new(0, "UI/Textures/Example.uasset", 0, 0, null)
        };
        var combined = new MaterialCombinedSampler[]
        {
            new("SPIRV_Cross_Combinedt0s0", "t0", "s0"),
            new("SPIRV_Cross_Combinedt1s1", "t1", "s1")
        };

        var slots = LegacyMaterialSamplerMapping.Resolve(table, materialBindings, expressions, combined);
        Assert.Equal(2, slots.Count);

        var noiseSlot = Assert.Single(slots, s => s.UniformName == "SPIRV_Cross_Combinedt0s0");
        Assert.Equal("t0", noiseSlot.ImageName);
        Assert.Equal("s0", noiseSlot.SamplerName);
        Assert.Equal("generated:ue419-perlin-gradient-v1", noiseSlot.AssetPath);
        var generated = Assert.IsType<GeneratedMaterialTexture>(noiseSlot.GeneratedSource);
        Assert.Equal("rgba8-linear", generated.Format);
        Assert.Equal("nearest", generated.Filter);
        Assert.Equal("repeat", generated.AddressMode);
        Assert.Equal(PerlinNoiseGradientGenerator.GenerateRgba(), Convert.FromBase64String(generated.RgbaBase64));
        Assert.Equal(-1, noiseSlot.ExpressionIndex);

        var matSlot = Assert.Single(slots, s => s.UniformName == "SPIRV_Cross_Combinedt1s1");
        Assert.Equal("t1", matSlot.ImageName);
        Assert.Equal("s1", matSlot.SamplerName);
        Assert.Equal("UI/Textures/Example.uasset", matSlot.AssetPath);
        Assert.Equal(0, matSlot.ExpressionIndex);
    }
}
