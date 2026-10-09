using System.Buffers.Binary;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;
public static class HcaLoopChunk
{
    static ushort Crc(ReadOnlySpan<byte> bytes){ushort crc=0;foreach(byte b in bytes){crc^=(ushort)(b<<8);for(int i=0;i<8;i++)crc=(ushort)((crc&0x8000)!=0?(crc<<1)^0x8005:crc<<1);}return crc;}
    public static byte[] Add(byte[] source,long start,long end)=>Edit(source,true,start,end);
    public static byte[] Edit(byte[] source,bool enabled,long start,long end)
    {
        using var input=new MemoryStream(source);var info=new HcaDecoder(input,0,0).HcaInfo;
        int size=info.HeaderSize;
        if(info.EncryptionEnabled)throw new InvalidDataException("Encrypted HCA header editing is not supported; upload WAV or preserve the HCA settings.");
        if(size<10||size>source.Length||Crc(source.AsSpan(0,size-2))!=BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(size-2)))throw new InvalidDataException("Invalid HCA header CRC.");
        if(enabled&&(start<0||end<=start||end>info.SampleCount))throw new InvalidDataException("Loop points outside HCA audio.");
        using var header=new MemoryStream();header.Write(source,0,8);bool inserted=false;
        void Loop(){if(!enabled||inserted)return;inserted=true;var b=new byte[16];"loop"u8.CopyTo(b);long first=start+info.EncoderDelay,last=end+info.EncoderDelay;
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4),checked((uint)(first/1024)));BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8),checked((uint)((last-1)/1024)));
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(12),checked((ushort)(first%1024)));BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(14),checked((ushort)((1024-last%1024)%1024)));header.Write(b);}
        for(int p=8;p<size-2;)
        {
            if(p+4>size-2)throw new InvalidDataException("Truncated HCA chunk.");string tag=System.Text.Encoding.ASCII.GetString(source,p,4);
            if(tag=="pad\0"||source.AsSpan(p,size-2-p).IndexOfAnyExcept((byte)0)<0){Loop();header.Write(source,p,size-2-p);p=size-2;break;}
            int n=tag switch{"fmt\0"=>16,"comp"=>16,"dec\0"=>12,"vbr\0"=>8,"ath\0"=>6,"loop"=>16,"ciph"=>6,"rva\0"=>8,"comm"=>5+source[p+4],_=>throw new InvalidDataException("Unsupported HCA chunk: "+tag)};
            if(p+n>size-2)throw new InvalidDataException("Truncated HCA chunk.");
            if(tag=="loop")Loop();else{if(tag is "ciph" or "rva\0" or "comm")Loop();header.Write(source,p,n);}p+=n;
        }
        Loop();int newSize=checked((int)header.Length+2);if(newSize>65535)throw new InvalidDataException("HCA header too large.");
        var result=new byte[newSize+source.Length-size];header.ToArray().CopyTo(result,0);BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6),checked((ushort)newSize));BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(newSize-2),Crc(result.AsSpan(0,newSize-2)));source.AsSpan(size).CopyTo(result.AsSpan(newSize));
        using var check=new MemoryStream(result);var parsed=new HcaDecoder(check,0,0).HcaInfo;
        if(parsed.LoopEnabled!=enabled||enabled&&(parsed.LoopStartSample!=start||parsed.LoopEndSample!=end)||parsed.SampleCount!=info.SampleCount||!source.AsSpan(size).SequenceEqual(result.AsSpan(newSize)))throw new InvalidDataException("HCA loop/payload verification failed.");return result;
    }
}
