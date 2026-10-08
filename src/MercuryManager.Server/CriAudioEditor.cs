using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
public static class CriAudioEditor
{
    public static void Replace(string sheet,string bank,ushort waveId,string hca,string outputDirectory,WaveformLoopEdit[]? extensions=null,string? targetBank=null)
    {
        if(Path.GetFullPath(outputDirectory)==Path.GetDirectoryName(Path.GetFullPath(sheet)))throw new InvalidOperationException("Use an isolated staging directory.");
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));
        var streams=CriUtf.Read(top.Blob(0,"StreamAwbHash"));
        using var parser=new AcbReader(new MemoryStream(top.Write()));
        var banks=parser.AtomCueSheetData["StreamAwb"];int port=banks.FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));if(port<0)throw new InvalidDataException("Bank is not referenced by CueSheet.");
        using var awb=new AwbReader(File.OpenRead(bank));if(!(CriCueEditor.Batch is null?awb.Waves.Any(w=>w.WaveId==waveId):CriCueEditor.Batch.Entries(bank,awb).Any(w=>w.Id==waveId)))throw new InvalidDataException("Wave ID missing.");
        int channels,rate;long samples;
        HcaInfo info;using(var stream=File.OpenRead(hca))info=new HcaDecoder(stream,0,awb.Subkey).HcaInfo;
        var affected=parser.AtomCueSheetData["Waveform"].Select((r,i)=>(r,i)).Where(x=>Convert.ToInt32(x.r["Streaming"])!=0&&Convert.ToInt32(x.r["StreamAwbPortNo"])==port&&Convert.ToInt32(x.r["StreamAwbId"])==waveId).ToArray();
        // Preserve extension payloads and ACB loop mode; these are independent of HCA loop chunks.
        byte[]? extensionBefore=affected.Any(x=>Convert.ToInt32(x.r["ExtensionData"]) is not (-1 or 65535))?top.Blob(0,"WaveformExtensionDataTable"):null;
        if(extensionBefore is not null){var extension=CriUtf.Read(extensionBefore);foreach(var x in affected){int index=Convert.ToInt32(x.r["ExtensionData"]);if(index is not (-1 or 65535)&&(index<0||index>=extension.Rows.Count))throw new InvalidDataException("Invalid waveform extension reference.");}}
        using(var pcm=new HcaWaveStream(File.OpenRead(hca),0,awb.Subkey)){channels=pcm.WaveFormat.Channels;rate=pcm.WaveFormat.SampleRate;samples=pcm.Length/pcm.WaveFormat.BlockAlign;var b=new byte[32768];while(pcm.Read(b,0,b.Length)>0){} }
        var waveform=CriUtf.Read(top.Blob(0,"WaveformTable"));
        long Number(byte[] b)=>b.Length switch{1=>b[0],2=>BinaryPrimitives.ReadUInt16BigEndian(b),4=>BinaryPrimitives.ReadUInt32BigEndian(b),8=>checked((long)BinaryPrimitives.ReadUInt64BigEndian(b)),_=>throw new InvalidDataException()};
        void Set(Dictionary<string,byte[]> r,string key,long v){var b=r[key];switch(b.Length){case 1:b[0]=checked((byte)v);break;case 2:BinaryPrimitives.WriteUInt16BigEndian(b,checked((ushort)v));break;case 4:BinaryPrimitives.WriteUInt32BigEndian(b,checked((uint)v));break;default:throw new InvalidDataException("Unsupported numeric cell.");}}
        int changed=0;foreach(var r in waveform.Rows)if(Number(r["Streaming"])!=0&&Number(r["StreamAwbPortNo"])==port&&Number(r["StreamAwbId"])==waveId){foreach(var key in new[]{"NumChannels","SamplingRate","NumSamples"})if((waveform.Columns.Single(c=>c.Name==key).Flags&0x40)==0){int ci=waveform.Columns.FindIndex(c=>c.Name==key);waveform.Columns[ci]=waveform.Columns[ci] with{Flags=0x50,Constant=null};}Set(r,"NumChannels",channels);Set(r,"SamplingRate",rate);Set(r,"NumSamples",samples);changed++;}
        if(changed==0)throw new InvalidDataException("Wave has no CueSheet waveform references.");
        var cueTable=CriUtf.Read(top.Blob(0,"CueTable"));var cueNames=CriUtf.Read(top.Blob(0,"CueNameTable"));var cueList=CriAudio.List(sheet);
        foreach(var cue in cueList.Where(c=>c.Tracks.Any(t=>t.Port==port&&t.WaveId==waveId)))
        {
            if(cue.Error!=null||cue.Tracks.Length==0)throw new InvalidDataException("Cannot determine Cue duration.");
            long duration=cue.Tracks.Max(t=>t.Port==port&&t.WaveId==waveId?(samples*1000+rate-1)/rate:(t.Samples*1000+t.SampleRate-1)/t.SampleRate);
            int ci=checked((int)cueNames.Number(cue.Index,"CueIndex"));if(cueTable.Number(ci,"CueId")!=cue.CueId)throw new InvalidDataException("Cue name reference mismatch.");cueTable.SetNumber(ci,"Length",duration);
        }
        top.SetBlob(0,"CueTable",cueTable.Write());
        if(extensions is {Length:>0}&&!extensions.Select(e=>e.WaveformIndex).ToHashSet().SetEquals(affected.Select(x=>x.i)))throw new InvalidDataException("Explicit loop edits must include every waveform sharing this HCA.");
        WaveformExtensions.Apply(top,waveform,affected.Select(x=>x.i).ToHashSet(),extensions,samples);
        if((extensions is null||extensions.Length==0)&&extensionBefore is not null&&!extensionBefore.SequenceEqual(top.Blob(0,"WaveformExtensionDataTable")))throw new InvalidDataException("Waveform extension changed during replacement.");
        if(targetBank is not null&&Path.GetFullPath(targetBank)!=Path.GetFullPath(bank))
        {
            var batchTarget=CriCueEditor.Batch??throw new InvalidOperationException("Bank redirection requires batch staging.");
            int targetPort=AudioBanks.Port(top,targetBank);if(targetPort<0)throw new InvalidDataException("Target bank is not registered.");
            using var targetReader=new AwbReader(File.OpenRead(targetBank));if(targetReader.Subkey!=awb.Subkey)throw new InvalidDataException("Target bank subkey differs; encrypted HCA redirection is unsupported.");
            var targetEntries=batchTarget.Entries(targetBank,targetReader);var used=targetEntries.Select(e=>(int)e.Id).ToHashSet();int free=Enumerable.Range(0,65535).FirstOrDefault(i=>!used.Contains(i),-1);if(free<0)throw new InvalidDataException("Target bank is full.");
            foreach(var x in affected){waveform.SetNumber(x.i,"StreamAwbPortNo",targetPort);waveform.SetNumber(x.i,"StreamAwbId",free);}
            top.SetBlob(0,"WaveformTable",waveform.Write());batchTarget.Update(targetBank,targetEntries.Append(new AwbWriter.Entry((ushort)free,new FileInfo(hca).Length,()=>File.OpenRead(hca))).ToArray(),top,sheet,outputDirectory);return;
        }
        if(CriCueEditor.Batch is {} batch){top.SetBlob(0,"WaveformTable",waveform.Write());var entries=batch.Entries(bank,awb).Select(w=>w.Id==waveId?new AwbWriter.Entry(waveId,new FileInfo(hca).Length,()=>File.OpenRead(hca)):w).ToArray();batch.Update(bank,entries,top,sheet,outputDirectory);return;}
        Directory.CreateDirectory(outputDirectory);var bankOutput=Path.Combine(outputDirectory,Path.GetFileName(bank));
        var header=new byte[16];using(var f=File.OpenRead(bank))f.ReadExactly(header);ushort alignment=BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
        using(var target=File.Create(bankOutput)){AwbWriter.Write(target,awb.Waves.Select(w=>w.WaveId==waveId?new AwbWriter.Entry(waveId,new FileInfo(hca).Length,()=>File.OpenRead(hca)):new AwbWriter.Entry(checked((ushort)w.WaveId),w.Length,()=>awb.GetWaveSubfileStream(w))).ToArray(),alignment,awb.Subkey);}
        using(var f=File.OpenRead(bankOutput))streams.SetBlob(port,"Hash",MD5.HashData(f));top.SetBlob(0,"StreamAwbHash",streams.Write());top.SetBlob(0,"WaveformTable",waveform.Write());
        var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));using(var f=File.OpenRead(bankOutput)){var b=new byte[16+awb.Waves.Count*2+(awb.Waves.Count+1)*4+2];f.ReadExactly(b.AsSpan(0,b.Length-2));headers.SetBlob(port,"Header",b);}top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());
        var rebuilt=top.Write();using(var independent=new AcbReader(new MemoryStream(rebuilt))){if(independent.AtomCueSheetData["CueName"].Count!=parser.AtomCueSheetData["CueName"].Count)throw new InvalidDataException("Cue count changed.");foreach(var cue in independent.AtomCueSheetData["Cue"]){int id=Convert.ToInt32(cue["CueId"]);var before=parser.GetWaveformsFromCueId(id);var after=independent.GetWaveformsFromCueId(id);if(!before.Select(w=>(w.Id,w.StreamId,w.PortNo)).SequenceEqual(after.Select(w=>(w.Id,w.StreamId,w.PortNo))))throw new InvalidDataException("Cue references changed.");}}
        CueSheetWriter.Embed(sheet,rebuilt,Path.Combine(outputDirectory,Path.GetFileName(sheet)));
        using var check=new AwbReader(File.OpenRead(bankOutput));if(!check.Waves.Select(w=>w.WaveId).SequenceEqual(awb.Waves.Select(w=>w.WaveId)))throw new InvalidDataException("Wave ID verification failed.");
    }
}
