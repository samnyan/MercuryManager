using System.Buffers.Binary;
using System.Reflection;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;
public static class AudioDetails
{
    public static object Read(byte[] bytes)
    {
        var summary=AudioInspection.Read(bytes);
        if(summary.Format!="hca")return new {fileBytes=bytes.LongLength,summary};
        using var stream=new MemoryStream(bytes);var h=new HcaDecoder(stream,0,0).HcaInfo;
        var fields=new SortedDictionary<string,object?>();
        foreach(var f in h.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))fields[f.Name]=f.GetValue(h);
        foreach(var p in h.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0))fields[p.Name]=p.GetValue(h);
        int size=h.HeaderSize;if(size<10||size>bytes.Length)throw new InvalidDataException("Invalid HCA header size.");
        ushort crc=0;foreach(byte b in bytes.AsSpan(0,size-2)){crc^=(ushort)(b<<8);for(int i=0;i<8;i++)crc=(ushort)((crc&0x8000)!=0?(crc<<1)^0x8005:crc<<1);}
        var chunks=new List<object>();
        for(int pos=8;pos<size-2;)
        {
            int left=size-2-pos;if(left<4){chunks.Add(new{offset=pos,tag="trailing",bytes=left,hex=Convert.ToHexString(bytes.AsSpan(pos,left))});break;}
            string tag=new string(bytes.AsSpan(pos,4).ToArray().Select(b=>(char)(b&127)).ToArray()).TrimEnd('\0');
            int n=tag switch{"fmt"=>16,"comp"=>16,"dec"=>12,"vbr"=>8,"ath"=>6,"loop"=>16,"ciph"=>6,"rva"=>8,"comm" when left>=5=>5+bytes[pos+4],_=>left};
            if(n>left){chunks.Add(new{offset=pos,tag,bytes=left,error="Truncated chunk",hex=Convert.ToHexString(bytes.AsSpan(pos,left))});break;}
            var values=new Dictionary<string,object?>();
            int U16(int o)=>BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos+o));
            uint U32(int o)=>BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos+o));
            switch(tag)
            {
                case "fmt":values["channels"]=bytes[pos+4];values["sampleRate"]=U32(4)&0xFFFFFF;values["blockCount"]=U32(8);values["encoderDelay"]=U16(12);values["encoderPadding"]=U16(14);break;
                case "comp":values["blockSize"]=U16(4);values["minResolution"]=bytes[pos+6];values["maxResolution"]=bytes[pos+7];values["trackCount"]=bytes[pos+8];values["channelConfig"]=bytes[pos+9];values["totalBandCount"]=bytes[pos+10];values["baseBandCount"]=bytes[pos+11];values["stereoBandCount"]=bytes[pos+12];values["bandsPerHfrGroup"]=bytes[pos+13];values["reserved"]=U16(14);break;
                case "dec":values["blockSize"]=U16(4);values["minResolution"]=bytes[pos+6];values["maxResolution"]=bytes[pos+7];values["totalBandCountStored"]=bytes[pos+8];values["baseBandCountStored"]=bytes[pos+9];values["trackCountStored"]=bytes[pos+10]>>4;values["channelConfig"]=bytes[pos+10]&15;values["type"]=bytes[pos+11];break;
                case "loop":values["startBlock"]=U32(4);values["endBlockInclusive"]=U32(8);values["startDelay"]=U16(12);values["endPadding"]=U16(14);break;
                case "ath":case "ciph":values["type"]=U16(4);break;
                case "vbr":values["maxFrameSize"]=U16(4);values["noiseLevel"]=U16(6);break;
                case "rva":values["volume"]=BitConverter.Int32BitsToSingle(unchecked((int)U32(4)));break;
                case "comm":values["length"]=bytes[pos+4];values["comment"]=System.Text.Encoding.UTF8.GetString(bytes,pos+5,n-5);break;
            }
            chunks.Add(new{offset=pos,tag,bytes=n,fields=values,hex=Convert.ToHexString(bytes.AsSpan(pos,n))});pos+=n;
        }
        return new{fileBytes=bytes.LongLength,summary,version=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)),headerBytes=size,payloadBytes=bytes.LongLength-size,headerCrc=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(size-2)),calculatedCrc=crc,crcValid=crc==BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(size-2)),decoderFields=fields,chunks,headerHex=Convert.ToHexString(bytes.AsSpan(0,size))};
    }
}
