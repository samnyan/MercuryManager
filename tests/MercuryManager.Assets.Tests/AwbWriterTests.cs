using MercuryManager.Server;
using CUE4Parse.UE4.Criware.Readers;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class AwbWriterTests
{
    [Fact]public void PreservesSparseIdsAlignmentAndSubkey()
    {
        using var output=new MemoryStream();
        AwbWriter.Write(output,[new(31,3,()=>new MemoryStream([1,2,3])),new(7,4,()=>new MemoryStream([4,5,6,7]))],32,123);
        output.Position=0;using var reader=new AwbReader(output);
        Assert.Equal((ushort)123,reader.Subkey);Assert.Equal(new[]{31,7},reader.Waves.Select(w=>w.WaveId));
        foreach(var w in reader.Waves)Assert.Equal(0,w.Offset%32);
        using var first=reader.GetWaveSubfileStream(reader.Waves[0]);var bytes=new byte[3];first.ReadExactly(bytes);Assert.Equal(new byte[]{1,2,3},bytes);
    }
    [Fact]public void RejectsDuplicateIds(){using var output=new MemoryStream();Assert.Throws<InvalidDataException>(()=>AwbWriter.Write(output,[new(1,0,()=>Stream.Null),new(1,0,()=>Stream.Null)]));}
    [Fact]public void RejectsTruncatedPayload(){using var output=new MemoryStream();Assert.Throws<EndOfStreamException>(()=>AwbWriter.Write(output,[new(1,4,()=>new MemoryStream([1]))]));}
}
