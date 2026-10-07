using Microsoft.AspNetCore.StaticFiles;
namespace MercuryManager.Server;

/// <summary>Serve the built SPA directly from assembly resources, independent of working directory.</summary>
public static class EmbeddedFrontend
{
    public static void MapEmbeddedFrontend(this WebApplication app)
    {
        var assembly=typeof(EmbeddedFrontend).Assembly;
        var names=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Frontend/",StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var types=new FileExtensionContentTypeProvider();
        app.MapGet("/{**path}", async (HttpContext context,string? path)=>
        {
            if(context.Request.Path.StartsWithSegments("/api")){context.Response.StatusCode=404;return;}
            var resource="Frontend/"+(string.IsNullOrEmpty(path)?"index.html":path);
            if(!names.Contains(resource))
            {
                if(Path.HasExtension(path)){context.Response.StatusCode=404;return;}
                resource="Frontend/index.html";
            }
            await using var stream=assembly.GetManifestResourceStream(resource);
            if(stream is null){context.Response.StatusCode=404;return;}
            context.Response.ContentType=types.TryGetContentType(resource,out var type)?type:"application/octet-stream";
            await stream.CopyToAsync(context.Response.Body,context.RequestAborted);
        });
    }
}
