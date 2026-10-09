using System.Buffers.Binary;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using VGAudio.Containers.Wave;
namespace MercuryManager.Server;
public sealed record AudioFileInfo(string Format,int Channels,int SampleRate,long Samples,bool LoopEnabled,long LoopStart,long LoopEnd,bool Encrypted=false);
public static class AudioInspection
{
    public static AudioFileInfo Read(byte[] bytes)
    {
        if(bytes.Length>=12&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8))
        {
            var format=new WaveReader().ReadFormat(HcaEncoding.NormalizeWav(bytes));long start=0,end=format.SampleCount;bool loop=false;
            for(int p=12;p+8<=bytes.Length;){int n=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+4)));if(n<0||p+8L+n>bytes.Length)throw new InvalidDataException("Invalid WAV chunk.");
                if(bytes.AsSpan(p,4).SequenceEqual("smpl"u8)&&n>=60&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+36))>0)
                {
                    if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+48))!=0)throw new InvalidDataException("Only forward WAV loops are supported.");
                    start=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+52));end=(long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+56))+1;loop=true;
                }p=checked(p+8+n+(n&1));
            }
            if(loop&&(start<0||end<=start||end>format.SampleCount))throw new InvalidDataException("WAV loop outside audio.");
            return new("wav",format.ChannelCount,format.SampleRate,format.SampleCount,loop,start,end);
        }
        using var stream=new MemoryStream(bytes);var h=new HcaDecoder(stream,0,0).HcaInfo;
        return new("hca",h.ChannelCount,h.SamplingRate,h.SampleCount,h.LoopEnabled,h.LoopEnabled?h.LoopStartSample:0,h.LoopEnabled?h.LoopEndSample:h.SampleCount,h.EncryptionEnabled);
    }
}
