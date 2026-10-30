using System.Security.Cryptography;
using System.Text;

namespace MercuryManager.Server;

// UE4 legacy Asset Registry v5. FName is an index/number pair into the
// trailing name table; each name has its own two serialized 16-bit hashes.
public static class AssetRegistryEditor
{
    private static readonly byte[] GuidBytes = [0xe7,0x9e,0x7f,0x71,0x3a,0x49,0xb0,0xe9,0x32,0x91,0xb3,0x88,0x07,0x81,0x38,0x1b];
    public sealed record Tag(string Name, string Value);
    public sealed record Entry(int Index, string ObjectPath, string PackagePath, string AssetClass, string PackageName, string AssetName, Tag[] Tags, int[] ChunkIds, uint PackageFlags);
    public sealed record Document(string Revision, Entry[] Entries);
    public sealed record Edit(int Index, string Name, string Value);
    public sealed record Request(string Revision, Edit[]? Edits = null, Entry[]? Entries = null);
    private sealed record Span(int Start, int End, int Index, string Name);
    private sealed record NameRef(string Text, int Index, int Number);
    private sealed record RawEntry(Entry Entry, NameRef[] Fields, NameRef[] Tags, byte[] Bytes);
    private sealed record Parsed(Document Document, List<Span> Values, long NameOffset, string[] Names, RawEntry[] RawEntries);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static int Count(BinaryReader reader, int max, string what)
    {
        int n=reader.ReadInt32(); if(n<0||n>max) throw new InvalidDataException($"Invalid {what} count."); return n;
    }
    private static string ReadString(BinaryReader reader)
    {
        int length=reader.ReadInt32(); if(length==0) return "";
        if(length==int.MinValue || Math.Abs((long)length)>1_000_001) throw new InvalidDataException("Invalid FString length.");
        int bytes=checked((int)(Math.Abs((long)length)*(length<0?2:1)));
        var raw=reader.ReadBytes(bytes); if(raw.Length!=bytes) throw new EndOfStreamException();
        if(length<0)
        {
            if(raw[^1]!=0||raw[^2]!=0) throw new InvalidDataException("Unterminated FString.");
            return new UnicodeEncoding(false,false,true).GetString(raw,0,raw.Length-2);
        }
        if(raw[^1]!=0) throw new InvalidDataException("Unterminated FString.");
        return Encoding.Latin1.GetString(raw,0,raw.Length-1);
    }
    private static NameRef Name(BinaryReader reader, string[] names)
    {
        int index=reader.ReadInt32(), number=reader.ReadInt32();
        if((uint)index >= names.Length || number<0) throw new InvalidDataException("Invalid FName reference.");
        return new(number==0 ? names[index] : $"{names[index]}_{number-1}",index,number);
    }
    private static Parsed Parse(byte[] bytes)
    {
        if(bytes.Length<32 || !bytes.AsSpan(0,16).SequenceEqual(GuidBytes) || BitConverter.ToInt32(bytes,16)!=5)
            throw new NotSupportedException("Only Asset Registry version 5 is supported.");
        using var stream=new MemoryStream(bytes,false); using var reader=new BinaryReader(stream,Encoding.UTF8,true);
        stream.Position=20; long nameOffset=reader.ReadInt64();
        if(nameOffset<32 || nameOffset>bytes.Length-4) throw new InvalidDataException("Invalid name table offset.");
        stream.Position=nameOffset; var names=new string[Count(reader,1_000_000,"name")];
        for(int i=0;i<names.Length;i++) { names[i]=ReadString(reader); reader.ReadUInt16(); reader.ReadUInt16(); }
        if(stream.Position!=stream.Length) throw new NotSupportedException("Unexpected data after name table.");
        stream.Position=28; int count=Count(reader,1_000_000,"asset");
        var entries=new List<Entry>(count); var spans=new List<Span>(); var rawEntries=new List<RawEntry>(count);
        for(int i=0;i<count;i++)
        {
            int entryStart=checked((int)stream.Position);
            var fields=Enumerable.Range(0,5).Select(_=>Name(reader,names)).ToArray();
            int n=Count(reader,100_000,"tag"); var tags=new List<Tag>(n); var tagRefs=new List<NameRef>(n); var unique=new HashSet<string>(StringComparer.Ordinal);
            for(int j=0;j<n;j++)
            {
                var reference=Name(reader,names); string name=reference.Text; tagRefs.Add(reference); int start=checked((int)stream.Position); string value=ReadString(reader); int end=checked((int)stream.Position);
                if(!unique.Add(name)) throw new InvalidDataException("Duplicate tag names are not supported.");
                tags.Add(new(name,value)); spans.Add(new(start,end,i,name));
            }
            int chunks=Count(reader,100_000,"chunk"); var ids=new int[chunks]; for(int j=0;j<chunks;j++) ids[j]=reader.ReadInt32();
            uint flags=reader.ReadUInt32();
            if(stream.Position>nameOffset) throw new InvalidDataException("Asset data overlaps name table.");
            var entry=new Entry(i,fields[0].Text,fields[1].Text,fields[2].Text,fields[3].Text,fields[4].Text,tags.ToArray(),ids,flags);
            entries.Add(entry); rawEntries.Add(new(entry,fields,tagRefs.ToArray(),bytes[entryStart..checked((int)stream.Position)]));
        }
        if(reader.ReadInt32()!=0) throw new NotSupportedException("Editing registries with dependency nodes is not yet supported.");
        if(reader.ReadInt32()!=0) throw new NotSupportedException("Editing registries with package data is not yet supported.");
        if(stream.Position!=nameOffset) throw new NotSupportedException("Unexpected data between assets and name table.");
        return new(new(Hash(bytes),entries.ToArray()),spans,nameOffset,names,rawEntries.ToArray());
    }
    public static Document Read(byte[] bytes) => Parse(bytes).Document;
    public static string ContentHash(byte[] bytes) => Hash(bytes);
    private static byte[] FString(string value)
    {
        if(value.Contains('\0') || value.Length>1_000_000) throw new ArgumentException("Invalid tag value.");
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream,Encoding.UTF8,true);
        if(value.Length==0) w.Write(0);
        else if(value.All(c=>c is > '\0' and <= '\x7f')) { var b=Encoding.ASCII.GetBytes(value); w.Write(b.Length+1); w.Write(b); w.Write((byte)0); }
        else { var b=Encoding.Unicode.GetBytes(value); w.Write(-(value.Length+1)); w.Write(b); w.Write((ushort)0); }
        return stream.ToArray();
    }
    private static bool SameContent(Entry a,Entry b) => a.ObjectPath==b.ObjectPath && a.PackagePath==b.PackagePath && a.AssetClass==b.AssetClass && a.PackageName==b.PackageName && a.AssetName==b.AssetName && a.Tags.SequenceEqual(b.Tags) && a.ChunkIds.SequenceEqual(b.ChunkIds) && a.PackageFlags==b.PackageFlags;
    private static byte[] Rebuild(byte[] source, Parsed parsed, Entry[] entries)
    {
        if (entries.Length > 1_000_000) throw new ArgumentException("Too many assets.");
        var names = parsed.Names.ToList();
        var indices = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for (int i=0;i<names.Count;i++) indices.TryAdd(names[i],i);
        var added = new List<string>();
        void WriteName(BinaryWriter w, string text, NameRef? original = null)
        {
            if(original is not null && text == original.Text) { w.Write(original.Index); w.Write(original.Number); return; }
            if (string.IsNullOrEmpty(text) || text.Length > 1023 || text.Any(c => c < 32 || c > 126))
                throw new ArgumentException("FName must be non-empty printable ASCII (max 1023 characters).");
            string baseName = text; int number = 0;
            int underscore = text.LastIndexOf('_');
            if (!indices.ContainsKey(text) && underscore > 0 && underscore < text.Length-1)
            {
                string suffix = text[(underscore+1)..];
                if ((suffix.Length == 1 || suffix[0] != '0') && suffix.All(char.IsAsciiDigit)
                    && int.TryParse(suffix, out int value) && value < int.MaxValue)
                { baseName=text[..underscore]; number=value+1; }
            }
            if (!indices.TryGetValue(baseName,out int index))
            { index=names.Count; indices.Add(baseName,index); names.Add(baseName); added.Add(baseName); }
            w.Write(index); w.Write(number);
        }
        using var output=new MemoryStream(); using var w=new BinaryWriter(output,Encoding.UTF8,true);
        w.Write(source,0,20); w.Write((long)0); w.Write(entries.Length);
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var usedIndices=new HashSet<int>();
        foreach(var entry in entries)
        {
            if(entry is null || string.IsNullOrEmpty(entry.ObjectPath) || !paths.Add(entry.ObjectPath)) throw new ArgumentException("Duplicate or missing object path.");
            if(entry.ObjectPath != $"{entry.PackageName}.{entry.AssetName}") throw new ArgumentException("UE4 derives ObjectPath as PackageName.AssetName; keep these fields synchronized.");
            if(entry.Tags is null || entry.ChunkIds is null || entry.Tags.Length>100_000 || entry.ChunkIds.Length>100_000) throw new ArgumentException("Invalid tags or chunks.");
            RawEntry? raw=null;
            if(entry.Index>=0)
            {
                if(entry.Index>=parsed.RawEntries.Length || !usedIndices.Add(entry.Index)) throw new ArgumentException("Invalid or duplicate entry index.");
                raw=parsed.RawEntries[entry.Index];
                if(entry.Index!=raw.Entry.Index) throw new ArgumentException("Entry index does not match the source row.");
            }
            var tagNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var tag in entry.Tags)
                if(tag is null || string.IsNullOrEmpty(tag.Name) || tag.Value is null || !tagNames.Add(tag.Name)) throw new ArgumentException("Duplicate or missing tag name/value.");
            if(raw is not null && SameContent(entry,raw.Entry)) { w.Write(raw.Bytes); continue; }
            WriteName(w,entry.ObjectPath,raw?.Fields[0]); WriteName(w,entry.PackagePath,raw?.Fields[1]); WriteName(w,entry.AssetClass,raw?.Fields[2]); WriteName(w,entry.PackageName,raw?.Fields[3]); WriteName(w,entry.AssetName,raw?.Fields[4]);
            w.Write(entry.Tags.Length);
            var oldTags=raw?.Tags.ToDictionary(t=>t.Text,StringComparer.Ordinal);
            foreach(var tag in entry.Tags)
            {
                NameRef? prior=null; oldTags?.TryGetValue(tag.Name,out prior);
                WriteName(w,tag.Name,prior); w.Write(FString(tag.Value));
            }
            w.Write(entry.ChunkIds.Length); foreach(var chunk in entry.ChunkIds) w.Write(chunk); w.Write(entry.PackageFlags);
        }
        w.Write(0); w.Write(0); long nameOffset=output.Position;
        w.Write(names.Count);
        // Existing name bytes and their hashes remain untouched.
        int originalTableStart=checked((int)parsed.NameOffset)+4;
        w.Write(source,originalTableStart,source.Length-originalTableStart);
        foreach(string name in added)
        {
            w.Write(FString(name));
            uint insensitive=0, crc=0xffffffff;
            foreach(char c in name)
            {
                byte upper=(byte)(c is >= 'a' and <= 'z' ? c-32 : c);
                uint table=(uint)((insensitive ^ upper)&255)<<24;
                for(int i=0;i<8;i++) table=(table<<1)^((table&0x80000000)!=0 ? 0x04c11db7u : 0);
                insensitive=(insensitive>>8)^table;
                uint ch=c;
                for(int j=0;j<4;j++)
                {
                    crc ^= ch & 255; ch >>= 8;
                    for(int i=0;i<8;i++) crc=(crc>>1)^((crc&1)!=0 ? 0xedb88320u : 0);
                }
            }
            w.Write((ushort)insensitive); w.Write((ushort)~crc);
        }
        output.Position=20; w.Write(nameOffset); var result=output.ToArray();
        var check=Read(result);
        if(check.Entries.Length!=entries.Length) throw new InvalidDataException("Registry verification failed.");
        for(int i=0;i<entries.Length;i++)
        {
            var a=entries[i]; var b=check.Entries[i];
            if(a.ObjectPath!=b.ObjectPath || a.PackagePath!=b.PackagePath || a.AssetClass!=b.AssetClass || a.PackageName!=b.PackageName || a.AssetName!=b.AssetName || !a.Tags.SequenceEqual(b.Tags) || !a.ChunkIds.SequenceEqual(b.ChunkIds) || a.PackageFlags!=b.PackageFlags)
                throw new InvalidDataException("Registry entry verification failed; a name may differ only by case.");
        }
        return result;
    }
    public static byte[] Apply(byte[] source, Request request)
    {
        var parsed=Parse(source); if(request.Revision!=parsed.Document.Revision) throw new InvalidOperationException("Registry changed; reload before saving.");
        if(request.Entries is not null)
        {
            if(request.Edits is { Length: > 0 }) throw new ArgumentException("Choose entries or edits, not both.");
            return Rebuild(source,parsed,request.Entries);
        }
        var edits=new Dictionary<(int,string),string>();
        foreach(var edit in request.Edits ?? throw new ArgumentException("Missing edits."))
            if(!edits.TryAdd((edit.Index,edit.Name),edit.Value)) throw new ArgumentException("Duplicate edit.");
        var spans=parsed.Values.Where(s=>edits.ContainsKey((s.Index,s.Name))).OrderBy(s=>s.Start).ToArray();
        if(spans.Length!=edits.Count) throw new ArgumentException("Unknown asset or tag.");
        using var output=new MemoryStream(); int pos=0;
        foreach(var span in spans) { output.Write(source.AsSpan(pos,span.Start-pos)); output.Write(FString(edits[(span.Index,span.Name)])); pos=span.End; }
        output.Write(source.AsSpan(pos)); var result=output.ToArray();
        long offset=parsed.NameOffset+(result.Length-source.Length); BitConverter.GetBytes(offset).CopyTo(result,20);
        var actual=Parse(result).Document;
        if(actual.Entries.Length!=parsed.Document.Entries.Length) throw new InvalidDataException("Verification failed.");
        foreach(var entry in actual.Entries) foreach(var tag in entry.Tags)
        {
            var original=parsed.Document.Entries[entry.Index].Tags.First(t=>t.Name==tag.Name);
            var expected=edits.GetValueOrDefault((entry.Index,tag.Name),original.Value);
            if(tag.Value!=expected) throw new InvalidDataException("Tag verification failed.");
        }
        return result;
    }
}
