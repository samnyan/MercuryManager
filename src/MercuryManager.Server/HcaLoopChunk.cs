using System.Buffers.Binary;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;

internal static class HcaLoopChunk
{
    public static byte[] Add(byte[] source,long start,long end)
    {
        using var input=new MemoryStream(source);var info=new HcaDecoder(input,0,0).HcaInfo;
        if(info.LoopEnabled||info.EncryptionEnabled||start<0||end<=start||end>info.SampleCount)throw new InvalidDataException("Unsupported HCA loop edit.");
        // Managed encoder emits fmt then comp; parse their tags before inserting loop.
        if(source.Length<40||!source.AsSpan(8,4).SequenceEqual("fmt\0"u8)||!source.AsSpan(24,4).SequenceEqual("comp"u8))throw new InvalidDataException("Unsupported encoded HCA header layout.");
        int oldSize=info.HeaderSize,newSize=checked(oldSize+16);if(newSize>ushort.MaxValue)throw new InvalidDataException("HCA header too large.");
        var result=new byte[source.Length+16];source.AsSpan(0,40).CopyTo(result);source.AsSpan(40).CopyTo(result.AsSpan(56));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6),checked((ushort)newSize));"loop"u8.CopyTo(result.AsSpan(40));
        long first=start+info.EncoderDelay,last=end+info.EncoderDelay;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(44),checked((uint)(first/1024)));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(48),checked((uint)((last-1)/1024)));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(52),checked((ushort)(first%1024)));
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(54),checked((ushort)((1024-last%1024)%1024)));
        ushort crc=0;foreach(byte b in result.AsSpan(0,newSize-2)){crc^=(ushort)(b<<8);for(int bit=0;bit<8;bit++)crc=(ushort)((crc&0x8000)!=0?(crc<<1)^0x8005:crc<<1);}
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(newSize-2),crc);
        using var check=new MemoryStream(result);var parsed=new HcaDecoder(check,0,0).HcaInfo;
        if(!parsed.LoopEnabled||parsed.LoopStartSample!=start||parsed.LoopEndSample!=end||parsed.SampleCount!=info.SampleCount)throw new InvalidDataException("HCA loop verification failed.");return result;
    }
}
