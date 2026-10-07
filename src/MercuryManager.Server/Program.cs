using MercuryManager.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["listen-url"] ?? "http://127.0.0.1:5087");
builder.Services.AddSingleton<MusicWorkspaceStore>();
builder.Services.AddSingleton<MessageWorkspaceStore>();
var accessPolicy = new LocalAccessPolicy(builder.Configuration);
var app = builder.Build();
app.UseApiResults();
app.Use(async (context, next) =>
{
    var expectedHost = $"{context.Connection.LocalIpAddress}:{context.Connection.LocalPort}";
    if (!accessPolicy.Allows(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = 403;
        return;
    }
    var origin = context.Request.Headers.Origin.ToString();
    if (context.Request.Host.Value != expectedHost ||
        (origin.Length > 0 && origin != $"http://{expectedHost}"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (context.Request.Method != "GET" && context.Request.Headers["X-Mercury-Local"] != "1")
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context);
});
app.MapGet("/api/health", () => new { application = "MercuryManager", stage = "editor", profile = "ue4.19" });
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
app.MapGet("/api/workspaces/{id}/changes", (string id, MusicWorkspaceStore store, MessageWorkspaceStore messages) => store.ChangedFiles(id).Concat(messages.Changes(id)).ToArray());
app.MapPost("/api/workspaces/{id}/save", (string id, MusicWorkspaceStore store) => store.SaveProject(id));
app.MapPost("/api/workspaces/{id}/write", (string id, WriteRequest request, MusicWorkspaceStore store, MessageWorkspaceStore messages) =>
{
    var written = new List<string>();
    var music = store.WriteFiles(id, request.Mode, request.OutputDirectory, request.Backup);
    var json = System.Text.Json.JsonSerializer.SerializeToElement(music);
    foreach (var file in json.GetProperty("writtenFiles").EnumerateArray()) written.Add(file.GetString()!);
    written.AddRange(messages.Write(id, request.Mode, request.OutputDirectory, request.Backup));
    return new { writtenFiles = written };
});
app.MapGet("/api/workspaces/{id}/tables", (string id, MessageWorkspaceStore store) => store.Tables(id));
app.MapGet("/api/workspaces/{id}/messages", (string id, MessageWorkspaceStore store) => store.List(id));
app.MapGet("/api/workspaces/{id}/messages/{name}", (string id, string name, MessageWorkspaceStore store) => store.Read(id,name));
app.MapPost("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store) => {store.Edit(id,name,request.RowName,request.Fields,true);return Results.Ok();});
app.MapPatch("/api/workspaces/{id}/messages/{name}/rows", (string id, string name, MessageEdit request, MessageWorkspaceStore store) => {store.Edit(id,name,request.RowName,request.Fields,false);return Results.Ok();});
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapEmbeddedFrontend();
app.Run();

public sealed record OpenWorkspace(string ServerPath);
public sealed record MessageEdit(string RowName, Dictionary<string,System.Text.Json.JsonElement> Fields);
public sealed record WriteRequest(string Mode, string? OutputDirectory, bool Backup = true);
