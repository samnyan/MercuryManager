using VGAudio.Containers.Wave;
using VGAudio.Containers.Hca;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;
public static class HcaEncoding
{
    // VGAudio's WAV reader accepts integer PCM only. Normalize IEEE float first.
    private static byte[] NormalizeWav(byte[] wav)
    {
        if(wav.Length<12||!wav.AsSpan(0,4).SequenceEqual("RIFF"u8)||!wav.AsSpan(8,4).SequenceEqual("WAVE"u8))throw new InvalidDataException("Invalid RIFF WAV.");
        int end=checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4))+8);
        if(end>wav.Length||end<12)throw new InvalidDataException("Truncated WAV.");
        int fmt=-1,fmtLength=0;var chunks=new List<(int Offset,int Length)>();
        for(int offset=12;offset<end;)
        {
            if(end-offset<8)throw new InvalidDataException("Truncated WAV chunk.");
            int length=checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset+4)));
            int next=checked(offset+8+length+(length&1));if(next>end)throw new InvalidDataException("Truncated WAV chunk.");
            if(wav.AsSpan(offset,4).SequenceEqual("fmt "u8)){if(fmt>=0)throw new InvalidDataException("Duplicate WAV format.");fmt=offset+8;fmtLength=length;}
            if(wav.AsSpan(offset,4).SequenceEqual("data"u8))chunks.Add((offset+8,length));
            offset=next;
        }
        if(fmtLength<16||chunks.Count!=1)throw new InvalidDataException("WAV requires format and one data chunk.");
        ushort U16(int offset)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt+offset));
        int tag=U16(0),channels=U16(2),bits=U16(14),align=U16(12);
        if(tag==65534)
        {
            if(fmtLength<40||U16(16)<22)throw new InvalidDataException("Invalid extensible WAV format.");
            var guid=new Guid(wav.AsSpan(fmt+24,16));
            if(guid==new Guid("00000003-0000-0010-8000-00aa00389b71"))tag=3;
            else if(guid==new Guid("00000001-0000-0010-8000-00aa00389b71"))tag=1;
            else throw new InvalidDataException("Unsupported extensible WAV encoding.");
        }
        if(tag==1)return wav;
        if(tag!=3||bits is not (32 or 64))throw new InvalidDataException($"Unsupported WAV encoding {tag}, {bits}-bit.");
        int rate=checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(fmt+4)));
        var data=chunks[0];int size=bits/8;
        if(channels is <1 or >2||rate is <8000 or >96000||align!=channels*size||data.Length==0||data.Length%align!=0||data.Length/align>(long)rate*600)throw new InvalidDataException("Invalid float WAV dimensions or duration.");
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        int pcmBytes=data.Length/size*2;
        writer.Write("RIFF"u8);writer.Write(36+pcmBytes);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)channels);writer.Write(rate);writer.Write(rate*channels*2);writer.Write((ushort)(channels*2));writer.Write((ushort)16);writer.Write("data"u8);writer.Write(pcmBytes);
        for(int offset=data.Offset;offset<data.Offset+data.Length;offset+=size)
        {
            double value=size==4?System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(wav.AsSpan(offset)):System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(wav.AsSpan(offset));
            if(!double.IsFinite(value))throw new InvalidDataException("Float WAV contains non-finite samples.");
            writer.Write((short)Math.Clamp(Math.Round(Math.Clamp(value,-1,1)*32768),short.MinValue,short.MaxValue));
        }
        return output.ToArray();
    }
    public static byte[] Encode(byte[] wav,WaveformLoopEdit[]? loops=null)
    {
        if(wav.Length>128*1024*1024)throw new InvalidDataException("WAV upload limit: 128 MiB.");
        var format=new WaveReader().ReadFormat(NormalizeWav(wav));
        if(format.ChannelCount is <1 or >2||format.SampleRate is <8000 or >96000||format.SampleCount<=0||format.SampleCount>format.SampleRate*600)throw new InvalidDataException("WAV must be mono/stereo, 8–96 kHz and no longer than 10 minutes.");
        var settings=loops?.Select(e=>(Enabled:e.LoopFlag!=0,e.LoopStart,e.LoopEnd)).Distinct().ToArray();
        if(settings is {Length:>1})throw new InvalidDataException("Shared HCA requires identical loop settings on all waveforms.");
        if(settings is {Length:1}&&(settings[0].LoopStart<0||settings[0].LoopEnd<=settings[0].LoopStart||settings[0].LoopEnd>format.SampleCount))throw new InvalidDataException("Loop points outside new audio.");
        // Encode the entire stream first: the encoder's looping path trims the tail.
        if(settings is not null)format=format.WithLoop(false);
        var hca=new HcaWriter().GetFile(format,new HcaConfiguration {TrimFile=false});
        if(settings is {Length:1}&&settings[0].Enabled)hca=HcaLoopChunk.Add(hca,settings[0].LoopStart,settings[0].LoopEnd);
        using var input=new MemoryStream(hca);using var pcm=new HcaWaveStream(input,0,0);var buffer=new byte[32768];long read=0;int n;while((n=pcm.Read(buffer,0,buffer.Length))>0)read+=n;
        if(read!=pcm.Length||pcm.WaveFormat.SampleRate!=format.SampleRate||read/pcm.WaveFormat.BlockAlign!=format.SampleCount)throw new InvalidDataException($"HCA encoding validation failed: PCM {read}/{pcm.Length}, samples {read/pcm.WaveFormat.BlockAlign}/{format.SampleCount}, VGAudio {new HcaReader().ReadFormat(hca).SampleCount}.");return hca;
    }
}
