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
    [Fact]public void HeaderEditsPreservePayloadAndCanRemoveLoop()
    {
        var source=HcaEncoding.Encode(Wav(),[new(0,123,3456,2)]);var before=AudioInspection.Read(source);
        var edited=HcaLoopChunk.Edit(source,true,100,4000);var info=AudioInspection.Read(edited);Assert.Equal(100,info.LoopStart);Assert.Equal(4000,info.LoopEnd);Assert.Equal(before.Samples,info.Samples);
        int oldHeader=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(6));int newHeader=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(edited.AsSpan(6));Assert.Equal(source[oldHeader..],edited[newHeader..]);
        var disabled=AudioInspection.Read(HcaLoopChunk.Edit(edited,false,0,0));Assert.False(disabled.LoopEnabled);Assert.Equal(4800,disabled.Samples);
    }
    [Fact]public void ReadsWavSmplExclusiveEnd()
    {
        var original=Wav();using var s=new MemoryStream();s.Write(original);using var w=new BinaryWriter(s);w.Write("smpl"u8);w.Write(60);w.Write(new byte[28]);w.Write(1);w.Write(0);w.Write(0);w.Write(0);w.Write(123);w.Write(3455);w.Write(0);w.Write(0);var bytes=s.ToArray();System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),bytes.Length-8);var info=AudioInspection.Read(bytes);Assert.True(info.LoopEnabled);Assert.Equal(123,info.LoopStart);Assert.Equal(3456,info.LoopEnd);
    }
    [Fact]
    public void DecodesOggStreamToWavAndInspectsMetadata()
    {
        var wav = Wav(48000);
        var info = OggVorbisEncoder.VorbisInfo.InitVariableBitRate(2, 48000, 0.5f);
        var oggStream = new OggVorbisEncoder.OggStream(12345);
        var comments = new OggVorbisEncoder.Comments();
        comments.AddTag("LOOPSTART", "1000");
        comments.AddTag("LOOPLENGTH", "20000");
        oggStream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildInfoPacket(info));
        oggStream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildCommentsPacket(comments));
        oggStream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildBooksPacket(info));
        using var oggMs = new MemoryStream();
        while (oggStream.PageOut(out var page, true)) { oggMs.Write(page.Header); oggMs.Write(page.Body); }
        var proc = OggVorbisEncoder.ProcessingState.Create(info);
        float[][] buffer = [new float[48000], new float[48000]];
        for (int i = 0; i < 48000; i++) { float v = (float)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 0.3); buffer[0][i] = v; buffer[1][i] = v; }
        proc.WriteData(buffer, 48000, 0);
        while (proc.PacketOut(out var packet)) { oggStream.PacketIn(packet); while (oggStream.PageOut(out var page, false)) { oggMs.Write(page.Header); oggMs.Write(page.Body); } }
        proc.WriteEndOfStream();
        while (proc.PacketOut(out var packet)) { oggStream.PacketIn(packet); while (oggStream.PageOut(out var page, false)) { oggMs.Write(page.Header); oggMs.Write(page.Body); } }
        while (oggStream.PageOut(out var page, true)) { oggMs.Write(page.Header); oggMs.Write(page.Body); }

        var oggBytes = oggMs.ToArray();
        var audioInfo = AudioInspection.Read(oggBytes);
        Assert.Equal("ogg", audioInfo.Format);
        Assert.Equal(2, audioInfo.Channels);
        Assert.Equal(48000, audioInfo.SampleRate);
        Assert.True(audioInfo.LoopEnabled);
        Assert.Equal(1000, audioInfo.LoopStart);
        Assert.Equal(21000, audioInfo.LoopEnd);

        var decodedWav = AudioInspection.DecodeOggToWav(oggBytes, audioInfo.LoopStart, audioInfo.LoopEnd, audioInfo.LoopEnabled);
        var wavInfo = AudioInspection.Read(decodedWav);
        Assert.Equal("wav", wavInfo.Format);
        Assert.Equal(2, wavInfo.Channels);
        Assert.Equal(48000, wavInfo.SampleRate);
        Assert.True(wavInfo.LoopEnabled);
        Assert.Equal(1000, wavInfo.LoopStart);
        Assert.Equal(21000, wavInfo.LoopEnd);

        var hca = HcaEncoding.Encode(decodedWav);
        Assert.True(hca.Length > 0);
    }
    [Fact]public void RejectsInvalidWav()=>Assert.ThrowsAny<Exception>(()=>HcaEncoding.Encode([1,2,3]));
}
