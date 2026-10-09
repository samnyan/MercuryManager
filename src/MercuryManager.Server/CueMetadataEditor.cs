using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
public sealed record CueMetadataEdit(int WaveformIndex,string TargetBank,ushort TargetWaveId,int CueId,Dictionary<string,long>? CueFields=null,WaveformLoopEdit[]? Loops=null,string? CueName=null,int? NewCueId=null,string? UploadId=null);
public static class CueMetadataEditor
{
    public static AudioFileInfo Inspect(string bank,ushort waveId)
    {
        using var awb=new AwbReader(File.OpenRead(bank));var wave=awb.Waves.Single(w=>w.WaveId==waveId);using var s=awb.GetWaveSubfileStream(wave);using var m=new MemoryStream();s.CopyTo(m);return AudioInspection.Read(m.ToArray());
    }
    public static object Describe(string sheet,int cueId)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));var names=CriUtf.Read(top.Blob(0,"CueNameTable"));var table=CriUtf.Read(top.Blob(0,"CueTable"));var cue=CriAudio.List(sheet).Single(c=>c.CueId==cueId);int index=checked((int)names.Number(cue.Index,"CueIndex"));
        return new {name=cue.Name,fields=table.Columns.Where(c=>c.Name is "Length" or "Priority" or "Probability").ToDictionary(c=>c.Name,c=>table.Number(index,c.Name))};
    }
    public static void Edit(string sheet,string bank,CueMetadataEdit edit,string output,string? replacement=null)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));
        var original=CriAudio.List(sheet).Single(c=>c.CueId==edit.CueId).Tracks.Single(t=>t.WaveformIndex==edit.WaveformIndex);bool relink=original.Bank!=edit.TargetBank||original.WaveId!=edit.TargetWaveId;
        int port=AudioBanks.Port(top,edit.TargetBank);if(port<0)throw new InvalidDataException("Target bank is not registered.");
        using var awb=new AwbReader(File.OpenRead(bank));
        var entries=CriCueEditor.Batch?.Entries(bank,awb)??awb.Waves.Select(w=>new AwbWriter.Entry((ushort)w.WaveId,w.Length,()=>awb.GetWaveSubfileStream(w))).ToArray();
        var entry=entries.SingleOrDefault(e=>e.Id==edit.TargetWaveId)??throw new InvalidDataException("Target Wave ID missing.");
        using var stream=replacement is null?entry.Open():File.OpenRead(replacement);var info=new HcaDecoder(stream,0,awb.Subkey).HcaInfo;
        var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));
        if(edit.WaveformIndex<0||edit.WaveformIndex>=waves.Rows.Count)throw new InvalidDataException("Waveform index missing.");
        var cues=CriAudio.List(sheet);var cue=cues.SingleOrDefault(c=>c.CueId==edit.CueId)??throw new InvalidDataException("Cue missing.");
        if(!cue.Tracks.Any(t=>t.WaveformIndex==edit.WaveformIndex))throw new InvalidDataException("Waveform does not belong to Cue.");
        waves.SetNumber(edit.WaveformIndex,"Streaming",1);waves.SetNumber(edit.WaveformIndex,"StreamAwbPortNo",port);waves.SetNumber(edit.WaveformIndex,"StreamAwbId",edit.TargetWaveId);
        waves.SetNumber(edit.WaveformIndex,"NumChannels",info.ChannelCount);waves.SetNumber(edit.WaveformIndex,"SamplingRate",info.SamplingRate);waves.SetNumber(edit.WaveformIndex,"NumSamples",info.SampleCount);
        if(replacement is not null)
        {
            var affected=Enumerable.Range(0,waves.Rows.Count).Where(i=>waves.Number(i,"Streaming")!=0&&waves.Number(i,"StreamAwbPortNo")==port&&waves.Number(i,"StreamAwbId")==edit.TargetWaveId).ToArray();
            foreach(var wi in affected){waves.SetNumber(wi,"NumChannels",info.ChannelCount);waves.SetNumber(wi,"SamplingRate",info.SamplingRate);waves.SetNumber(wi,"NumSamples",info.SampleCount);}
            var loops=affected.Select(wi=>new WaveformLoopEdit(wi,info.LoopEnabled?info.LoopStartSample:0,info.LoopEnabled?info.LoopEndSample:info.SampleCount,info.LoopEnabled?2:0)).ToArray();
            WaveformExtensions.Apply(top,waves,affected.ToHashSet(),loops,info.SampleCount);
        }
        else if(relink)
            WaveformExtensions.Apply(top,waves,new HashSet<int>{edit.WaveformIndex},[new(edit.WaveformIndex,info.LoopEnabled?info.LoopStartSample:0,info.LoopEnabled?info.LoopEndSample:info.SampleCount,info.LoopEnabled?2:0)],info.SampleCount);
        var names=CriUtf.Read(top.Blob(0,"CueNameTable"));var table=CriUtf.Read(top.Blob(0,"CueTable"));int index=checked((int)names.Number(cue.Index,"CueIndex"));
        if(edit.NewCueId is not null){if(edit.NewCueId<0||cues.Any(c=>c.CueId!=edit.CueId&&c.CueId==edit.NewCueId))throw new InvalidDataException("Cue ID already exists or is invalid.");table.SetNumber(index,"CueId",edit.NewCueId.Value);}
        if(edit.CueName is not null){if(cues.Any(c=>c.CueId!=edit.CueId&&c.Name==edit.CueName))throw new InvalidDataException("Cue name already exists.");if(string.IsNullOrWhiteSpace(edit.CueName)||edit.CueName.Length>256||edit.CueName.Contains('\0'))throw new InvalidDataException("Invalid Cue name.");names.SetText(cue.Index,"CueName",edit.CueName);top.SetBlob(0,"CueNameTable",names.Write());}
        foreach(var field in edit.CueFields??[])
        {
            if(field.Key is not ("Length" or "Priority" or "Probability"))throw new InvalidDataException("Unsupported Cue metadata field: "+field.Key);
            if(!table.Rows[index].ContainsKey(field.Key))throw new InvalidDataException("Cue metadata field absent: "+field.Key);
            table.SetNumber(index,field.Key,field.Value);
        }
        // A shared waveform can affect other Cues; recompute all affected lengths.
        foreach(var affected in cues.Where(c=>(relink||replacement is not null)&&c.Tracks.Any(t=>t.WaveformIndex==edit.WaveformIndex||replacement is not null&&t.Bank==edit.TargetBank&&t.WaveId==edit.TargetWaveId)))
        {
            long duration=affected.Tracks.Max(t=>t.WaveformIndex==edit.WaveformIndex||replacement is not null&&t.Bank==edit.TargetBank&&t.WaveId==edit.TargetWaveId?((long)info.SampleCount*1000+info.SamplingRate-1)/info.SamplingRate:(t.Samples*1000+t.SampleRate-1)/t.SampleRate);
            if(affected.CueId!=edit.CueId||edit.CueFields?.ContainsKey("Length")!=true)table.SetNumber(checked((int)names.Number(affected.Index,"CueIndex")),"Length",duration);
        }
        top.SetBlob(0,"WaveformTable",waves.Write());top.SetBlob(0,"CueTable",table.Write());
        if(replacement is not null){var batch=CriCueEditor.Batch??throw new InvalidOperationException("Batch required.");batch.Update(bank,entries.Select(e=>e.Id==edit.TargetWaveId?new AwbWriter.Entry(e.Id,new FileInfo(replacement).Length,()=>File.OpenRead(replacement)):e).ToArray(),top,sheet,output);}
        else{Directory.CreateDirectory(output);CueSheetWriter.Embed(sheet,top.Write(),Path.Combine(output,Path.GetFileName(sheet)));}
        if(CriAudio.List(Path.Combine(output,Path.GetFileName(sheet))).Any(c=>c.Error!=null))throw new InvalidDataException("Edited references failed validation.");
    }
}
