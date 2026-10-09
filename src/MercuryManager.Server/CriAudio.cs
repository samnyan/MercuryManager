using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Criware;
using CUE4Parse.UE4.Criware.Readers;
using CUE4Parse.UE4.Criware.Decoders.HCA;
namespace MercuryManager.Server;

public static class CriAudio
{
    public sealed record Cue(int Index,string Name,int CueId,int[] WaveIds,string? Error){public AudioTrack[] Tracks{get;init;}=[];}
    public sealed record AudioTrack(int TrackIndex,int WaveformIndex,int WaveId,string Bank,int Port,string Route,int Channels,int SampleRate,long Samples,Dictionary<string,int> Sends);
    private static AudioTrack[] Tracks(AcbReader acb,Dictionary<string,object?> cue)
    {
        var tables=acb.AtomCueSheetData;
        int Number(Dictionary<string,object?> r,string k)=>Convert.ToInt32(r[k]);
        byte[] Bytes(Dictionary<string,object?> r,string k)=>r[k] as byte[]??[];
        int U16(byte[] b,int i)=>(b[i]<<8)|b[i+1];
        if(Number(cue,"ReferenceType")!=3)return [];
        var sequence=tables["Sequence"][Number(cue,"ReferenceIndex")];var indexes=Bytes(sequence,"TrackIndex");var result=new List<AudioTrack>();
        for(int i=0;i+1<indexes.Length;i+=2)
        {
            int trackIndex=U16(indexes,i);var track=tables["Track"][trackIndex];var sends=new Dictionary<string,int>();
            // Track commands are optional (e.g. voice sheets use CommandIndex=-1).
            int commandIndex=Number(track,"CommandIndex");
            var command=commandIndex is -1 or 65535 ? []
                : Bytes(tables["TrackCommand"][commandIndex],"Command");
            for(int pos=0;pos+3<=command.Length;){int op=U16(command,pos),size=command[pos+2];pos+=3;if(pos+size>command.Length)break;if(op==111&&size==4){int key=U16(command,pos);if(key<tables["Strings"].Count)sends[Convert.ToString(tables["Strings"][key]["StringValue"])??""]=U16(command,pos+2);}pos+=size;}
            var waveIndexes=new List<int>();var visited=new HashSet<int>();
            void Synth(int index){if(!visited.Add(index))return;var refs=Bytes(tables["Synth"][index],"ReferenceItems");for(int j=0;j+3<refs.Length;j+=4){int type=U16(refs,j),target=U16(refs,j+2);if(type==1)waveIndexes.Add(target);else if(type==2)Synth(target);}}
            var events=Bytes(tables["TrackEvent"][Number(track,"EventIndex")],"Command");
            for(int pos=0;pos+3<=events.Length;){int op=U16(events,pos),size=events[pos+2];pos+=3;if(pos+size>events.Length)break;if(op is 2000 or 2003 && size>=4 && U16(events,pos)==2)Synth(U16(events,pos+2));pos+=size;}
            foreach(int wi in waveIndexes){var w=tables["Waveform"][wi];bool streaming=Number(w,"Streaming")!=0;int port=Number(w,"StreamAwbPortNo");string bank=streaming?Convert.ToString(tables["StreamAwb"][port]["Name"])+".awb":"Embedded AWB";
                result.Add(new(trackIndex,wi,Number(w,streaming?"StreamAwbId":"MemoryAwbId"),bank,port,string.Join(" + ",sends.Where(s=>s.Value>0).Select(s=>s.Key)),Number(w,"NumChannels"),Number(w,"SamplingRate"),Convert.ToInt64(w["NumSamples"]),sends));}
        }
        return result.ToArray();
    }
    private static (DefaultFileProvider Provider,AcbReader Acb) OpenAcb(string path)
    {
        var provider=new DefaultFileProvider(Path.GetDirectoryName(path)!,SearchOption.TopDirectoryOnly,new VersionContainer(EGame.GAME_UE4_19),StringComparer.OrdinalIgnoreCase);
        try{provider.Initialize();var file=provider.Files.Values.Single(f=>f.Path.EndsWith(Path.GetFileName(path),StringComparison.OrdinalIgnoreCase));
            var sheet=provider.LoadPackage(file.Path).GetExports().OfType<USoundAtomCueSheet>().Single();
            return (provider,sheet.AcbReader??throw new InvalidDataException("Missing ACB."));}
        catch{provider.Dispose();throw;}
    }
    public static (string Path,string? CueName) Target(string path,Func<string,string> resolve)
    {
        if(path.EndsWith(".awb",StringComparison.Ordinal))return (path,null);
        var asset=new UAssetAPI.UAsset(path,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
        var cue=asset.Exports.OfType<UAssetAPI.ExportTypes.NormalExport>().SingleOrDefault(e=>e.GetExportClassType().ToString()=="SoundAtomCue");
        if(cue is null)return (path,null);
        var reference=cue.Data.OfType<UAssetAPI.PropertyTypes.Objects.ObjectPropertyData>().Single(p=>p.Name.ToString()=="CueSheet").Value;
        if(reference.Index>=0)throw new InvalidDataException("Expected imported CueSheet reference.");
        var import=asset.Imports[-reference.Index-1];
        if(import.OuterIndex.Index>=0)throw new InvalidDataException("Missing CueSheet package.");
        var package=asset.Imports[-import.OuterIndex.Index-1].ObjectName.ToString();
        if(!package.StartsWith("/Game/",StringComparison.Ordinal))throw new InvalidDataException("CueSheet is outside Content.");
        var name=cue.Data.OfType<UAssetAPI.PropertyTypes.Objects.StrPropertyData>().Single(p=>p.Name.ToString()=="CueName").Value.ToString();
        return (resolve(package[6..]+".uasset"),name);
    }
    public static object DescribeSheet(string path)
    {
        var (provider,acb)=OpenAcb(path);using(provider)using(acb)
            return new {Parser="CUE4Parse ACB",Tables=acb.AtomCueSheetData};
    }
    public static Cue[] List(string path)
    {
        if(path.EndsWith(".awb",StringComparison.OrdinalIgnoreCase))
        {using var awb=new AwbReader(File.OpenRead(path));return awb.Waves.Select((w,i)=>new Cue(i,$"Wave {w.WaveId}",w.WaveId,[w.WaveId],null)).ToArray();}
        var (provider,acb)=OpenAcb(path);using(provider)using(acb)
        {
            return acb.AtomCueSheetData["CueName"].Select((row,index)=>{
                var cueIndex=Convert.ToInt32(row["CueIndex"]);var id=Convert.ToInt32(acb.AtomCueSheetData["Cue"][cueIndex]["CueId"]);
                try{return new Cue(index,Convert.ToString(row["CueName"])??"",id,acb.GetWaveformsFromCueId(id).Select(w=>(int)(w.Streaming==EWaveformStreamType.Memory?w.Id:w.StreamId)).ToArray(),null){Tracks=Tracks(acb,acb.AtomCueSheetData["Cue"][cueIndex])};}
                catch(Exception ex){return new Cue(index,Convert.ToString(row["CueName"])??"",id,[],ex.Message);}
            }).ToArray();
        }
    }
    // HTTP PCM is emitted incrementally; neither the complete AWB nor decoded WAV is buffered.
    public static async Task Play(HttpContext context,string path,int index,int part,Func<string,string>? resolveBank=null)
    {
        DefaultFileProvider? provider=null;AcbReader? acb=null;AwbReader? awb=null;
        try
        {
            int waveId;
            if(path.EndsWith(".awb",StringComparison.OrdinalIgnoreCase))
            {awb=new AwbReader(File.OpenRead(path));if(index<0||index>=awb.Waves.Count)throw new ArgumentException("Invalid wave index.");waveId=awb.Waves[index].WaveId;}
            else
            {
                (provider,acb)=OpenAcb(path);var names=acb.AtomCueSheetData["CueName"];
                if(index<0||index>=names.Count)throw new ArgumentException("Invalid cue index.");
                var cue=acb.AtomCueSheetData["Cue"][Convert.ToInt32(names[index]["CueIndex"])];
                var waves=acb.GetWaveformsFromCueId(Convert.ToInt32(cue["CueId"]));
                if(part<0||part>=waves.Count)throw new ArgumentException("Cue has no selected waveform.");
                var wave=waves[part];
                if(wave.EncodeType is not (EEncodeType.HCA or EEncodeType.HCA_ALT))throw new InvalidDataException("Only HCA preview is supported.");
                if(wave.Streaming==EWaveformStreamType.Memory){awb=acb.GetAwb();waveId=wave.Id;}
                else{var bankName=Convert.ToString(acb.AtomCueSheetData["StreamAwb"][wave.PortNo]["Name"])??throw new InvalidDataException("Missing bank name.");if(bankName!=Path.GetFileName(bankName)||bankName.Contains('\\'))throw new InvalidDataException("Invalid bank name.");var external=resolveBank is null?Path.Combine(Path.GetDirectoryName(path)!,bankName+".awb"):resolveBank(bankName+".awb");MusicWorkspaceStore.RejectLinks(external);awb=new AwbReader(File.OpenRead(external));waveId=wave.StreamId;}
            }
            if(awb is null)throw new InvalidDataException("No AWB available.");
            var entry=awb.Waves.Single(w=>w.WaveId==waveId);
            using var source=awb.GetWaveSubfileStream(entry);
            using var pcm=new HcaWaveStream(source,0,awb.Subkey);
            if(pcm.Length>uint.MaxValue-36||pcm.Length<=0)throw new InvalidDataException("Audio exceeds WAV limits.");
            using var header=new MemoryStream();using(var w=new BinaryWriter(header,System.Text.Encoding.ASCII,true))
            {w.Write("RIFF"u8);w.Write((uint)pcm.Length+36);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)pcm.WaveFormat.Channels);w.Write(pcm.WaveFormat.SampleRate);w.Write(pcm.WaveFormat.AverageBytesPerSecond);w.Write((short)pcm.WaveFormat.BlockAlign);w.Write((short)16);w.Write("data"u8);w.Write((uint)pcm.Length);}
            long total=pcm.Length+44,start=0,end=total-1;
            context.Response.Headers.AcceptRanges="bytes";
            var range=context.Request.Headers.Range.ToString();
            if(range.Length>0)
            {
                var match=System.Text.RegularExpressions.Regex.Match(range,@"^bytes=(\d*)-(\d*)$");
                if(!match.Success || (match.Groups[1].Value.Length==0&&match.Groups[2].Value.Length==0))
                {context.Response.StatusCode=416;context.Response.Headers.ContentRange=$"bytes */{total}";return;}
                if(match.Groups[1].Value.Length==0){if(!long.TryParse(match.Groups[2].Value,out var suffix)||suffix<=0){context.Response.StatusCode=416;return;}start=Math.Max(0,total-suffix);}
                else if(!long.TryParse(match.Groups[1].Value,out start)){context.Response.StatusCode=416;return;}
                if(match.Groups[1].Value.Length>0&&match.Groups[2].Value.Length>0){if(!long.TryParse(match.Groups[2].Value,out end)){context.Response.StatusCode=416;return;}end=Math.Min(end,total-1);}
                if(start>=total||start>end){context.Response.StatusCode=416;context.Response.Headers.ContentRange=$"bytes */{total}";return;}
                context.Response.StatusCode=206;context.Response.Headers.ContentRange=$"bytes {start}-{end}/{total}";
            }
            context.Response.ContentType="audio/wav";context.Response.ContentLength=end-start+1;
            long remaining=end-start+1;
            if(start<44){int n=(int)Math.Min(44-start,remaining);await context.Response.Body.WriteAsync(header.ToArray().AsMemory((int)start,n),context.RequestAborted);remaining-=n;}
            long offset=Math.Max(0,start-44);int align=pcm.WaveFormat.BlockAlign;pcm.Position=offset-offset%align;
            if(offset%align>0){var frame=new byte[align];if(pcm.Read(frame,0,align)!=align)throw new EndOfStreamException();int from=(int)(offset%align),n=(int)Math.Min(align-from,remaining);await context.Response.Body.WriteAsync(frame.AsMemory(from,n),context.RequestAborted);remaining-=n;}
            var buffer=new byte[32768];
            // Read whole PCM frames, then slice arbitrary HTTP byte boundaries.
            while(remaining>0){context.RequestAborted.ThrowIfCancellationRequested();int count=pcm.Read(buffer,0,buffer.Length);if(count==0)break;int n=(int)Math.Min(count,remaining);await context.Response.Body.WriteAsync(buffer.AsMemory(0,n),context.RequestAborted);remaining-=n;await context.Response.Body.FlushAsync(context.RequestAborted);}
        }
        finally{awb?.Dispose();acb?.Dispose();provider?.Dispose();}
    }
}
