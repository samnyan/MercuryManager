using System.Net;
using MercuryManager.Server;

var builder = WebApplication.CreateBuilder(StartupOptions.NormalizeArguments(args));
var startup = StartupOptions.FromConfiguration(builder.Configuration);
builder.WebHost.UseUrls(startup.ListenUrl);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=360L*1024*1024);
builder.Services.AddSingleton<MusicWorkspaceStore>();
builder.Services.AddSingleton<MessageWorkspaceStore>();
builder.Services.AddSingleton<GameContent>();
builder.Services.AddSingleton<ProjectManager>();
builder.Services.AddSingleton<TexturePreviewService>();
builder.Services.AddSingleton<ResourceService>();
builder.Services.AddSingleton<BlueprintService>();
builder.Services.AddResponseCompression(options=>{options.EnableForHttps=true;options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();});
builder.Services.AddSingleton<AudioJobs>();
builder.Services.AddSingleton<AudioIntegrityService>();
builder.Services.AddSingleton<VoiceLookupService>();
var accessPolicy = new LocalAccessPolicy(builder.Configuration);
var app = builder.Build();
app.UseResponseCompression();
app.UseApiResults();
app.Use(async (context, next) =>
{
    if (!accessPolicy.Allows(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = 403;
        return;
    }
    var localIp = context.Connection.LocalIpAddress;
    var isAnyLocal = localIp is null || IPAddress.Any.Equals(localIp) || IPAddress.IPv6Any.Equals(localIp);
    if (!isAnyLocal)
    {
        var expectedHost = new HostString(localIp!.ToString(), context.Connection.LocalPort).Value;
        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Host.Value != expectedHost ||
            (origin.Length > 0 && origin != $"http://{expectedHost}"))
        {
            context.Response.StatusCode = 403;
            return;
        }
    }
    if (context.Request.Method != "GET" && context.Request.Method != "HEAD" && context.Request.Headers["X-Mercury-Local"] != "1")
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.MapGet("/api/projects/{id}/texture", (string id,string table,string field,string value,TexturePreviewService textures)=>Results.File(textures.Load(id,table,field,value),"image/png"));
app.MapGet("/api/projects/{id}/resource-sources",(string id,string path,ResourceService r)=>r.Sources(id,path));
app.MapGet("/api/projects/{id}/resources",(string id,string? directory,ResourceService r)=>r.List(id,directory??""));
app.MapGet("/api/projects/{id}/resource-font",(string id,string path,ResourceService r,BlueprintService b)=>b.Font(r.Resolve(id,path)));
app.MapGet("/api/projects/{id}/resource-umg",(string id,string path,ResourceService r,BlueprintService b)=>
{
    var doc = UMGDependencyBuilder.Expand(b.UMG(r.Resolve(id,path),path),dependency=>b.UMG(r.Resolve(id,dependency),dependency));
    var matPaths = doc.Nodes.SelectMany(n => n.Resources)
        .Where(res => res.ClassName is "Material" or "MaterialInstanceConstant" or "MaterialInstance")
        .Select(res => res.AssetPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    var matDict = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
    foreach (var mp in matPaths)
    {
        try
        {
            var resolved = r.Resolve(id, mp);
            var mData = GenericResourceViewer.ReadMaterialData(resolved, p => r.Resolve(id, p));
            if (mData != null)
            {
                var elem = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(mData.ToString(Newtonsoft.Json.Formatting.None));
                matDict[mp] = elem;
            }
        }
        catch {}
    }

    var particlePaths = doc.Nodes.SelectMany(n => n.Resources)
        .Where(res => res.ClassName == "ParticleSystem" || res.AssetPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && res.AssetPath.Contains("Particle", StringComparison.OrdinalIgnoreCase))
        .Select(res => res.AssetPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    var particleDict = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
    foreach (var pp in particlePaths)
    {
        try
        {
            var resolved = r.Resolve(id, pp);
            var pData = GenericResourceViewer.ReadParticleSystemData(resolved, p => r.Resolve(id, p));
            if (pData != null)
            {
                var elem = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(pData.ToString(Newtonsoft.Json.Formatting.None));
                particleDict[pp] = elem;
            }
        }
        catch {}
    }

    return doc with { Materials = matDict, Particles = particleDict };
});
app.MapGet("/api/projects/{id}/resource-blueprint",(string id,string path,ResourceService r,BlueprintService b)=>b.Read(r.Resolve(id,path)));
app.MapGet("/api/projects/{id}/resource-blueprint-node",(string id,string path,int index,ResourceService r,BlueprintService b)=>b.Node(r.Resolve(id,path),index));
app.MapGet("/api/projects/{id}/resource-data",(string id,string path,ResourceService r)=>Results.Ok(GenericResourceViewer.Read(r.Resolve(id,path),p=>r.Resolve(id,p))));
app.MapGet("/api/projects/{id}/resource-raw",(string id,string path,int exportIndex,ResourceService r)=>Results.File(GenericResourceViewer.ReadRaw(r.Resolve(id,path),exportIndex),"application/octet-stream",$"{Path.GetFileNameWithoutExtension(path)}.export-{exportIndex}.bin"));
app.MapGet("/api/projects/{id}/audio-banks",(string id,string path,string? source,ResourceService r)=>{var target=r.AudioTarget(id,path,source);return target.Path.EndsWith(".awb",StringComparison.OrdinalIgnoreCase)?new[]{Path.GetFileName(target.Path)}:CriAudio.Banks(target.Path);});
app.MapGet("/api/projects/{id}/audio-inspection",(string id,string path,string? source,bool? deep,HttpContext context,AudioIntegrityService inspection)=>Task.Run(()=>inspection.Inspect(id,path,source,deep==true,context.RequestAborted),context.RequestAborted));
app.MapPost("/api/projects/{id}/audio-repair",(string id,string path,string? source,AudioRepairRequest request,HttpContext context,AudioIntegrityService inspection)=>Task.Run(()=>inspection.Repair(id,path,source,request,context.RequestAborted),context.RequestAborted));
app.MapGet("/api/projects/{id}/audio-wave-info",(string id,string path,string? source,string bank,ushort waveId,ResourceService r)=>{AudioBanks.ValidateName(bank);var target=r.AudioTarget(id,path,source);return CueMetadataEditor.Inspect(r.Resolve(id,ResourceService.Sibling(target.Relative,bank),source),waveId);});
app.MapGet("/api/projects/{id}/audio-metadata",(string id,string path,string? source,int cueId,ResourceService r)=>{var target=r.AudioTarget(id,path,source);return CueMetadataEditor.Describe(target.Path,cueId);});
app.MapGet("/api/projects/{id}/audio-extension",(string id,string path,string? source,string bank,ushort waveId,ResourceService r)=>{var target=r.AudioTarget(id,path,source);return WaveformExtensions.Describe(target.Path,bank,waveId);});
app.MapGet("/api/projects/{id}/resource-audio-details",(string id,string path,string? source,int index,int? part,ResourceService r)=>{var target=r.AudioTarget(id,path,source);if(target.CueName is not null&&!CriAudio.List(target.Path).Any(c=>c.Index==index&&c.Name==target.CueName))throw new ArgumentException("Cue does not match asset reference.");return CriAudio.Details(target.Path,index,part??0,bank=>r.Resolve(id,ResourceService.Sibling(target.Relative,bank),source));});
app.MapGet("/api/projects/{id}/resource-cues",(string id,string path,string? source,bool? all,ResourceService r)=>{var target=r.AudioTarget(id,path,source);return CriAudio.List(target.Path).Where(c=>all==true||target.CueName is null||c.Name==target.CueName).ToArray();});
app.MapGet("/api/projects/{id}/resource-audio",(HttpContext context,string id,string path,string? source,int index,int? part,ResourceService r)=>{var target=r.AudioTarget(id,path,source);if(target.CueName is not null&&!CriAudio.List(target.Path).Any(c=>c.Index==index&&c.Name==target.CueName))throw new ArgumentException("Cue does not match asset reference.");return CriAudio.Play(context,target.Path,index,part??0,bank=>r.Resolve(id,ResourceService.Sibling(target.Relative,bank),source));});
app.MapGet("/api/projects/{id}/resource-audio-export",(HttpContext context,string id,string path,string? source,int index,int? part,string format,ResourceService r)=>{var target=r.AudioTarget(id,path,source);if(target.CueName is not null&&!CriAudio.List(target.Path).Any(c=>c.Index==index&&c.Name==target.CueName))throw new ArgumentException("Cue does not match asset reference.");return CriAudio.Export(context,target.Path,index,part??0,format,bank=>r.Resolve(id,ResourceService.Sibling(target.Relative,bank),source));});
app.MapGet("/api/projects/{id}/resource-json",(string id,string path,ResourceService r)=>Results.File(System.Text.Encoding.UTF8.GetBytes(ResourceJson.Export(r.Resolve(id,path))),"application/octet-stream",Path.GetFileNameWithoutExtension(path)+".json"));
app.MapPost("/api/projects/{id}/resource-json",(string id,string path,ImportResourceJsonRequest request,ResourceService r)=>r.ImportJson(id,path,request.Json));
app.MapPost("/api/projects/{id}/audio-upload",(string id,HttpRequest request,AudioJobs jobs)=>jobs.Upload(id,request));
app.MapPost("/api/projects/{id}/audio-apply",(string id,string path,string? source,AudioApplyRequest request,AudioJobs jobs)=>jobs.Start(id,path,request.Actions,source));
app.MapGet("/api/projects/{id}/audio-events",(string id,string jobId,HttpContext context,AudioJobs jobs)=>jobs.Events(id,jobId,context));
app.MapPost("/api/projects/{id}/resource-cue-edit",(string id,string path,CueEditRequest request,ResourceService r)=>r.EditCue(id,path,request));
app.MapPost("/api/projects/{id}/resource-audio-replace",(string id,string path,ReplaceAudioRequest request,ResourceService r)=>r.ReplaceAudio(id,path,request.Bank,request.WaveId,Convert.FromBase64String(request.HcaBase64)));
app.MapGet("/api/projects/{id}/resource-text",(string id,string path,string? source,ResourceService r)=>r.ReadText(id,path,source));
app.MapPost("/api/projects/{id}/resource-text",(string id,string path,SaveTextRequest request,ResourceService r)=>r.SaveText(id,path,request.Content));
app.MapPost("/api/projects/{id}/resource-material-params",(string id,string path,SaveMaterialParamsRequest request,ResourceService r)=>r.SaveMaterialParams(id,path,request));
app.MapGet("/api/projects/{id}/resource-material-shader", async (string id, string path, float? time, float? previousTime, string? scalarOverrides, string? vectorOverrides, string? collectionOverrides, ResourceService r) =>
{
    float now = time ?? 0, before = previousTime ?? now;
    if (!float.IsFinite(before)) return Results.BadRequest("Material time must be finite");
    var collectionValues = LegacyMaterialCollectionInputs.ParseOverrides(collectionOverrides);
    var context = MaterialPreviewOverrides.Parse(scalarOverrides, vectorOverrides, now, now);
    var resolved = r.Resolve(id, path);
    try
    {
    var preview = LegacyMaterialInstanceResolver.Resolve(resolved, dependency => r.ResolveMaterialDependency(id, dependency), context);
    var asset = preview.Asset;
    var matExport = preview.Material;
    context = preview.Context;
    resolved = preview.Path;
    var cooked = LegacyMaterialUniformReader.ReadCookedMaterial(asset, matExport);
    if (cooked == null) return Results.NotFound("Cooked material shader data not found");
    var candidates = MaterialShaderCompiler.Discover(resolved);
    var psCandidate = LegacySlateProgramSelector.SelectDefaultFalse(candidates, cooked.Shaders);
    var cacheDir = Path.Combine(Path.GetTempPath(), "mercury-material-cache");
    var translation = await MaterialShaderCompiler.Translate(psCandidate, cacheDir);
    var psEntry = cooked.Shaders.First(s => s.Type == "TSlateMaterialShaderPSDefaultfalse");
    var bindings = LegacySlateShaderBindings.ReadDefaultFalse(matExport.Extras!, psEntry);
    var constants = LegacyMaterialUniformPacker.PackNumericConstants(cooked.Uniforms, bindings.Material, context);
    var uploadConstants = LegacySlateProgramSelector.PrepareMaterialBuffer(translation.Reflection, bindings.Material, constants);
    var frameWrites = LegacyMaterialFrameEvaluator.Evaluate(cooked.Uniforms, bindings, context,
        context with { Time = before, RealTime = before });
    LegacySlateProgramSelector.ValidateFrameWrites(translation.Reflection, bindings.Material, uploadConstants.Length, frameWrites);
    IReadOnlyList<string> textureTable = cooked.ReferencedTextures;
    IReadOnlyList<LegacyMaterialTextureExpression> textureExpressions = [];
    IReadOnlyList<LegacyMaterialSamplerSlot> samplerSlots = [];
    string? textureMappingError = null;
    try
    {
        if (textureTable.Count == 0) textureTable = LegacyMaterialTextureTable.Read(asset, matExport,
            dependency => new UAssetAPI.UAsset(r.ResolveMaterialDependency(id, dependency), UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19));
        textureExpressions = LegacyMaterialTextureResolver.Resolve2D(cooked.Uniforms, textureTable, preview.TextureOverrides);
        samplerSlots = LegacyMaterialSamplerMapping.Resolve(LegacyShaderResourceTable.Read(psCandidate.ResourceTablePrefix),
            bindings.Material, textureExpressions, translation.CombinedSamplers);
    }
    catch (NotSupportedException ex) { textureMappingError = ex.Message; }
    var limitations = new List<string>();
    if (textureMappingError != null) limitations.Add(textureMappingError);
    IReadOnlyList<LegacyMaterialCollectionInput> collectionInputs = [];
    IReadOnlyList<LegacySlateBufferInput> sceneInputs = [];
    try
    {
        collectionInputs = LegacyMaterialCollectionInputs.Evaluate(asset, matExport, cooked.Uniforms, bindings,
            translation.Reflection, dependency => r.ResolveMaterialDependency(id, dependency), collectionValues);
        var collectionBuffers = collectionInputs.Select(input => new LegacySlateBufferInput(input.BufferIndex, input.Data)).ToArray();
        sceneInputs = collectionBuffers.Concat(LegacySlateTimeInputs.Evaluate(translation.Reflection, translation.Glsl,
            bindings, now, now, collectionBuffers)).ToArray();
        LegacySlatePreviewInputs.Validate(translation.Reflection, bindings, sceneInputs);
    }
    catch (NotSupportedException ex) { limitations.Add(ex.Message); }
    var blendMode = (matExport.Data.FirstOrDefault(p => p.Name.ToString() == "BlendMode") as UAssetAPI.PropertyTypes.Objects.BytePropertyData)?.EnumValue?.ToString()?.Split("::").Last() ?? "BLEND_Opaque";
    if (blendMode is not ("BLEND_Opaque" or "BLEND_Masked" or "BLEND_Translucent" or "BLEND_Additive"))
        limitations.Add("Destination-dependent blend modes are not supported by isolated rectangle preview.");
    using var reflection = System.Text.Json.JsonDocument.Parse(translation.Reflection);
    return Results.Ok(new
    {
        schemaVersion = 1,
        glsl = translation.Glsl,
        combinedSamplers = translation.CombinedSamplers,
        reflection = reflection.RootElement.Clone(),
        materialBindings = bindings.Material,
        materialConstants = uploadConstants,
        nativeMaterialConstantBytes = constants.Length,
        frameWrites,
        uniformProgram = MaterialRuntimeProgramBuilder.Build(cooked.Uniforms, bindings, context, uploadConstants.Length),
        sceneInputs,
        // Evaluate has already verified the time-only layout and read profile.
        timeInputs = sceneInputs.Where(input => !collectionInputs.Any(c => c.BufferIndex == input.BufferIndex))
            .Select(input => new { input.BufferIndex, gameTimeOffset = 2120, realTimeOffset = 2124 }).ToArray(),
        collectionInputs,
        slateBindings = bindings,
        time = now,
        previousTime = before,
        referencedTextures = textureTable,
        textureExpressions,
        samplerSlots,
        textureMappingReady = textureMappingError == null,
        textureMappingError,
        blendMode,
        previewProfile = "slate-rectangle-v1",
        renderingReady = limitations.Count == 0,
        limitations
    });
    }
    catch (NotSupportedException ex)
    {
        // An unsupported cooked capability is a preview diagnostic, not a server failure.
        return Results.Ok(new
        {
            schemaVersion = 1,
            renderingReady = false,
            previewProfile = "slate-rectangle-v1",
            limitations = new[] { ex.Message }
        });
    }
});
app.MapPost("/api/projects/{id}/resource-umg-widget",(string id,string path,SaveWidgetPropertyRequest request,ResourceService r)=>r.SaveWidgetProperty(id,path,request));
app.MapGet("/api/projects/{id}/resource-info",(string id,string path,ResourceService r)=>r.Info(id,path));
app.MapGet("/api/projects/{id}/resource-image",(string id,string path,int? mip,ResourceService r)=>Results.File(TextureAuthoring.Preview(r.ResolveMaterialDependency(id,path),mip),"image/png"));
app.MapGet("/api/projects/{id}/resource-image-crop",(string id,string path,int x,int y,int w,int h,ResourceService r)=>Results.File(TextureAuthoring.PreviewCrop(r.Resolve(id,path),x,y,w,h),"image/png"));
app.MapPost("/api/projects/{id}/resources",(string id,BuildTextureRequest request,ResourceService r)=>r.Build(id,request.Template,request.Target,Convert.FromBase64String(request.ImageBase64)));
app.MapGet("/api/health", () => new { application = "MercuryManager", stage = "editor", profile = "ue4.19" });
app.MapGet("/api/projects", (ProjectManager p)=>p.List());
app.MapPost("/api/projects", (CreateProject request, ProjectManager p)=>p.Create(request.Name));
app.MapGet("/api/projects/{id}/export-files", (string id,ProjectManager p)=>p.ExportFiles(id));
app.MapGet("/api/projects/{id}", (string id,ProjectManager p)=>p.Status(id));
app.MapPost("/api/projects/{id}/import", (string id,OpenWorkspace request,ProjectManager p)=>p.Import(id,request.ServerPath));
app.MapPost("/api/projects/{id}/save", (string id,ProjectManager p)=>p.Save(id));
app.MapPost("/api/workspaces", (OpenWorkspace request, MusicWorkspaceStore store) => store.Open(request.ServerPath));
app.MapGet("/api/workspaces/{id}", (string id, MusicWorkspaceStore store) => store.Get(id));
app.MapGet("/api/workspaces/{id}/music", (string id, MusicWorkspaceStore store) => store.ReadRows(id));
app.MapGet("/api/workspaces/{id}/music/{rowName}", (string id, string rowName, MusicWorkspaceStore store) =>
{
    var row = store.ReadRows(id).SingleOrDefault(r => r.RowName == rowName);
    return row is null ? Results.NotFound() : Results.Ok(row);
});
app.MapPost("/api/workspaces/{id}/music/{rowName}", (string id, string rowName, Dictionary<string, System.Text.Json.JsonElement> changes, MusicWorkspaceStore store) => store.AddSong(id, rowName, changes));
app.MapPatch("/api/workspaces/{id}/music/{rowName}", (string id, string rowName, Dictionary<string, System.Text.Json.JsonElement> changes, MusicWorkspaceStore store) => store.Patch(id, rowName, changes));
app.MapPost("/api/workspaces/{id}/exports", (string id, MusicWorkspaceStore store) => store.Export(id));
app.MapGet("/api/workspaces/{id}/changes", (string id, MusicWorkspaceStore store, MessageWorkspaceStore messages, ResourceService resources) => store.ChangedFiles(id).Concat(messages.Changes(id)).Concat(resources.Changes(id)).ToArray());
app.MapPost("/api/workspaces/{id}/save", (string id, MusicWorkspaceStore store) => store.SaveProject(id));
app.MapPost("/api/workspaces/{id}/write", (string id, WriteRequest request, MusicWorkspaceStore store, MessageWorkspaceStore messages, ProjectManager projects, ResourceService resources) =>
{
    projects.RequireSaved(id);
    var written = projects.ExportBase(id,request.Mode,request.OutputDirectory).ToList();
    var music = store.WriteFiles(id, request.Mode, request.OutputDirectory, request.Backup);
    var json = System.Text.Json.JsonSerializer.SerializeToElement(music);
    foreach (var file in json.GetProperty("writtenFiles").EnumerateArray()) written.Add(file.GetString()!);
    written.AddRange(messages.Write(id, request.Mode, request.OutputDirectory, request.Backup));
    written.AddRange(resources.Write(id,request.Mode=="overwrite"?store.Get(id).ContentRoot!:request.OutputDirectory!,request.Backup));
    return new { writtenFiles = written.Distinct().ToArray() };
});
app.MapGet("/api/workspaces/{id}/voice-lookup", (string id, string? voiceId, string? voiceIds, VoiceLookupService voices) =>
{
    if (!string.IsNullOrWhiteSpace(voiceId))
    {
        return Results.Ok(voices.Lookup(id, voiceId));
    }
    if (!string.IsNullOrWhiteSpace(voiceIds))
    {
        var list = voiceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Results.Ok(voices.LookupBatch(id, list));
    }
    return Results.Ok(Array.Empty<VoiceMatch>());
});
app.MapGet("/api/workspaces/{id}/tables", (string id, MessageWorkspaceStore store) => store.Tables(id));
app.MapGet("/api/workspaces/{id}/messages", (string id, MessageWorkspaceStore store) => store.List(id));
app.MapGet("/api/workspaces/{id}/messages/{name}", (string id, string name, MessageWorkspaceStore store) => store.Read(id,name));
app.MapPost("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store, VoiceLookupService voices) => {store.Edit(id,name,request.RowName,request.Fields,true);voices.Invalidate(id);return Results.Ok();});
app.MapPatch("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store, VoiceLookupService voices) => {store.Edit(id,name,request.RowName,request.Fields,false);voices.Invalidate(id);return Results.Ok();});
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapEmbeddedFrontend();
if (startup.LaunchBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var url = StartupOptions.BrowserUrl(app.Urls.First());
            var start = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }
                : new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            if (!OperatingSystem.IsWindows()) start.ArgumentList.Add(url);
            using var process = System.Diagnostics.Process.Start(start);
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Could not open the browser. Open {Url} manually.", startup.ListenUrl);
        }
    });
}
app.Run();

public sealed record CreateProject(string Name);
public sealed record OpenWorkspace(string ServerPath);
public sealed record MessageEdit(string RowName, Dictionary<string,System.Text.Json.JsonElement> Fields);
public sealed record WriteRequest(string Mode, string? OutputDirectory, bool Backup = true);
