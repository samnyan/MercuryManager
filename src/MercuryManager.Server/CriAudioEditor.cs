using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
public static class CriAudioEditor
{
    public static void Replace(string sheet,string bank,ushort waveId,string hca,string outputDirectory)
    {
        if(Path.GetFullPath(outputDirectory)==Path.GetDirectoryName(Path.GetFullPath(sheet)))throw new InvalidOperationException("Use an isolated staging directory.");
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));
        var streams=CriUtf.Read(top.Blob(0,"StreamAwbHash"));
        using var parser=new AcbReader(new MemoryStream(top.Write()));
        var banks=parser.AtomCueSheetData["StreamAwb"];int port=banks.FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));if(port<0)throw new InvalidDataException("Bank is not referenced by CueSheet.");
        using var awb=new AwbReader(File.OpenRead(bank));if(!awb.Waves.Any(w=>w.WaveId==waveId))throw new InvalidDataException("Wave ID missing.");
        int channels,rate;long samples;
        HcaInfo info,oldInfo;using(var stream=File.OpenRead(hca))info=new HcaDecoder(stream,0,awb.Subkey).HcaInfo;using(var stream=awb.GetWaveSubfileStream(awb.Waves.Single(w=>w.WaveId==waveId)))oldInfo=new HcaDecoder(stream,0,awb.Subkey).HcaInfo;
        if(info.LoopEnabled!=oldInfo.LoopEnabled||info.LoopStartSample!=oldInfo.LoopStartSample||info.LoopEndSample!=oldInfo.LoopEndSample)throw new InvalidDataException("Loop changes require explicit loop editing.");
        using(var pcm=new HcaWaveStream(File.OpenRead(hca),0,awb.Subkey)){channels=pcm.WaveFormat.Channels;rate=pcm.WaveFormat.SampleRate;samples=pcm.Length/pcm.WaveFormat.BlockAlign;var b=new byte[32768];while(pcm.Read(b,0,b.Length)>0){} }
        var waveform=CriUtf.Read(top.Blob(0,"WaveformTable"));
        long Number(byte[] b)=>b.Length switch{1=>b[0],2=>BinaryPrimitives.ReadUInt16BigEndian(b),4=>BinaryPrimitives.ReadUInt32BigEndian(b),8=>checked((long)BinaryPrimitives.ReadUInt64BigEndian(b)),_=>throw new InvalidDataException()};
        void Set(Dictionary<string,byte[]> r,string key,long v){var b=r[key];switch(b.Length){case 1:b[0]=checked((byte)v);break;case 2:BinaryPrimitives.WriteUInt16BigEndian(b,checked((ushort)v));break;case 4:BinaryPrimitives.WriteUInt32BigEndian(b,checked((uint)v));break;default:throw new InvalidDataException("Unsupported numeric cell.");}}
        int changed=0;foreach(var r in waveform.Rows)if(Number(r["Streaming"])!=0&&Number(r["StreamAwbPortNo"])==port&&Number(r["StreamAwbId"])==waveId){foreach(var key in new[]{"NumChannels","SamplingRate","NumSamples"})if((waveform.Columns.Single(c=>c.Name==key).Flags&0x40)==0){int ci=waveform.Columns.FindIndex(c=>c.Name==key);waveform.Columns[ci]=waveform.Columns[ci] with{Flags=0x50,Constant=null};}Set(r,"NumChannels",channels);Set(r,"SamplingRate",rate);Set(r,"NumSamples",samples);changed++;}
        if(changed==0)throw new InvalidDataException("Wave has no CueSheet waveform references.");
        var original=parser.AtomCueSheetData["Waveform"].Where(r=>Convert.ToInt32(r["Streaming"])!=0&&Convert.ToInt32(r["StreamAwbPortNo"])==port&&Convert.ToInt32(r["StreamAwbId"])==waveId).ToArray();
        if(original.Any(r=>Convert.ToInt64(r["NumSamples"])!=samples||Convert.ToInt32(r["SamplingRate"])!=rate))throw new InvalidDataException("Duration-changing replacement is not supported yet.");
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
