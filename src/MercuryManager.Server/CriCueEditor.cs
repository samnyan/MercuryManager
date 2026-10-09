using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
// Clone only the verified simple two-track sequence topology; preserve all old indexes.
public static class CriCueEditor
{
    [ThreadStatic] internal static AudioBatchContext? Batch;
    public static void Add(string sheet,string bank,int templateId,int cueId,string name,string speaker,string headphone,string output,Dictionary<int,string>? trackFiles=null)
    {
        if(cueId<0||string.IsNullOrWhiteSpace(name)||name.Length>128||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Invalid Cue ID or name.");
        var cues=CriAudio.List(sheet);if(cues.Any(c=>c.CueId==cueId||c.Name==name))throw new InvalidDataException("Cue ID/name already exists.");
        var donor=cues.SingleOrDefault(c=>c.CueId==templateId)??throw new InvalidDataException($"Template Cue {templateId} is missing (possibly deleted by an earlier action).");if(donor.Error!=null||donor.Tracks.Length==0||donor.Tracks.Select(t=>t.TrackIndex).Distinct().Count()!=donor.Tracks.Length)throw new InvalidDataException("Select a simple Speaker/Headphone template Cue.");
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));
        using var acb=new AcbReader(new MemoryStream(top.Write()));int port=acb.AtomCueSheetData["StreamAwb"].FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));if(port<0)throw new InvalidDataException("Unknown bank.");
        var tables=new Dictionary<string,CriUtf>();CriUtf T(string name)=>tables.TryGetValue(name,out var t)?t:tables[name]=CriUtf.Read(top.Blob(0,name));
        var cue=T("CueTable");int donorCue=checked((int)T("CueNameTable").Number(donor.Index,"CueIndex"));if(cue.Number(donorCue,"CueId")!=templateId)throw new InvalidDataException("Template Cue reference mismatch.");if(cue.Number(donorCue,"ReferenceType")!=3)throw new InvalidDataException("Only sequence Cues are supported.");
        var seq=T("SequenceTable");int donorSeq=(int)cue.Number(donorCue,"ReferenceIndex");if(seq.Number(donorSeq,"NumTracks")!=donor.Tracks.Length||seq.Number(donorSeq,"Type")!=0)throw new InvalidDataException("Only simultaneous two-track sequences are supported.");
        using var awb=new AwbReader(File.OpenRead(bank));var used=Batch is null?awb.Waves.Select(w=>w.WaveId).ToHashSet():Batch.Entries(bank,awb).Select(w=>(int)w.Id).ToHashSet();ushort Allocate(){int value=Enumerable.Range(0,65535).First(i=>!used.Contains(i));used.Add(value);return (ushort)value;}
        var added=new List<AwbWriter.Entry>();var tracks=new List<int>();long duration=0;
        foreach(var track in donor.Tracks)
        {
            string source=trackFiles is not null?(trackFiles.TryGetValue(track.TrackIndex,out var file)?file:throw new InvalidDataException("Missing track audio.")):track.Route=="BGM_SPEAKER"?speaker:headphone;HcaInfo info;using(var f=File.OpenRead(source))info=new HcaDecoder(f,0,awb.Subkey).HcaInfo;
            using(var pcm=new HcaWaveStream(File.OpenRead(source),0,awb.Subkey)){var b=new byte[32768];while(pcm.Read(b,0,b.Length)>0){}}
            ushort id=Allocate();added.Add(new(id,new FileInfo(source).Length,()=>File.OpenRead(source)));duration=Math.Max(duration,((long)info.SampleCount*1000+info.SamplingRate-1)/info.SamplingRate);
            var wave=T("WaveformTable");int wi=wave.CloneRow(track.WaveformIndex);wave.SetNumber(wi,"ExtensionData",65535);
            wave.SetNumber(wi,"StreamAwbPortNo",port);wave.SetNumber(wi,"StreamAwbId",id);wave.SetNumber(wi,"NumChannels",info.ChannelCount);wave.SetNumber(wi,"SamplingRate",info.SamplingRate);wave.SetNumber(wi,"NumSamples",info.SampleCount);wave.SetNumber(wi,"LoopFlag",info.LoopEnabled?2:0);if(info.LoopEnabled)WaveformExtensions.Apply(top,wave,new HashSet<int>{wi},[new(wi,info.LoopStartSample,info.LoopEndSample,2)],info.SampleCount);
            var synth=T("SynthTable");var events=T("TrackEventTable");var tr=T("TrackTable");int oldEvent=(int)tr.Number(track.TrackIndex,"EventIndex");var cmd=events.Blob(oldEvent,"Command");if(cmd.Length!=10||BinaryPrimitives.ReadUInt16BigEndian(cmd)!=2000||cmd[2]!=4||BinaryPrimitives.ReadUInt16BigEndian(cmd.AsSpan(3))!=2)throw new InvalidDataException("Unsupported template TrackEvent.");int oldSynth=BinaryPrimitives.ReadUInt16BigEndian(cmd.AsSpan(5));var refs=synth.Blob(oldSynth,"ReferenceItems");if(refs.Length!=4||BinaryPrimitives.ReadUInt16BigEndian(refs)!=1)throw new InvalidDataException("Unsupported template Synth.");
            int si=synth.CloneRow(oldSynth);synth.Promote("ReferenceItems");BinaryPrimitives.WriteUInt16BigEndian(refs.AsSpan(2),checked((ushort)wi));synth.SetBlob(si,"ReferenceItems",refs);
            int ei=events.CloneRow(oldEvent);events.Promote("Command");BinaryPrimitives.WriteUInt16BigEndian(cmd.AsSpan(5),checked((ushort)si));events.SetBlob(ei,"Command",cmd);
            int ti=tr.CloneRow(track.TrackIndex);tr.SetNumber(ti,"EventIndex",ei);tracks.Add(ti);
        }
        int sequence=seq.CloneRow(donorSeq);var indexes=new byte[tracks.Count*2];for(int i=0;i<tracks.Count;i++)BinaryPrimitives.WriteUInt16BigEndian(indexes.AsSpan(i*2),checked((ushort)tracks[i]));seq.Promote("TrackIndex");seq.SetBlob(sequence,"TrackIndex",indexes);
        int newCue=cue.CloneRow(donorCue);cue.SetNumber(newCue,"CueId",cueId);cue.SetNumber(newCue,"ReferenceIndex",sequence);cue.SetNumber(newCue,"Length",duration);
        var names=T("CueNameTable");int ni=names.CloneRow(donor.Index);names.SetNumber(ni,"CueIndex",newCue);names.SetText(ni,"CueName",name);
        foreach(var pair in tables)top.SetBlob(0,pair.Key,pair.Value.Write());
        var entries=(Batch is null?awb.Waves.Select(w=>new AwbWriter.Entry((ushort)w.WaveId,w.Length,()=>awb.GetWaveSubfileStream(w))):Batch.Entries(bank,awb)).Concat(added).ToArray();Publish(sheet,bank,output,top,awb,entries,port);
        var check=CriAudio.List(Path.Combine(output,Path.GetFileName(sheet)));var created=check.Single(c=>c.CueId==cueId);if(created.Error!=null||created.Tracks.Length!=donor.Tracks.Length||created.Name!=name)throw new InvalidDataException("New Cue verification failed.");
    }
    public static void Delete(string sheet,string bank,int cueId,string output)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));using var acb=new AcbReader(new MemoryStream(top.Write()));int port=acb.AtomCueSheetData["StreamAwb"].FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));if(port<0)throw new InvalidDataException("Unknown bank.");
        var cues=CriAudio.List(sheet);if(cues.Any(c=>c.Error!=null||c.WaveIds.Length>0&&c.Tracks.Length==0))throw new InvalidDataException("Cannot prove shared-wave references for every Cue; deletion refused.");var target=cues.SingleOrDefault(c=>c.CueId==cueId)??throw new InvalidDataException($"Cue {cueId} is missing (already deleted or stale operation list).");if(target.Error!=null||target.Tracks.Length==0||target.Tracks.Any(t=>t.Port!=port))throw new InvalidDataException("Delete requires a Cue wholly contained in the selected bank.");
        var cue=CriUtf.Read(top.Blob(0,"CueTable"));var names=CriUtf.Read(top.Blob(0,"CueNameTable"));int index=checked((int)names.Number(target.Index,"CueIndex"));if(cue.Number(index,"CueId")!=cueId)throw new InvalidDataException("Cue name reference mismatch.");
        // Removing a Cue row would shift references: keep its slot as an inert tombstone.
        var sequence=CriUtf.Read(top.Blob(0,"SequenceTable"));if(sequence.Rows.Count==0)throw new InvalidDataException("Missing sequence schema.");int empty=sequence.CloneRow(0);sequence.SetNumber(empty,"NumTracks",0);sequence.Promote("TrackIndex");sequence.SetBlob(empty,"TrackIndex",[]);cue.SetNumber(index,"ReferenceType",3);cue.SetNumber(index,"ReferenceIndex",empty);cue.SetNumber(index,"Length",0);
        names.Rows.RemoveAll(r=>BinaryPrimitives.ReadUInt16BigEndian(r["CueIndex"])==index);
        top.SetBlob(0,"CueTable",cue.Write());top.SetBlob(0,"CueNameTable",names.Write());top.SetBlob(0,"SequenceTable",sequence.Write());
        var shared=cues.Where(c=>c.CueId!=cueId).SelectMany(c=>c.Tracks).Where(t=>t.Port==port).Select(t=>t.WaveId).ToHashSet();var remove=target.Tracks.Select(t=>t.WaveId).Where(id=>!shared.Contains(id)).ToHashSet();
        // Preserve unreferenced graph rows; detach deleted waveforms instead of reindexing unknown references.
        var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));foreach(var t in target.Tracks.Where(t=>remove.Contains(t.WaveId))){waves.SetNumber(t.WaveformIndex,"StreamAwbId",65535);waves.SetNumber(t.WaveformIndex,"NumSamples",0);}
        top.SetBlob(0,"WaveformTable",waves.Write());using var awb=new AwbReader(File.OpenRead(bank));var entries=(Batch is null?awb.Waves.Select(w=>new AwbWriter.Entry((ushort)w.WaveId,w.Length,()=>awb.GetWaveSubfileStream(w))):Batch.Entries(bank,awb)).Where(w=>!remove.Contains(w.Id)).ToArray();Publish(sheet,bank,output,top,awb,entries,port);
        if(CriAudio.List(Path.Combine(output,Path.GetFileName(sheet))).Any(c=>c.CueId==cueId))throw new InvalidDataException("Cue deletion verification failed.");
    }
    static void Publish(string sheet,string bank,string output,CriUtf top,AwbReader original,IReadOnlyList<AwbWriter.Entry> entries,int port)
    {
        if(Batch is not null){Batch.Update(bank,entries,top,sheet,output);return;}
        if(Path.GetFullPath(output)==Path.GetDirectoryName(Path.GetFullPath(sheet))||Path.GetFullPath(output)==Path.GetDirectoryName(Path.GetFullPath(bank)))throw new InvalidOperationException("Isolated staging required.");Directory.CreateDirectory(output);var dest=Path.Combine(output,Path.GetFileName(bank));var header=new byte[16];using(var f=File.OpenRead(bank))f.ReadExactly(header);using(var f=File.Create(dest))AwbWriter.Write(f,entries,BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)),original.Subkey);
        var hashes=CriUtf.Read(top.Blob(0,"StreamAwbHash"));using(var f=File.OpenRead(dest))hashes.SetBlob(port,"Hash",MD5.HashData(f));top.SetBlob(0,"StreamAwbHash",hashes.Write());var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));var bytes=new byte[16+entries.Count*2+(entries.Count+1)*4+2];using(var f=File.OpenRead(dest))f.ReadExactly(bytes.AsSpan(0,bytes.Length-2));headers.SetBlob(port,"Header",bytes);top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());CueSheetWriter.Embed(sheet,top.Write(),Path.Combine(output,Path.GetFileName(sheet)));
    }
}
