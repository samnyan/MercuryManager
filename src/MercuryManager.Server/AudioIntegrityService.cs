using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
namespace MercuryManager.Server;

public sealed record AudioRepairRequest(string Revision,string[] Actions,int? WorkBytes=null,Dictionary<string,string>? RecoveryBanks=null);
public sealed record AudioRepairResult(string[] Applied,string[] ChangedFiles,AudioIntegrityReport Before,AudioIntegrityReport After);
public sealed class AudioIntegrityService(MusicWorkspaceStore assets,ResourceService resources)
{
    private Func<string,string> Resolver(string id,ResourceService.AudioResource target,string? source)
        => bank=>resources.Resolve(id,BankRelative(target,bank),source);
    internal static string BankRelative(ResourceService.AudioResource target,string bank)
    {
        AudioBanks.ValidateName(bank);
        var asset=new UAsset(target.Path,EngineVersion.VER_UE4_19);var export=asset.Exports.OfType<NormalExport>().Single(e=>e.GetExportClassType().ToString()=="SoundAtomCueSheet");
        bool overridden=export.Data.OfType<BoolPropertyData>().SingleOrDefault(p=>p.Name.ToString()=="bOverrideAwbDirectory")?.Value==true;
        if(!overridden)throw new InvalidDataException("Default plugin AWB directory is not known to the inspector; an explicit AwbDirectory is required for external bank checks.");
        var directory=export.Data.OfType<StructPropertyData>().Single(p=>p.Name.ToString()=="AwbDirectory").Value.OfType<StrPropertyData>().Single(p=>p.Name.ToString()=="Path").Value.ToString().Replace('\\','/').TrimEnd('/');
        return ResourceService.Validate(directory+"/"+bank);
    }
    public AudioIntegrityReport Inspect(string id,string path,string? source,bool deep,CancellationToken cancellation=default)
    {
        lock(assets){var target=resources.AudioTarget(id,path,source);return AudioIntegrityInspection.Read(target.Path,Resolver(id,target,source),deep,cancellation);}
    }
    public AudioRepairResult Repair(string id,string path,string? source,AudioRepairRequest request,CancellationToken cancellation=default)
    {
        if(request.Actions is null||request.Actions.Length is <1 or >100||request.Actions.Distinct(StringComparer.Ordinal).Count()!=request.Actions.Length)throw new ArgumentException("Select 1–100 distinct repair actions.");
        if(request.WorkBytes is not null&&!request.Actions.Contains("rebuild-stream-work"))throw new ArgumentException("WorkBytes requires rebuild-stream-work.");
        if(request.RecoveryBanks is not null&&request.RecoveryBanks.Any(p=>!request.Actions.Contains(p.Key)||!p.Key.StartsWith("repair-legacy-awb:",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(p.Value)))throw new ArgumentException("RecoveryBanks must contain explicit paths for selected legacy AWB repairs only.");
        lock(assets)
        {
            var target=resources.AudioTarget(id,path,source);if(target.Path.EndsWith(".awb",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Open the paired CueSheet to repair bank/ACB metadata together.");
            var resolve=Resolver(id,target,source);var before=AudioIntegrityInspection.Read(target.Path,resolve,false,cancellation);
            if(string.IsNullOrEmpty(request.Revision)||request.Revision!=before.Revision)throw new InvalidOperationException("Audio files changed; inspect again before repair.");
            var allowed=before.Repairs.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);foreach(string action in request.Actions)if(!allowed.Contains(action))throw new InvalidDataException("Unsafe or unavailable repair action: "+action);
            var top=CriUtf.Read(CueSheetWriter.Extract(new UAsset(target.Path,EngineVersion.VER_UE4_19)));
            var stage=Path.Combine(assets.WorkspaceDirectory(id),"audio-repair-"+Guid.NewGuid().ToString("N"));MusicWorkspaceStore.RejectLinks(stage);Directory.CreateDirectory(stage);
            var stagedBanks=new Dictionary<string,string>(StringComparer.Ordinal);var sourceBankHashes=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            try
            {
                // File-dependent actions address the original ports; sort/remap slots only afterwards.
                foreach(string action in request.Actions.Where(a=>a.Contains(':')))
                {
                    cancellation.ThrowIfCancellationRequested();string[] parts=action.Split(':');if(parts.Length!=2||!int.TryParse(parts[1],out int port))throw new ArgumentException("Invalid repair port.");
                    var slots=CriUtf.Read(top.Blob(0,"StreamAwbHash"));var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));string bank=slots.Text(port,"Name")+".awb";
                    string original=resolve(bank),current=stagedBanks.GetValueOrDefault(bank)??original;
                    if(parts[0]=="repair-legacy-awb")
                    {
                        using(var f=File.OpenRead(original))sourceBankHashes[original]=SHA256.HashData(f);
                        string? donor=request.RecoveryBanks?.GetValueOrDefault(action);
                        if(donor is not null)
                        {
                            if(!Path.IsPathFullyQualified(donor))throw new ArgumentException("Recovery AWB requires an absolute server path.");
                            donor=Path.GetFullPath(donor);MusicWorkspaceStore.RejectLinks(donor);using var f=File.OpenRead(donor);sourceBankHashes[donor]=SHA256.HashData(f);
                        }
                        string output=Path.Combine(stage,bank);var recovered=AwbLegacyRepair.Repack(current,output,donor,cancellation);
                        var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));
                        foreach(var format in recovered)for(int row=0;row<waves.Rows.Count;row++)
                            if(waves.Number(row,"Streaming")!=0&&waves.Number(row,"StreamAwbPortNo")==port&&waves.Number(row,"StreamAwbId")==format.Id&&
                                (waves.Number(row,"NumChannels")!=format.Channels||waves.Number(row,"SamplingRate")!=format.Rate||waves.Number(row,"NumSamples")!=format.Samples))
                                throw new InvalidDataException($"Recovery HCA {format.Id} differs from ACB waveform format/sample count.");
                        current=output;stagedBanks[bank]=output;
                        headers.Promote("Header");headers.SetBlob(port,"Header",AwbIndex.Read(current).CacheHeader);
                        slots.Promote("Hash");using(var f=File.OpenRead(current))slots.SetBlob(port,"Hash",MD5.HashData(f));
                    }
                    else if(parts[0]=="repack-awb")
                    {
                        using(var f=File.OpenRead(original))sourceBankHashes[original]=SHA256.HashData(f);
                        using var input=new AwbReader(File.OpenRead(current));var index=AwbIndex.Read(current);string output=Path.Combine(stage,bank);
                        using(var f=File.Create(output))AwbWriter.Write(f,input.Waves.Select(w=>new AwbWriter.Entry(checked((ushort)w.WaveId),w.Length,()=>input.GetWaveSubfileStream(w))).ToArray(),index.Alignment,index.Subkey);
                        using var reread=new AwbReader(File.OpenRead(output));
                        if(!input.Waves.Select(w=>w.WaveId).SequenceEqual(reread.Waves.Select(w=>w.WaveId)))throw new InvalidDataException("Repacked AWB IDs changed.");
                        foreach(var entry in input.Waves){using var a=input.GetWaveSubfileStream(entry);using var b=reread.GetWaveSubfileStream(reread.Waves.Single(w=>w.WaveId==entry.WaveId));if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))throw new InvalidDataException("Repacked AWB payload changed.");}
                        current=output;stagedBanks[bank]=output;
                        headers.Promote("Header");headers.SetBlob(port,"Header",AwbIndex.Read(current).CacheHeader);
                        slots.Promote("Hash");using(var f=File.OpenRead(current))slots.SetBlob(port,"Hash",MD5.HashData(f));
                    }
                    else if(parts[0]=="refresh-awb-header"){headers.Promote("Header");headers.SetBlob(port,"Header",AwbIndex.Read(current).CacheHeader);}
                    else if(parts[0]=="refresh-awb-hash")
                    {
                        using(var f=File.OpenRead(original))sourceBankHashes[original]=SHA256.HashData(f);
                        slots.Promote("Hash");using var f2=File.OpenRead(current);slots.SetBlob(port,"Hash",MD5.HashData(f2));
                    }
                    else throw new ArgumentException("Unknown file-dependent repair.");
                    top.SetBlob(0,"StreamAwbHash",slots.Write());top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());
                }
                if(request.Actions.Contains("sort-cue-names"))CueSheetMaintenance.SortCueNames(top);
                if(request.Actions.Contains("sort-stream-slots"))CueSheetMaintenance.SortStreamSlots(top);
                if(request.Actions.Contains("rebuild-stream-work"))CueSheetMaintenance.RebuildStreamWork(top,request.WorkBytes);
                int? slotsOverride=request.Actions.Contains("sync-num-slots")?CueSheetMaintenance.StreamCount(top):null;
                string sheetOutput=Path.Combine(stage,Path.GetFileName(target.Path));CueSheetWriter.Embed(target.Path,top.Write(),sheetOutput,normalize:false,numSlots:slotsOverride);
                string ResolveStaged(string bank)=>stagedBanks.GetValueOrDefault(bank)??resolve(bank);
                var staged=AudioIntegrityInspection.Read(sheetOutput,ResolveStaged,false,cancellation);
                foreach(string action in request.Actions)
                {
                    if(!action.Contains(':') && staged.Issues.Any(i=>i.Repair==action))throw new InvalidDataException("Repair verification failed: "+action);
                    if(action.StartsWith("refresh-awb-header:",StringComparison.Ordinal)||action.StartsWith("repack-awb:",StringComparison.Ordinal)||action.StartsWith("repair-legacy-awb:",StringComparison.Ordinal))
                    {
                        string name=before.Streams.Single(s=>s.Port==int.Parse(action.Split(':')[1])).Name;
                        if(staged.Streams.Single(s=>s.Name==name).HeaderMatches!=true)throw new InvalidDataException("Bank header repair readback failed: "+name);
                    }
                    if(action.StartsWith("refresh-awb-hash:",StringComparison.Ordinal)||action.StartsWith("repair-legacy-awb:",StringComparison.Ordinal))
                    {
                        int oldPort=int.Parse(action.Split(':')[1]);string bank=before.Streams.Single(s=>s.Port==oldPort).Name+".awb";
                        var outputTop=CriUtf.Read(CueSheetWriter.Extract(new UAsset(sheetOutput,EngineVersion.VER_UE4_19)));var slots=CriUtf.Read(outputTop.Blob(0,"StreamAwbHash"));int port=Enumerable.Range(0,slots.Rows.Count).Single(i=>slots.Text(i,"Name")==Path.GetFileNameWithoutExtension(bank));
                        using var f=File.OpenRead(ResolveStaged(bank));if(!slots.Blob(port,"Hash").SequenceEqual(MD5.HashData(f)))throw new InvalidDataException("Hash repair readback failed.");
                    }
                }
                if(AudioIntegrityInspection.Read(target.Path,resolve,false,cancellation).Revision!=before.Revision)throw new InvalidOperationException("Source files changed during repair; draft was not written.");
                foreach(var snapshot in sourceBankHashes){using var f=File.OpenRead(snapshot.Key);if(!SHA256.HashData(f).SequenceEqual(snapshot.Value))throw new InvalidOperationException("Bank payload changed during repair; draft was not written.");}
                cancellation.ThrowIfCancellationRequested();
                var files=Directory.GetFiles(stage).Select(f=>(Source:f,Destination:f.EndsWith(".awb",StringComparison.OrdinalIgnoreCase)
                    ?Path.Combine(resources.DraftRoot(id),BankRelative(target,Path.GetFileName(f)))
                    :Path.Combine(resources.DraftRoot(id),Path.GetDirectoryName(target.Relative)??"",Path.GetFileName(f)))).ToArray();
                FileTransaction.Copy(files,false);
                // After commit, cancellation cannot skip readback. Read exactly the project draft targets.
                foreach(var file in files){using var a=File.OpenRead(file.Source);using var b=File.OpenRead(file.Destination);if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))throw new IOException("Draft repair write readback failed.");}
                var afterTarget=resources.AudioTarget(id,target.Relative,"project");var after=AudioIntegrityInspection.Read(afterTarget.Path,Resolver(id,afterTarget,"project"));
                return new(request.Actions,files.Select(f=>Path.GetRelativePath(resources.DraftRoot(id),f.Destination).Replace('\\','/')).ToArray(),before,after);
            }
            finally{Directory.Delete(stage,true);}
        }
    }
}
