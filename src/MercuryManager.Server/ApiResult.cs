using System.Text.Json;
namespace MercuryManager.Server;

public sealed record ApiResult(int Code, string Message, object? Data)
{
    public static ApiResult Ok(object? data = null) => new(0, "ok", data);
    public static ApiResult Error(int code, string message) => new(code, message, null);
}

public static class ApiResultMiddleware
{
    public static void UseApiResults(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            ApiResult? failure = null;
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Response.Body = original; return; }
            catch (Exception ex)
            {
                var status = ex switch
                {
                    BadHttpRequestException bad => bad.StatusCode,
                    FileNotFoundException or DirectoryNotFoundException => 404,
                    ArgumentException or InvalidDataException or JsonException or FormatException or OverflowException or InvalidOperationException => 400,
                    UnauthorizedAccessException => 403,
                    IOException => 409,
                    _ => 500
                };
                app.Logger.LogError(ex, "API request failed: {Method} {Path}, trace {TraceId}", context.Request.Method, context.Request.Path, context.TraceIdentifier);
                context.Response.StatusCode = status;
                failure = ApiResult.Error(status, status == 500 ? "服务器内部错误，请查看服务日志（请求 ID：" + context.TraceIdentifier + "）" : ex.Message);
            }
            finally { context.Response.Body = original; }
            if (context.RequestAborted.IsCancellationRequested) return;
            if(failure is null && context.Response.StatusCode<400 && context.Response.ContentType is "image/png" or "application/octet-stream")
            {buffer.Position=0;await buffer.CopyToAsync(original);return;}
            var statusCode = context.Response.StatusCode;
            object? data = null;
            if (failure is null && buffer.Length > 0)
            {
                buffer.Position = 0;
                try
                {
                    using var document = await JsonDocument.ParseAsync(buffer);
                    if (statusCode < 400) data = document.RootElement.Clone();
                    else if (document.RootElement.TryGetProperty("title", out var title)) failure = ApiResult.Error(statusCode, title.GetString() ?? "请求失败");
                }
                catch (JsonException) { if (statusCode < 400) { context.Response.StatusCode = 500; failure = ApiResult.Error(500, "服务器响应格式错误"); } }
            }
            if (statusCode >= 400 && failure is null)
                failure = ApiResult.Error(statusCode, statusCode switch { 400 => "请求参数或 JSON 格式错误", 403 => "请求被拒绝：请检查访问地址、来源和局域网限制", 404 => "资源不存在", 405 => "请求方法不支持", _ => "请求失败（HTTP " + statusCode + "）" });
            if (statusCode == 204) context.Response.StatusCode = 200;
            context.Response.ContentLength = null;
            await context.Response.WriteAsJsonAsync(failure ?? ApiResult.Ok(data));
        });
    }
}
