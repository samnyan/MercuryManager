using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
namespace MercuryManager.Server;

public sealed record AudioIssue(string Code,string Severity,string Table,int? Row,string Message,string? Repair=null,int? Port=null);
public sealed record AudioRepairOption(string Id,string Description,bool Recommended=false);
public sealed record AudioStreamInspection(int Port,string Name,bool Available,int? Entries,int CachedHeaderBytes,bool? HeaderMatches,string? FileBytes);
public sealed record AudioNameInspection(int Row,string Name,int CueIndex,bool LookupMatches);
public sealed record AudioWorkInspection(int Ports,int Bytes,int AlignmentLoss,int RequiredBytes,int RecommendedBytes);
public sealed record AudioIntegrityReport(string Revision,string Scope,bool Deep,bool ChecksPassed,List<AudioIssue> Issues,List<AudioRepairOption> Repairs,List<AudioStreamInspection> Streams,List<AudioNameInspection> CueNames,AudioWorkInspection? Work,int? NumSlots);

public static class AudioIntegrityInspection
{
    public static AudioIntegrityReport Read(string sheet,Func<string,string> resolveBank,bool deep=false,CancellationToken cancellation=default)
    {
        var issues=new List<AudioIssue>();var options=new List<AudioRepairOption>();var streams=new List<AudioStreamInspection>();var names=new List<AudioNameInspection>();AudioWorkInspection? work=null;int? numSlots=null;
        using var revision=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Identity(string path,bool whole=false)
        {
            MusicWorkspaceStore.RejectLinks(path);var f=new FileInfo(path);revision.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks));
            if(whole){using var s=File.OpenRead(path);revision.AppendData(SHA256.HashData(s));}
        }
        void Issue(string code,string message,string table="ACB",int? row=null,string? repair=null,string severity="error",int? port=null)=>issues.Add(new(code,severity,table,row,message,repair,port));
        void Option(string id,string description,bool recommended=false){if(!options.Any(o=>o.Id==id))options.Add(new(id,description,recommended));}
        try
        {
            Identity(sheet,!sheet.EndsWith(".awb",StringComparison.OrdinalIgnoreCase));foreach(string ext in new[]{".uexp",".ubulk"})if(File.Exists(Path.ChangeExtension(sheet,ext)))Identity(Path.ChangeExtension(sheet,ext),true);
            if(sheet.EndsWith(".awb",StringComparison.OrdinalIgnoreCase))
            {
                var bank=AwbIndex.Read(sheet);revision.AppendData(bank.CacheHeader);streams.Add(new(0,Path.GetFileNameWithoutExtension(sheet),true,bank.Entries.Count,0,null,bank.FileBytes.ToString()));
                if(deep)CheckHca(sheet,bank,new Dictionary<uint,List<(int Row,long Channels,long Rate,long Samples)>>(),Issue,cancellation);
                Issue("standalone-awb","AWB index checked without a paired ACB; slot names, cached indexes and Cue references are not checked.",severity:"warning");
            }
            else
            {
                var asset=new UAsset(sheet,EngineVersion.VER_UE4_19);var export=asset.Exports.OfType<NormalExport>().Single(e=>e.GetExportClassType().ToString()=="SoundAtomCueSheet");
                numSlots=export.Data.OfType<IntPropertyData>().SingleOrDefault(p=>p.Name.ToString()=="NumSlots")?.Value;
                var top=CriUtf.Read(CueSheetWriter.Extract(asset));
                if(top.Rows.Count!=1)throw new InvalidDataException("Expected a one-row ACB root.");
                var cueNames=CriUtf.Read(top.Blob(0,"CueNameTable"));var cues=CriUtf.Read(top.Blob(0,"CueTable"));var seen=new HashSet<string>(StringComparer.Ordinal);bool nameRepairable=true;
                for(int i=0;i<cueNames.Rows.Count;i++)
                {
                    cancellation.ThrowIfCancellationRequested();string name=cueNames.Text(i,"CueName");int index=checked((int)cueNames.Number(i,"CueIndex"));
                    if(!seen.Add(name)){Issue("cue-name-duplicate","Duplicate CueName: "+name,"CueName",i);nameRepairable=false;}
                    if(index>=cues.Rows.Count){Issue("cue-index-invalid","CueIndex outside CueTable.","CueName",i);nameRepairable=false;}
                    bool found=CueSheetMaintenance.LookupName(cueNames,"CueName",name)==i;names.Add(new(i,name,index,found));
                }
                bool sorted=Enumerable.Range(1,Math.Max(0,cueNames.Rows.Count-1)).All(i=>CueSheetMaintenance.CompareNames(cueNames.Text(i-1,"CueName"),cueNames.Text(i,"CueName"))<0);
                if(!sorted)Issue("cue-names-unsorted","CueName rows must be sorted by case-sensitive UTF-8 strcmp; native binary lookup can miss existing names.","CueName",repair:nameRepairable?"sort-cue-names":null);
                if(nameRepairable)Option("sort-cue-names","Sort CueName rows, preserving each CueIndex",!sorted);
                var ids=new HashSet<long>();long last=-1;bool idSorted=true;
                for(int i=0;i<cues.Rows.Count;i++){long value=cues.Number(i,"CueId");if(!ids.Add(value))Issue("cue-id-duplicate","Duplicate CueId.","Cue",i);if(value<last)idSorted=false;last=value;}
                if(!idSorted)Issue("cue-id-order-unverified","CueId rows are not ascending. Native numeric-ID lookup/reindexing is outside the verified repair subset; review the source tool's ID mapping. No automatic graph reindexing is offered.","Cue",severity:"warning");
                try{foreach(var cue in CriAudio.List(sheet)){if(cue.Error is not null)Issue("cue-graph-invalid",cue.Error,"CueName",cue.Index);else if(cue.WaveIds.Length>0&&cue.Tracks.Length==0)Issue("cue-graph-unverified","Cue topology is outside the sequence/track inspection subset.","CueName",cue.Index,severity:"warning");}}
                catch(Exception e) when(IsDataError(e)){Issue("cue-graph-invalid",e.Message,"Cue/Sequence/Track/Synth");}
                int count=CueSheetMaintenance.StreamCount(top);
                if(count>0)
                {
                    bool modern=true;try{CueSheetMaintenance.RequireModern(top);}catch(InvalidDataException e){modern=false;Issue("stream-layout-unsupported",e.Message);}
                    var slots=CriUtf.Read(top.Blob(0,"StreamAwbHash"));var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));
                    bool headerCount=headers.Rows.Count==count;if(!headerCount)Issue("stream-header-count","Stream slot and cached header counts differ; missing rows cannot be inferred.","StreamAwbHeader");
                    if(modern)
                    {
                        var bytes=top.Blob(0,"StreamAwbTocWork");int loss=(-top.BlobOffset(0,"StreamAwbTocWork"))&7,required=checked(count*CueSheetMaintenance.StreamWorkPerPort);
                        work=new(count,bytes.Length,loss,required,CueSheetMaintenance.RecommendedWork(count));
                        if(bytes.Length-loss<required)Issue("stream-work-overflow","StreamAwbTocWork is too small: CRI rejects the entire ACB with E2015051807.","StreamAwbTocWork",repair:"rebuild-stream-work");
                        if(bytes.Any(b=>b!=0))Issue("stream-work-not-zero","Serialized stream work contains nonzero runtime state.","StreamAwbTocWork",repair:"rebuild-stream-work");
                        Option("rebuild-stream-work","Regenerate zero-filled Win64 StreamAwbTocWork (portCount × 2064 + alignment margin)",issues.Any(i=>i.Repair=="rebuild-stream-work"));
                        if(numSlots!=count)Issue("num-slots-mismatch","UE NumSlots differs from the ACB stream slot count; this is a metadata inconsistency, not by itself proof of a native load failure.","USoundAtomCueSheet",repair:"sync-num-slots",severity:"warning");
                        Option("sync-num-slots","Synchronize UE NumSlots with ACB stream slot count",numSlots!=count);
                    }
                    bool slotSorted=Enumerable.Range(1,Math.Max(0,count-1)).All(i=>CueSheetMaintenance.CompareNames(slots.Text(i-1,"Name"),slots.Text(i,"Name"))<0);
                    bool distinct=Enumerable.Range(0,count).Select(i=>slots.Text(i,"Name")).Distinct(StringComparer.Ordinal).Count()==count;
                    bool primary=slots.Text(0,"Name")==top.Text(0,"Name");if(!primary)Issue("primary-slot-invalid","Primary AWB is attached to literal port 0, whose name must equal the sheet name.","StreamAwb",0);
                    bool canSort=modern&&headerCount&&distinct&&Enumerable.Range(0,count).Select(i=>slots.Text(i,"Name")).OrderBy(n=>n,Comparer<string>.Create(CueSheetMaintenance.CompareNames)).First()==top.Text(0,"Name");
                    if(!slotSorted)Issue("stream-slots-unsorted","Stream slot names are not sorted for native binary lookup.","StreamAwb",repair:canSort?"sort-stream-slots":null);
                    if(canSort)Option("sort-stream-slots","Sort stream slots and move headers/remap every streaming Waveform port",!slotSorted);
                    var references=new Dictionary<int,Dictionary<uint,List<(int Row,long Channels,long Rate,long Samples)>>>();
                    for(int i=0;i<waves.Rows.Count;i++)
                    {
                        if(waves.Number(i,"Streaming")==0)continue;int port=checked((int)waves.Number(i,"StreamAwbPortNo"));uint id=checked((uint)waves.Number(i,"StreamAwbId"));
                        if(port>=count){Issue("wave-port-invalid","Waveform stream port outside slot table.","Waveform",i);canSort=false;continue;}
                        if(id==65535&&waves.Number(i,"NumSamples")==0)continue; // detached deletion tombstone
                        if(!references.TryGetValue(port,out var byId))references[port]=byId=[];
                        if(!byId.TryGetValue(id,out var refs))byId[id]=refs=[];
                        refs.Add((i,waves.Number(i,"NumChannels"),waves.Number(i,"SamplingRate"),waves.Number(i,"NumSamples")));
                        int extension=checked((int)waves.Number(i,"ExtensionData"));
                        if(extension!=65535)
                        {
                            try{var ext=CriUtf.Read(top.Blob(0,"WaveformExtensionDataTable"));if(extension>=ext.Rows.Count)throw new InvalidDataException("Extension row outside table.");long start=ext.Number(extension,"LoopStart"),end=ext.Number(extension,"LoopEnd");if(start<0||end<=start||end>waves.Number(i,"NumSamples"))Issue("wave-loop-range","ACB loop interval is outside declared samples.","Waveform",i);}
                            catch(Exception e) when(IsDataError(e)){Issue("wave-extension-invalid",e.Message,"Waveform",i);}
                        }
                    }
                    if(!canSort)options.RemoveAll(o=>o.Id=="sort-stream-slots");
                    for(int port=0;port<count;port++)
                    {
                        cancellation.ThrowIfCancellationRequested();string name=slots.Text(port,"Name"),file=name+".awb";int cached=port<headers.Rows.Count?headers.Blob(port,"Header").Length:0;
                        try
                        {
                            AudioBanks.ValidateName(file);string path=resolveBank(file);Identity(path);var index=AwbIndex.Read(path);revision.AppendData(index.CacheHeader);
                            var refs=references.GetValueOrDefault(port)??[];var physical=index.Entries.Select(e=>e.Id).ToHashSet();bool missing=false;
                            foreach(var pair in refs)if(!physical.Contains(pair.Key)){missing=true;Issue("wave-entry-missing",$"AWB {file} has no entry {pair.Key}; supplying the right bank is required, not rewriting the expected cached header.","Waveform",pair.Value[0].Row,port:port);}
                            bool matches=headerCount&&MatchesHeader(headers.Blob(port,"Header"),index);
                            if(!matches)Issue("awb-header-mismatch","Cached AFS2 header differs from physical AWB; entry IDs/offsets may be stale.","StreamAwbHeader",port,modern&&headerCount&&!missing?$"refresh-awb-header:{port}":null,port:port);
                            if(modern&&headerCount&&!missing)
                            {
                                Option($"refresh-awb-header:{port}","Rebuild cached AFS2 index from "+file,!matches);
                                Option($"refresh-awb-hash:{port}","Recompute stored MD5 from "+file+" (runtime hash contract not guaranteed)");
                                if(index.Entries.All(e=>e.Id<65535))Option($"repack-awb:{port}","Repack "+file+" preserving every ID and payload; refresh ACB header/hash");
                            }
                            streams.Add(new(port,name,true,index.Entries.Count,cached,matches,index.FileBytes.ToString()));
                            if(deep){CheckHca(path,index,refs,Issue,cancellation);using var f=File.OpenRead(path);if(!MD5.HashData(f).SequenceEqual(slots.Blob(port,"Hash")))Issue("awb-hash-mismatch","Stored MD5 differs from bank bytes; runtime validation of this field is not fully recovered.","StreamAwb",port,!missing?$"refresh-awb-hash:{port}":null,"warning",port);}
                        }
                        catch(Exception e) when(IsDataError(e)){revision.AppendData(Encoding.UTF8.GetBytes(file+":unavailable"));Issue("awb-unavailable",file+": "+e.Message,"StreamAwb",port,port:port);streams.Add(new(port,name,false,null,cached,null,null));}
                    }
                }
                else Issue("memory-awb-scope","No external stream slots; embedded AWB waveform payload validation is outside this inspector.",severity:"warning");
            }
        }
        catch(Exception e) when(IsDataError(e)){Issue("asset-invalid",e.Message);}
        return new(Convert.ToHexString(revision.GetHashAndReset()),"UE4.19 Win64 static contracts; not CRI/game execution or proof of audible output",deep,!issues.Any(i=>i.Severity=="error"),issues,options,streams,names,work,numSlots);
    }
    public static bool IsDataError(Exception e)=>e is IOException or InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException or KeyNotFoundException or InvalidOperationException;
    public static bool MatchesHeader(byte[] cached,AwbIndex index)
    {
        int core=index.CacheHeader.Length-2;return cached.Length>=core&&cached.Length<=index.CacheHeader.Length&&cached.AsSpan().SequenceEqual(index.CacheHeader.AsSpan(0,cached.Length));
    }
    private static void CheckHca(string path,AwbIndex index,Dictionary<uint,List<(int Row,long Channels,long Rate,long Samples)>> refs,
        Action<string,string,string,int?,string?,string,int?> issue,CancellationToken cancellation)
    {
        using var bank=new AwbReader(File.OpenRead(path));
        foreach(var entry in index.Entries.Where(e=>refs.Count==0||refs.ContainsKey(e.Id)))
        {
            cancellation.ThrowIfCancellationRequested();int? row=refs.GetValueOrDefault(entry.Id)?.FirstOrDefault().Row;
            try
            {
                var wave=bank.Waves.Single(w=>(uint)w.WaveId==entry.Id);using var input=bank.GetWaveSubfileStream(wave);
                var magic=new byte[4];input.ReadExactly(magic);input.Position=0;
                if(!magic.Select(b=>(byte)(b&127)).SequenceEqual("HCA\0"u8.ToArray()))
                {issue("codec-unverified","Non-HCA entry: this inspector does not claim codec/PCM validation for this payload.","AWB",row,null,"warning",null);continue;}
                var info=new HcaDecoder(input,0,index.Subkey).HcaInfo;
                if(info.HeaderSize<24||info.HeaderSize>entry.Length)throw new InvalidDataException("HCA header outside AWB entry.");
                input.Position=0;var header=new byte[info.HeaderSize];input.ReadExactly(header);
                if(Crc(header)!=0)throw new InvalidDataException("HCA header CRC mismatch.");
                if(refs.TryGetValue(entry.Id,out var rows))foreach(var w in rows)
                {
                    if(info.ChannelCount!=w.Channels||info.SamplingRate!=w.Rate)issue("hca-format-mismatch","HCA channel count/sample rate differs from Waveform.","Waveform",w.Row,null,"error",null);
                    if(info.SampleCount!=w.Samples)issue("hca-samples-mismatch","HCA valid sample count differs from Waveform; review loop/encoder delay before changing metadata.","Waveform",w.Row,null,"warning",null);
                }
                int frames=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16))),frameBytes=0;
                for(int pos=24;pos+6<=header.Length-2;)
                {
                    string tag=new string(header.AsSpan(pos,4).ToArray().Select(b=>(char)(b&127)).ToArray()).TrimEnd('\0');
                    if(tag is "comp" or "dec")frameBytes=BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(pos+4));
                    int size=tag switch{"comp"=>16,"dec"=>12,"vbr"=>8,"ath" or "ciph"=>6,"loop"=>16,"rva"=>8,"comm"=>5+header[pos+4],_=>header.Length};pos=checked(pos+size);
                }
                if(frameBytes==0){issue("hca-crc-unverified","Variable/unknown HCA frame layout: frame CRC checking skipped.","AWB",row,null,"warning",null);continue;}
                if((long)info.HeaderSize+(long)frames*frameBytes!=entry.Length)throw new InvalidDataException("HCA frame count/size differs from AWB entry length.");
                var frame=new byte[frameBytes];for(int i=0;i<frames;i++){cancellation.ThrowIfCancellationRequested();input.ReadExactly(frame);if(Crc(frame)!=0)throw new InvalidDataException("HCA frame CRC mismatch at frame "+i);}
                if(info.EncryptionEnabled){issue("hca-encrypted-unverified","Encrypted HCA: no key was guessed; PCM validation skipped.","AWB",row,null,"warning",null);continue;}
                input.Position=0;using var pcm=new HcaWaveStream(input,0,index.Subkey);var buffer=new byte[32768];bool nonzero=false;int n;
                while((n=pcm.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();if(buffer.AsSpan(0,n).ContainsAnyExcept((byte)0))nonzero=true;}
                if(!nonzero)issue("hca-silent","Decoded PCM is all zero; silence may be intentional.","AWB",row,null,"warning",null);
            }
            catch(Exception e) when(IsDataError(e)){issue("hca-invalid",$"Entry {entry.Id}: {e.Message}","AWB",row,null,"error",null);}
        }
    }
    private static readonly ushort[] CrcTable=Enumerable.Range(0,256).Select(i=>{int c=i<<8;for(int j=0;j<8;j++)c=((c<<1)^((c&0x8000)!=0?0x8005:0))&0xffff;return (ushort)c;}).ToArray();
    private static ushort Crc(ReadOnlySpan<byte> bytes){ushort c=0;foreach(byte b in bytes)c=(ushort)((c<<8)^CrcTable[(c>>8)^b]);return c;}
}
