using MercuryManager.Server;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class HcaEncodingTests
{
    public static byte[] Wav(int frames=4800)
    {
        using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write("RIFF"u8);w.Write(36+frames*4);w.Write("WAVEfmt "u8);w.Write(16);w.Write((ushort)1);w.Write((ushort)2);w.Write(48000);w.Write(192000);w.Write((ushort)4);w.Write((ushort)16);w.Write("data"u8);w.Write(frames*4);for(int i=0;i<frames;i++){short value=(short)(Math.Sin(i*2*Math.PI*440/48000)*12000);w.Write(value);w.Write(value);}return s.ToArray();
    }
    [Fact]public void EncodesEntireStereoTrackAndIndependentDecoderPreservesLength(){var bytes=HcaEncoding.Encode(Wav());using var stream=new MemoryStream(bytes);var info=new HcaDecoder(stream,0,0).HcaInfo;Assert.Equal(48000,info.SamplingRate);Assert.Equal(2,info.ChannelCount);Assert.Equal(4800,info.SampleCount);}
    [Theory]
    [InlineData(32,false)] [InlineData(64,false)] [InlineData(32,true)]
    public void EncodesFloatWav(int bits,bool extensible)
    {
        var wav=FloatWav(bits,extensible);var bytes=HcaEncoding.Encode(wav);
        using var stream=new MemoryStream(bytes);var info=new HcaDecoder(stream,0,0).HcaInfo;
        Assert.Equal(4800,info.SampleCount);Assert.Equal(2,info.ChannelCount);Assert.Equal(48000,info.SamplingRate);
    }
    public static byte[] FloatWav(int bits=32,bool extensible=false,bool invalid=false)
    {
        using var s=new MemoryStream();using var w=new BinaryWriter(s);
        int fmtSize=extensible?40:16,size=bits/8,dataSize=4800*2*size;
        w.Write("RIFF"u8);w.Write(20+fmtSize+dataSize);w.Write("WAVEfmt "u8);w.Write(fmtSize);w.Write((ushort)(extensible?65534:3));w.Write((ushort)2);w.Write(48000);w.Write(48000*2*size);w.Write((ushort)(2*size));w.Write((ushort)bits);
        if(extensible){w.Write((ushort)22);w.Write((ushort)bits);w.Write(3);w.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());}
        w.Write("data"u8);w.Write(dataSize);
        for(int i=0;i<4800*2;i++){double value=invalid?double.NaN:Math.Sin(i/2*2*Math.PI*440/48000)*0.3;if(bits==32)w.Write((float)value);else w.Write(value);}
        return s.ToArray();
    }
    [Fact]public void RejectsNonfiniteFloatSamples()=>Assert.Throws<InvalidDataException>(()=>HcaEncoding.Encode(FloatWav(invalid:true)));
    [Fact]public void RejectsTruncatedFloatChunk()=>Assert.Throws<InvalidDataException>(()=>HcaEncoding.Encode(FloatWav()[..^1]));
    [Theory][InlineData(0,4800)][InlineData(123,3456)]
    public void EncodesLoopChunkWithExactSamplePoints(int start,int end)
    {
        var bytes=HcaEncoding.Encode(Wav(),[new(0,start,end,2)]);
        using var stream=new MemoryStream(bytes);var info=new HcaDecoder(stream,0,0).HcaInfo;
        Assert.True(info.LoopEnabled);
        var encoded=new VGAudio.Containers.Hca.HcaReader().ReadFormat(bytes);
        Assert.Equal(start,encoded.LoopStart);Assert.Equal(end,encoded.LoopEnd);Assert.Equal(4800,info.SampleCount);Assert.Equal(start,info.LoopStartSample);Assert.Equal(end,info.LoopEndSample);
    }
    [Fact]public void RejectsConflictingSharedLoops()=>Assert.Throws<InvalidDataException>(()=>HcaEncoding.Encode(Wav(),[new(0,0,4800,2),new(1,100,4800,2)]));
    [Fact]public void RejectsInvalidWav()=>Assert.ThrowsAny<Exception>(()=>HcaEncoding.Encode([1,2,3]));
}
