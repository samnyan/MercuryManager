using System.Net;
using MercuryManager.Server;

var builder = WebApplication.CreateBuilder(StartupOptions.NormalizeArguments(args));
var startup = StartupOptions.FromConfiguration(builder.Configuration);
builder.WebHost.UseUrls(startup.ListenUrl);
builder.Services.AddSingleton<MusicWorkspaceStore>();
builder.Services.AddSingleton<MessageWorkspaceStore>();
builder.Services.AddSingleton<ProjectManager>();
builder.Services.AddSingleton<TexturePreviewService>();
builder.Services.AddSingleton<ResourceService>();
var accessPolicy = new LocalAccessPolicy(builder.Configuration);
var app = builder.Build();
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
app.MapGet("/api/projects/{id}/resources",(string id,string? directory,ResourceService r)=>r.List(id,directory??""));
app.MapGet("/api/projects/{id}/resource-data",(string id,string path,ResourceService r)=>GenericResourceViewer.Read(r.Resolve(id,path)));
app.MapGet("/api/projects/{id}/resource-raw",(string id,string path,int exportIndex,ResourceService r)=>Results.File(GenericResourceViewer.ReadRaw(r.Resolve(id,path),exportIndex),"application/octet-stream",$"{Path.GetFileNameWithoutExtension(path)}.export-{exportIndex}.bin"));
app.MapGet("/api/projects/{id}/resource-cues",(string id,string path,ResourceService r)=>{var target=CriAudio.Target(r.Resolve(id,path),p=>r.Resolve(id,p));return CriAudio.List(target.Path).Where(c=>target.CueName is null||c.Name==target.CueName).ToArray();});
app.MapGet("/api/projects/{id}/resource-audio",(HttpContext context,string id,string path,int index,int? part,ResourceService r)=>{var target=CriAudio.Target(r.Resolve(id,path),p=>r.Resolve(id,p));if(target.CueName is not null&&!CriAudio.List(target.Path).Any(c=>c.Index==index&&c.Name==target.CueName))throw new ArgumentException("Cue does not match asset reference.");return CriAudio.Play(context,target.Path,index,part??0);});
app.MapGet("/api/projects/{id}/resource-info",(string id,string path,ResourceService r)=>r.Info(id,path));
app.MapGet("/api/projects/{id}/resource-image",(string id,string path,ResourceService r)=>Results.File(TextureAuthoring.Preview(r.Resolve(id,path)),"image/png"));
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
app.MapGet("/api/workspaces/{id}/tables", (string id, MessageWorkspaceStore store) => store.Tables(id));
app.MapGet("/api/workspaces/{id}/messages", (string id, MessageWorkspaceStore store) => store.List(id));
app.MapGet("/api/workspaces/{id}/messages/{name}", (string id, string name, MessageWorkspaceStore store) => store.Read(id,name));
app.MapPost("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store) => {store.Edit(id,name,request.RowName,request.Fields,true);return Results.Ok();});
app.MapPatch("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store) => {store.Edit(id,name,request.RowName,request.Fields,false);return Results.Ok();});
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
