using System.Buffers.Binary;
using System.Text;
namespace MercuryManager.Server;

// Lossless column storage/type model for the cooked CRI @UTF tables.
public sealed class CriUtf
{
    public sealed record Column(string Name,byte Flags,byte Type,byte[]? Constant);
    public string Name{get;private set;}="";
    public List<Column> Columns{get;}=[];
    public List<Dictionary<string,byte[]>> Rows{get;}=[];
    private byte[] strings=[],data=[];
    private ushort version;
    static int Width(byte type)=>type switch{0 or 1=>1,2 or 3=>2,4 or 5 or 8 or 10=>4,6 or 7 or 9 or 11=>8,12=>16,_=>throw new InvalidDataException("Unsupported UTF column type.")};
    public static CriUtf Read(byte[] bytes)
    {
        if(bytes.Length<32||!bytes.AsSpan(0,4).SequenceEqual("@UTF"u8))throw new InvalidDataException("Invalid UTF.");
        uint U32(int p)=>BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p,4));
        ushort U16(int p)=>BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p,2));
        int size=checked((int)U32(4)+8),rowsOffset=U16(10)+8,str=checked((int)U32(12)+8),blob=checked((int)U32(16)+8);
        if(size>bytes.Length||rowsOffset<32||str<rowsOffset||blob<str||blob>size)throw new InvalidDataException("Invalid UTF offsets.");
        var table=new CriUtf{version=U16(8),strings=bytes[str..blob],data=bytes[blob..size]};
        string Text(uint offset){if(offset>=table.strings.Length)throw new InvalidDataException("Invalid UTF string.");int end=Array.IndexOf(table.strings,(byte)0,(int)offset);if(end<0)throw new InvalidDataException("Unterminated string.");return Encoding.UTF8.GetString(table.strings,(int)offset,end-(int)offset);}
        table.Name=Text(U32(20));int pos=32,rowWidth=U16(26),rowCount=checked((int)U32(28));
        if((long)rowsOffset+(long)rowWidth*rowCount>str)throw new InvalidDataException("Invalid UTF rows.");
        for(int i=0;i<U16(24);i++){
            byte flags=bytes[pos++],type=(byte)(flags&15);var name=Text(U32(pos));pos+=4;byte[]? constant=null;
            if((flags&0x20)!=0){constant=bytes.AsSpan(pos,Width(type)).ToArray();pos+=constant.Length;}
            if(pos>rowsOffset)throw new InvalidDataException("UTF schema exceeds rows.");table.Columns.Add(new(name,(byte)(flags&0xf0),type,constant));
        }
        for(int i=0;i<rowCount;i++){pos=rowsOffset+i*rowWidth;var row=new Dictionary<string,byte[]>();foreach(var c in table.Columns){int n=Width(c.Type);row[c.Name]=(c.Flags&0x40)!=0?bytes.AsSpan(pos,n).ToArray():c.Constant?.ToArray()??new byte[n];if((c.Flags&0x40)!=0)pos+=n;}if(pos>rowsOffset+(i+1)*rowWidth)throw new InvalidDataException("UTF row width mismatch.");table.Rows.Add(row);}
        return table;
    }
    public byte[] Blob(int row,string column){var v=Rows[row][column];int offset=checked((int)BinaryPrimitives.ReadUInt32BigEndian(v)),length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(v.AsSpan(4)));return data.AsSpan(offset,length).ToArray();}
    public void SetBlob(int row,string column,byte[] value)
    {
        var c=Columns.Single(c=>c.Name==column);if(c.Type!=11)throw new InvalidDataException("Not a blob column.");
        int offset=data.Length;data=data.Concat(value).ToArray();var pointer=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(pointer,(uint)offset);BinaryPrimitives.WriteUInt32BigEndian(pointer.AsSpan(4),(uint)value.Length);
        if((c.Flags&0x40)!=0)Rows[row][column]=pointer;
        else if(Rows.Count==1){int i=Columns.IndexOf(c);Columns[i]=c with{Flags=0x30,Constant=pointer};Rows[row][column]=pointer;}
        else throw new InvalidDataException("Cannot edit shared constant blob in multi-row table.");
    }
    public long Number(int row,string column){var b=Rows[row][column];return b.Length switch{1=>b[0],2=>BinaryPrimitives.ReadUInt16BigEndian(b),4=>BinaryPrimitives.ReadUInt32BigEndian(b),8=>checked((long)BinaryPrimitives.ReadUInt64BigEndian(b)),_=>throw new InvalidDataException("Not an integer.")};}
    public void Promote(string name){int i=Columns.FindIndex(c=>c.Name==name);if(i<0)throw new InvalidDataException("Missing column.");Columns[i]=Columns[i] with{Flags=0x50,Constant=null};}
    public void SetNumber(int row,string name,long value){Promote(name);var b=Rows[row][name];switch(b.Length){case 1:b[0]=checked((byte)value);break;case 2:BinaryPrimitives.WriteUInt16BigEndian(b,checked((ushort)value));break;case 4:BinaryPrimitives.WriteUInt32BigEndian(b,checked((uint)value));break;case 8:BinaryPrimitives.WriteUInt64BigEndian(b,checked((ulong)value));break;default:throw new InvalidDataException("Not an integer.");}}
    public void SetText(int row,string name,string text){Promote(name);var bytes=Encoding.UTF8.GetBytes(text+"\0");uint offset=(uint)strings.Length;strings=strings.Concat(bytes).ToArray();BinaryPrimitives.WriteUInt32BigEndian(Rows[row][name],offset);}
    public int CloneRow(int donor){if(Rows.Count>=ushort.MaxValue)throw new InvalidDataException("Table row limit.");Rows.Add(Rows[donor].ToDictionary(k=>k.Key,k=>k.Value.ToArray()));return Rows.Count-1;}
    public byte[] Write()
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        void U16(int v){Span<byte>b=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,checked((ushort)v));writer.Write(b);}
        void U32(uint v){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,v);writer.Write(b);}
        uint StringOffset(string name){var encoded=Encoding.UTF8.GetBytes(name+"\0");for(int i=0;i<=strings.Length-encoded.Length;i++)if(strings.AsSpan(i,encoded.Length).SequenceEqual(encoded))return (uint)i;uint offset=(uint)strings.Length;strings=strings.Concat(encoded).ToArray();return offset;}
        uint nameOffset=StringOffset(Name);var names=Columns.Select(c=>StringOffset(c.Name)).ToArray();
        writer.Write(new byte[32]);for(int i=0;i<Columns.Count;i++){var c=Columns[i];writer.Write((byte)(c.Flags|c.Type));U32(names[i]);if((c.Flags&0x20)!=0)writer.Write(c.Constant??new byte[Width(c.Type)]);}
        int rowsOffset=checked((int)stream.Position),rowWidth=Columns.Where(c=>(c.Flags&0x40)!=0).Sum(c=>Width(c.Type));
        foreach(var row in Rows)foreach(var c in Columns.Where(c=>(c.Flags&0x40)!=0)){var value=row[c.Name];if(value.Length!=Width(c.Type))throw new InvalidDataException("Invalid cell width.");writer.Write(value);}
        uint str=(uint)stream.Position;writer.Write(strings);uint blob=(uint)stream.Position;writer.Write(data);uint size=(uint)stream.Length;
        stream.Position=0;writer.Write("@UTF"u8);U32(size-8);U16(version);U16(rowsOffset-8);U32(str-8);U32(blob-8);U32(nameOffset);U16(Columns.Count);U16(rowWidth);U32((uint)Rows.Count);return stream.ToArray();
    }
}
