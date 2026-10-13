using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;

public sealed record AwbLegacyDamage(uint Id,long OverwrittenBytes,long SurvivingBytes);
public sealed record AwbLegacyLayout(AwbIndex Index,long IndexBytes,long FirstOffset,int OriginalCount,IReadOnlyList<AwbLegacyDamage> Damage)
{
    // A matching surviving suffix is required; a completely overwritten entry cannot be identified safely.
    public bool Repairable=>Index.Entries.All(e=>e.Id<65535)&&Damage.All(d=>d.SurvivingBytes>=32);
    public string Description=> $"Legacy editor append layout: first offset {FirstOffset}, index end {IndexBytes}, inferred original entries {OriginalCount}. "+
        (Damage.Count==0?"Aligned payloads do not overlap the index; repacking can preserve all bytes.":
        "Index overwrote payload prefixes: "+string.Join(", ",Damage.Select(d=>$"ID {d.Id}: {d.OverwrittenBytes} bytes"))+". Recovery requires an explicitly supplied original/backup AWB with matching IDs, lengths and surviving bytes. "+
        (Repairable?"":"Recovery is unavailable: fewer than 32 surviving bytes remain to identify an entry safely."));
}
public sealed record AwbRecoveredFormat(uint Id,int Channels,int Rate,long Samples);

// This is not a permissive replacement for AwbIndex.Read. Only explicit legacy repair uses its ranges.
public static class AwbLegacyRepair
{
    public static AwbLegacyLayout Inspect(string path)
    {
        var index=AwbIndex.ReadLegacy(path);int count=index.Entries.Count;
        long end=16L+count*index.IdWidth+((long)count+1)*index.OffsetWidth;
        long first=BinaryPrimitives.ReadUInt32LittleEndian(index.CacheHeader.AsSpan(16+count*index.IdWidth));
        int original=checked((int)((first-16-count*2)/4-1));
        var damage=index.Entries.Select(e=>new AwbLegacyDamage(e.Id,Math.Clamp(end-e.Offset,0,e.Length),Math.Max(0,e.Offset+e.Length-Math.Max(end,e.Offset))))
            .Where(d=>d.OverwrittenBytes>0).ToArray();
        return new(index,end,first,original,damage);
    }

    public static IReadOnlyList<AwbRecoveredFormat> Repack(string source,string destination,string? recoveryBank=null,CancellationToken cancellation=default)
    {
        var layout=Inspect(source);
        if(!layout.Repairable)throw new InvalidDataException("Legacy AWB recovery cannot safely identify a fully/nearly overwritten entry; supply a complete corrected bank instead.");
        if(Path.GetFullPath(source)==Path.GetFullPath(destination))throw new ArgumentException("Legacy repair must write a separate staged file.");
        var recovery=new Dictionary<uint,AwbIndexEntry>();var formats=new List<AwbRecoveredFormat>();
        if(layout.Damage.Count>0)
        {
            if(string.IsNullOrWhiteSpace(recoveryBank)||!Path.IsPathFullyQualified(recoveryBank)||!recoveryBank.EndsWith(".awb",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An absolute server path to the original/backup AWB is required to recover overwritten audio.");
            recoveryBank=Path.GetFullPath(recoveryBank);
            if(recoveryBank==Path.GetFullPath(source)||recoveryBank==Path.GetFullPath(destination))throw new ArgumentException("Recovery AWB must be separate from source/output.");
            var donor=AwbIndex.Read(recoveryBank);
            if(donor.Subkey!=layout.Index.Subkey)throw new InvalidDataException("Recovery AWB subkey differs from damaged bank.");
            foreach(var damage in layout.Damage)
            {
                cancellation.ThrowIfCancellationRequested();var damaged=layout.Index.Entries.Single(e=>e.Id==damage.Id);
                var entry=donor.Entries.SingleOrDefault(e=>e.Id==damage.Id);
                if(entry is null||entry.Length!=damaged.Length)throw new InvalidDataException($"Recovery AWB entry {damage.Id} is missing or its length differs.");
                if(!HashRange(source,damaged.Offset+damage.OverwrittenBytes,damage.SurvivingBytes,cancellation)
                    .SequenceEqual(HashRange(recoveryBank,entry.Offset+damage.OverwrittenBytes,damage.SurvivingBytes,cancellation)))
                    throw new InvalidDataException($"Recovery AWB entry {damage.Id} does not match surviving audio; refusing to replace it with unrelated/original audio.");
                formats.Add(ReadHcaFormat(recoveryBank,entry,donor.Subkey));recovery[damage.Id]=entry;
            }
        }
        cancellation.ThrowIfCancellationRequested();MusicWorkspaceStore.RejectLinks(destination);
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))
            AwbWriter.Write(output,layout.Index.Entries.Select(e=>new AwbWriter.Entry(checked((ushort)e.Id),e.Length,()=>
            {
                cancellation.ThrowIfCancellationRequested();return recovery.TryGetValue(e.Id,out var replacement)?OpenAt(recoveryBank!,replacement.Offset):OpenAt(source,e.Offset);
            })).ToArray(),layout.Index.Alignment,layout.Index.Subkey,(_,_)=>cancellation.ThrowIfCancellationRequested());
        var written=AwbIndex.Read(destination);var originals=layout.Index.Entries.ToDictionary(e=>e.Id);
        if(!written.Entries.Select(e=>e.Id).SequenceEqual(layout.Index.Entries.Select(e=>e.Id)))throw new InvalidDataException("Legacy repair changed entry IDs/order.");
        foreach(var entry in written.Entries)
        {
            var expected=recovery.GetValueOrDefault(entry.Id)??originals[entry.Id];
            string path=recovery.ContainsKey(entry.Id)?recoveryBank!:source;
            if(entry.Length!=expected.Length||!HashRange(path,expected.Offset,expected.Length,cancellation).SequenceEqual(HashRange(destination,entry.Offset,entry.Length,cancellation)))
                throw new InvalidDataException($"Legacy repair payload verification failed for ID {entry.Id}.");
        }
        return formats;
    }

    private static FileStream OpenAt(string path,long offset)
    {
        MusicWorkspaceStore.RejectLinks(path);var stream=File.OpenRead(path);
        try{stream.Position=offset;return stream;}catch{stream.Dispose();throw;}
    }
    private static byte[] HashRange(string path,long offset,long length,CancellationToken cancellation)
    {
        using var file=OpenAt(path,offset);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];
        while(length>0){cancellation.ThrowIfCancellationRequested();int n=file.Read(buffer,0,(int)Math.Min(length,buffer.Length));if(n==0)throw new EndOfStreamException("Truncated AWB payload.");hash.AppendData(buffer,0,n);length-=n;}
        return hash.GetHashAndReset();
    }
    private static AwbRecoveredFormat ReadHcaFormat(string path,AwbIndexEntry entry,ushort subkey)
    {
        if(entry.Length<24)throw new InvalidDataException("Recovery entry is not a complete HCA header.");
        using var file=OpenAt(path,entry.Offset);var prefix=new byte[8];file.ReadExactly(prefix);
        if(!prefix.AsSpan(0,4).ToArray().Select(b=>(byte)(b&127)).SequenceEqual("HCA\0"u8.ToArray()))throw new InvalidDataException("Recovery entry is not HCA.");
        int size=BinaryPrimitives.ReadUInt16BigEndian(prefix.AsSpan(6));if(size<24||size>entry.Length)throw new InvalidDataException("Recovery HCA header exceeds entry.");
        var header=new byte[size];prefix.CopyTo(header,0);file.ReadExactly(header.AsSpan(8));
        ushort crc=0;foreach(byte b in header){int c=crc^(b<<8);for(int i=0;i<8;i++)c=((c<<1)^((c&0x8000)!=0?0x8005:0))&0xffff;crc=(ushort)c;}
        if(crc!=0)throw new InvalidDataException("Recovery HCA header CRC mismatch.");
        using var input=new MemoryStream(header);var info=new HcaDecoder(input,0,subkey).HcaInfo;
        return new(entry.Id,info.ChannelCount,info.SamplingRate,info.SampleCount);
    }
}
