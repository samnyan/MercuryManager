using System.Buffers.Binary;
namespace MercuryManager.Server;

public sealed record AwbIndexEntry(uint Id,long Offset,long Length);
public sealed record AwbIndex(long FileBytes,int OffsetWidth,int IdWidth,ushort Alignment,ushort Subkey,byte[] CacheHeader,IReadOnlyList<AwbIndexEntry> Entries)
{
    public static AwbIndex Read(string path)=>ReadCore(path,false);
    // Only the explicit legacy inspector/repair may read overlapping payload ranges.
    internal static AwbIndex ReadLegacy(string path)=>ReadCore(path,true);
    private static AwbIndex ReadCore(string path,bool legacy)
    {
        MusicWorkspaceStore.RejectLinks(path);using var file=File.OpenRead(path);var h=new byte[16];file.ReadExactly(h);
        if(!h.AsSpan(0,4).SequenceEqual("AFS2"u8)||h[4]!=2||h[7]!=0||h[5] is not (2 or 4 or 8)||h[6] is not (2 or 4))throw new InvalidDataException("Unsupported AFS2 header.");
        int ow=h[5],iw=h[6],count=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8)));
        ushort align=BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(12)),subkey=BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(14));
        long indexBytes=checked(16L+(long)count*iw+((long)count+1)*ow);
        if(count>1_000_000||align==0||indexBytes>16*1024*1024||indexBytes>file.Length)throw new InvalidDataException("AFS2 index outside file or inspection bounds.");
        var core=new byte[(int)indexBytes];h.CopyTo(core,0);file.ReadExactly(core.AsSpan(16));
        ulong Read(int p,int width)=>width switch {2=>BinaryPrimitives.ReadUInt16LittleEndian(core.AsSpan(p)),4=>BinaryPrimitives.ReadUInt32LittleEndian(core.AsSpan(p)),8=>BinaryPrimitives.ReadUInt64LittleEndian(core.AsSpan(p)),_=>throw new InvalidDataException()};
        int pointers=16+count*iw;var entries=new List<AwbIndexEntry>();var ids=new HashSet<uint>();
        ulong previous=Read(pointers,ow);
        if(previous>(ulong)file.Length||(!legacy&&previous<(ulong)indexBytes))throw new InvalidDataException("AFS2 first offset outside file or before index end.");
        if(legacy)
        {
            // Raymonf's writer reserves the old count+1 offsets after writing the new ID array.
            long reserved=(long)previous-pointers;
            long oldCount=reserved/4-1;
            if(ow!=4||iw!=2||subkey!=0||previous>=(ulong)indexBytes||reserved<4||reserved%4!=0||oldCount<0||oldCount>=count)
                throw new InvalidDataException("AFS2 does not match the supported legacy editor append layout.");
        }
        for(int i=0;i<count;i++)
        {
            uint id=checked((uint)Read(16+i*iw,iw));ulong end=Read(pointers+(i+1)*ow,ow);
            if(!ids.Add(id)||end<previous||end>(ulong)file.Length||(legacy&&id!=(uint)i))throw new InvalidDataException("AFS2 duplicate ID or invalid offsets/legacy ID order.");
            ulong start=checked(previous+(align-previous%align)%align);
            if(start>end)throw new InvalidDataException("AFS2 entry alignment exceeds end offset.");
            entries.Add(new(id,checked((long)start),checked((long)(end-start))));previous=end;
        }
        // CRI caches commonly include two bytes after the index. Preserve actual bytes, not invented zeroes
        // when an aligned index is immediately followed by an HCA. Empty banks may have no trailing bytes.
        var cache=new byte[core.Length+2];core.CopyTo(cache,0);
        int trailing=(int)Math.Min(2,file.Length-file.Position);if(trailing>0)file.ReadExactly(cache.AsSpan(core.Length,trailing));
        return new(file.Length,ow,iw,align,subkey,cache,entries);
    }
}
