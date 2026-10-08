using System.Buffers.Binary;
namespace MercuryManager.Server;

// Keep IDs independent of physical order. Callers stage the result before publishing the paired ACB.
public static class AwbWriter
{
    public sealed record Entry(ushort Id,long Length,Func<Stream> Open);
    public static void Write(Stream output,IReadOnlyList<Entry> entries,ushort alignment=32,ushort subkey=0,Action<long,long>? progress=null)
    {
        if(!output.CanSeek||!output.CanWrite)throw new ArgumentException("Seekable output required.");
        if(alignment==0||entries.Select(e=>e.Id).Distinct().Count()!=entries.Count||entries.Any(e=>e.Length<0))throw new InvalidDataException("Invalid AWB entries.");
        long header=checked(16L+entries.Count*2L+(entries.Count+1L)*4L);
        long size=header;
        foreach(var entry in entries)size=checked(Align(size,alignment)+entry.Length);
        if(size>uint.MaxValue)throw new InvalidDataException("AWB exceeds 32-bit offset limit.");
        output.Position=0;output.SetLength(0);
        using var writer=new BinaryWriter(output,System.Text.Encoding.ASCII,true);
        writer.Write("AFS2"u8);writer.Write((byte)2);writer.Write((byte)4);writer.Write((ushort)2);writer.Write(entries.Count);writer.Write(alignment);writer.Write(subkey);
        foreach(var entry in entries)writer.Write(entry.Id);
        long offsets=output.Position;for(int i=0;i<=entries.Count;i++)writer.Write(0u);
        var pointers=new uint[entries.Count+1];pointers[0]=(uint)header;
        var buffer=new byte[1024*1024];long copied=0,total=entries.Sum(e=>e.Length);
        for(int i=0;i<entries.Count;i++)
        {
            var entry=entries[i];long padding=Align(output.Position,alignment)-output.Position;
            for(long j=0;j<padding;j++)writer.Write((byte)0);
            using var input=entry.Open();long remaining=entry.Length;
            while(remaining>0){int n=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(n==0)throw new EndOfStreamException("Truncated AWB payload.");output.Write(buffer,0,n);remaining-=n;copied+=n;progress?.Invoke(copied,total);}
            pointers[i+1]=checked((uint)output.Position);
        }
        long end=output.Position;output.Position=offsets;foreach(var pointer in pointers)writer.Write(pointer);output.Position=end;
    }
    private static long Align(long position,ushort alignment)=>checked(position+(alignment-position%alignment)%alignment);
}
