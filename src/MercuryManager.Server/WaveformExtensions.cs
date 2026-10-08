using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
public sealed record WaveformLoopEdit(int WaveformIndex,long LoopStart,long LoopEnd,int LoopFlag);
public static class WaveformExtensions
{
    public static object[] Describe(string sheet,string bank,ushort waveId)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheet,EngineVersion.VER_UE4_19)));
        using var acb=new CUE4Parse.UE4.Criware.Readers.AcbReader(new MemoryStream(top.Write()));
        int port=acb.AtomCueSheetData["StreamAwb"].FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));
        var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));
        var blob=top.Blob(0,"WaveformExtensionDataTable");var extension=blob.Length>0?CriUtf.Read(blob):null;
        return Enumerable.Range(0,waves.Rows.Count).Where(i=>waves.Number(i,"Streaming")!=0&&waves.Number(i,"StreamAwbPortNo")==port&&waves.Number(i,"StreamAwbId")==waveId).Select(i=>{
            long index=waves.Number(i,"ExtensionData");bool present=index!=65535;
            if(present&&(extension is null||index>=extension.Rows.Count))throw new InvalidDataException("Invalid extension reference.");
            return (object)new {waveformIndex=i,extensionIndex=present?(long?)index:null,loopFlag=waves.Number(i,"LoopFlag"),sampleRate=waves.Number(i,"SamplingRate"),samples=waves.Number(i,"NumSamples"),fields=present?extension!.Rows[(int)index].ToDictionary(p=>p.Key,p=>Convert.ToHexString(p.Value)):new Dictionary<string,string>(),loopStart=present&&extension!.Rows[(int)index].ContainsKey("LoopStart")?(long?)extension.Number((int)index,"LoopStart"):null,loopEnd=present&&extension!.Rows[(int)index].ContainsKey("LoopEnd")?(long?)extension.Number((int)index,"LoopEnd"):null};
        }).ToArray();
    }
    public static void Apply(CriUtf top,CriUtf waves,IReadOnlySet<int> affected,WaveformLoopEdit[]? edits,long samples)
    {
        if(edits is null||edits.Length==0)return;
        if(edits.Select(e=>e.WaveformIndex).Distinct().Count()!=edits.Length)throw new InvalidDataException("Duplicate waveform edit.");
        var blob=top.Blob(0,"WaveformExtensionDataTable");var extension=blob.Length>0?CriUtf.Read(blob):null;
        foreach(var edit in edits)
        {
            if(!affected.Contains(edit.WaveformIndex))throw new InvalidDataException("Extension edit does not belong to replaced wave.");
            int index=checked((int)waves.Number(edit.WaveformIndex,"ExtensionData"));
            if(edit.LoopFlag is <0 or >2)throw new InvalidDataException("LoopFlag must be 0–2.");
            if(index==65535){waves.SetNumber(edit.WaveformIndex,"LoopFlag",edit.LoopFlag==0?0:1);continue;}
            if(extension is null||index<0||index>=extension.Rows.Count)throw new InvalidDataException("This waveform has no editable extension.");
            if(edit.LoopFlag is <0 or >2||edit.LoopStart<0||edit.LoopEnd<=edit.LoopStart||edit.LoopEnd>samples)throw new InvalidDataException("Loop points must satisfy 0 <= start < end <= new sample count; LoopFlag must be 0–2.");
            // Clone to avoid changing another waveform sharing the original record.
            int cloned=extension.CloneRow(index);extension.SetNumber(cloned,"LoopStart",edit.LoopStart);extension.SetNumber(cloned,"LoopEnd",edit.LoopEnd);
            waves.SetNumber(edit.WaveformIndex,"ExtensionData",cloned);waves.SetNumber(edit.WaveformIndex,"LoopFlag",edit.LoopFlag);
        }
        if(extension is not null)top.SetBlob(0,"WaveformExtensionDataTable",extension.Write());
    }
}
