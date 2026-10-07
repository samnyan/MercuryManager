using MercuryManager.Server;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class TextureAuthoringTests
{
    private static byte[] Fixture()
    {
        using var s=new MemoryStream();using var w=new BinaryWriter(s);
        w.Write((ushort)1);w.Write((ushort)1);w.Write(1);w.Write(4);w.Write(0);w.Write(128);
        w.Write(2);w.Write(2);w.Write(1);w.Write(12);w.Write(System.Text.Encoding.ASCII.GetBytes("PF_B8G8R8A8\0"));
        w.Write(0);w.Write(1);w.Write(1);w.Write((uint)0x48);w.Write(16);w.Write(16);w.Write((long)80);w.Write(new byte[16]);w.Write(2);w.Write(2);w.Write(0);w.Write(0);return s.ToArray();
    }
    [Fact]public void ReadsRestrictedInlineLayout(){var l=TextureAuthoring.Inspect(Fixture());Assert.Equal(2,l.Width);Assert.Equal(16,l.Length);Assert.Equal(80,l.Pixels);}
    [Fact]public void RejectsStreamingFlags(){var bytes=Fixture();BitConverter.GetBytes((uint)0x100).CopyTo(bytes,60);Assert.Throws<InvalidDataException>(()=>TextureAuthoring.Inspect(bytes));}
    [Fact]public void RejectsTrailingData(){Assert.Throws<InvalidDataException>(()=>TextureAuthoring.Inspect(Fixture().Concat(new byte[1]).ToArray()));}
    [Theory][InlineData("../foo.uasset")][InlineData("/foo.uasset")][InlineData("UI//foo.uasset")][InlineData("UI/foo.png")][InlineData("UI\\foo.uasset")]
    public void RejectsUnsafePaths(string value)=>Assert.Throws<ArgumentException>(()=>ResourceService.Validate(value));
    [Fact]public void AcceptsContentRelativePath()=>Assert.Equal("UI/Textures/JACKET/New/cover.uasset",ResourceService.Validate("UI/Textures/JACKET/New/cover.uasset"));
}
