using System.Collections.Concurrent;
using System.Text.Json;
namespace MercuryManager.Server;
public sealed record AudioAction(string Operation,string Bank,int CueId=0,ushort WaveId=0,int TemplateId=88,string? Name=null,string? UploadId=null,string? SpeakerId=null,string? HeadphoneId=null,WaveformLoopEdit[]? Extensions=null);
public sealed record AudioApplyRequest(AudioAction[] Actions);
public sealed class AudioJobs(MusicWorkspaceStore assets,ResourceService resources,ILogger<AudioJobs> logger)
{
    public sealed record Progress(int Percent,string Stage,bool Done=false,string? Error=null);
    private sealed class Job{public string Owner="";public Progress State=new(0,"queued");}
    private readonly ConcurrentDictionary<string,Job> jobs=[];
    private readonly ConcurrentDictionary<string,byte> active=[];
    string UploadRoot(string id)=>Path.Combine(assets.WorkspaceDirectory(id),"AudioUploads");
    public async Task<object> Upload(string id,HttpRequest request)
    {
        assets.Get(id);var root=UploadRoot(id);MusicWorkspaceStore.RejectLinks(root);Directory.CreateDirectory(root);string token=Guid.NewGuid().ToString("N"),file=Path.Combine(root,token);long total=0;
        try{await using var output=File.Create(file);var buffer=new byte[65536];int n;while((n=await request.Body.ReadAsync(buffer,request.HttpContext.RequestAborted))>0){total+=n;if(total>128*1024*1024)throw new ArgumentException("Audio upload limit: 128 MiB.");await output.WriteAsync(buffer.AsMemory(0,n));}if(total==0)throw new ArgumentException("Empty upload.");return new{uploadId=token,size=total};}catch{File.Delete(file);throw;}
    }
    public object Start(string id,string path,AudioAction[] actions)
    {
        if(actions is null || actions.Length is <1 or >100)throw new ArgumentException("Apply requires 1–100 actions.");
        foreach(var action in actions)
        {
            if(action is null || action.Operation is not ("add" or "delete" or "replace"))throw new ArgumentException("Unknown action.");
            if(string.IsNullOrWhiteSpace(action.Bank)||action.Bank!=Path.GetFileName(action.Bank)||action.Bank.Contains('\\'))throw new ArgumentException("Invalid bank.");
            if(action.Operation=="add" && (action.CueId<0||string.IsNullOrWhiteSpace(action.Name)))throw new ArgumentException("Cue name and nonnegative ID required.");
            var tokens=action.Operation switch {"add"=>new[]{action.SpeakerId,action.HeadphoneId},"replace"=>new[]{action.UploadId},_=>Array.Empty<string?>()};
            foreach(var upload in tokens)
            {
                if(upload is null||!Guid.TryParseExact(upload,"N",out _))throw new ArgumentException("Invalid upload reference.");
                var file=Path.Combine(UploadRoot(id),upload);MusicWorkspaceStore.RejectLinks(file);
                if(!File.Exists(file))throw new FileNotFoundException("Audio upload missing; upload the file again.");
            }
        }
        resources.Resolve(id,path);var key=id;if(!active.TryAdd(key,0))throw new InvalidOperationException("Audio apply already running.");
        string token=Guid.NewGuid().ToString("N");var job=new Job{Owner=id};jobs[token]=job;
        _=Task.Run(()=>{try{Run(id,path,actions,(p,s)=>job.State=new(p,s));job.State=new(100,"complete",true);}catch(Exception ex){logger.LogError(ex,"Audio job {JobId} failed for project {ProjectId} at {Stage}",token,id,job.State.Stage);job.State=new(job.State.Percent,"failed",true,ex.Message);}finally{active.TryRemove(key,out _);}});return new{jobId=token};
    }
    public async Task Events(string id,string token,HttpContext context)
    {
        if(!jobs.TryGetValue(token,out var job)||job.Owner!=id)throw new FileNotFoundException("Job missing.");context.Response.ContentType="text/event-stream";context.Response.Headers.CacheControl="no-cache";
        while(!context.RequestAborted.IsCancellationRequested){var state=job.State;await context.Response.WriteAsync("data: "+JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))+"\n\n",context.RequestAborted);await context.Response.Body.FlushAsync(context.RequestAborted);if(state.Done)break;await Task.Delay(250,context.RequestAborted);}
    }
    void Run(string id,string path,AudioAction[] actions,Action<int,string> report)
    {
        lock(assets){string relative=path;var source=CriAudio.Target(resources.Resolve(id,path),p=>{relative=p;return resources.Resolve(id,p);}).Path;var stage=Path.Combine(assets.WorkspaceDirectory(id),"audio-batch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        using var batch=new AudioBatchContext();var encoded=new Dictionary<string,string>();
        string Input(string? token){if(token==null||!Guid.TryParseExact(token,"N",out _))throw new ArgumentException("Invalid upload reference.");if(encoded.TryGetValue(token,out var cached))return cached;string f=Path.Combine(UploadRoot(id),token);MusicWorkspaceStore.RejectLinks(f);var bytes=File.ReadAllBytes(f);if(bytes.AsSpan(0,Math.Min(4,bytes.Length)).SequenceEqual("RIFF"u8))bytes=HcaEncoding.Encode(bytes);string output=Path.Combine(stage,token+".hca");File.WriteAllBytes(output,bytes);return encoded[token]=output;}
        try{CriCueEditor.Batch=batch;string current=source;for(int i=0;i<actions.Length;i++){var a=actions[i];if(a.Bank!=Path.GetFileName(a.Bank)||a.Bank.Contains('\\'))throw new ArgumentException("Invalid bank.");string bank=resources.Resolve(id,((Path.GetDirectoryName(relative)??"")+"/"+a.Bank).TrimStart('/'));report(5+i*35/actions.Length,"transcode:"+(i+1));string output=Path.Combine(stage,"step-"+i);try{switch(a.Operation){case "add":CriCueEditor.Add(current,bank,a.TemplateId,a.CueId,a.Name??"",Input(a.SpeakerId),Input(a.HeadphoneId),output);break;case "delete":CriCueEditor.Delete(current,bank,a.CueId,output);break;case "replace":CriAudioEditor.Replace(current,bank,a.WaveId,Input(a.UploadId),output,a.Extensions);break;default:throw new ArgumentException("Unknown action.");}}catch(Exception ex){throw new InvalidDataException($"Action {i+1}/{actions.Length}: {a.Operation}, bank {a.Bank}, Cue {a.CueId}, Wave {a.WaveId}: {ex.Message}",ex);}current=Path.Combine(output,Path.GetFileName(source));}
        CriCueEditor.Batch=null;string final=Path.Combine(stage,"final");Directory.CreateDirectory(final);batch.Finish(current,final,report);report(95,"publish");FileTransaction.Copy(Directory.GetFiles(final).Select(f=>(Source:f,Destination:Path.Combine(resources.DraftRoot(id),Path.GetDirectoryName(relative)??"",Path.GetFileName(f)))).ToArray(),false);
        }finally{CriCueEditor.Batch=null;Directory.Delete(stage,true);}}
    }
}
