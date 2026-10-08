using System.Globalization;
using System.Net;

namespace MercuryManager.Server;

public sealed record StartupOptions(string ListenUrl, bool LaunchBrowser)
{
    public static string[] NormalizeArguments(string[] args) => args.Select(arg => arg switch
    {
        "--no-launch" => "--no-launch=true",
        "-port" => "--port",
        _ when arg.StartsWith("-port=", StringComparison.Ordinal) => "--port=" + arg[6..],
        _ => arg
    }).ToArray();

    public static StartupOptions FromConfiguration(IConfiguration configuration)
    {
        var noLaunch = configuration["no-launch"];
        if (noLaunch is not null && !bool.TryParse(noLaunch, out _))
            throw new ArgumentException("--no-launch must be true or false.");
        var launch = !bool.TryParse(noLaunch, out var disabled) || !disabled;
        var legacyUrl = configuration["listen-url"];
        if (legacyUrl is not null)
        {
            if (configuration["host"] is not null || configuration["port"] is not null)
                throw new ArgumentException("--listen-url cannot be combined with --host or --port.");
            return new StartupOptions(legacyUrl, launch);
        }

        var host = configuration["host"] ?? "127.0.0.1";
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";
        if (host is "*" or "+") host = "0.0.0.0";
        if (!IPAddress.TryParse(host, out var address))
            throw new ArgumentException("--host requires an IP address, localhost, or 0.0.0.0/*.");
        var portText = configuration["port"] ?? "5087";
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new ArgumentException("--port requires a number between 1 and 65535.");
        return new StartupOptions(new UriBuilder("http", address.ToString(), port).Uri.GetLeftPart(UriPartial.Authority), launch);
    }

    public static string BrowserUrl(string listeningUrl)
    {
        var uri = new UriBuilder(listeningUrl.Replace("://*:", "://0.0.0.0:").Replace("://+:", "://0.0.0.0:"));
        if (uri.Host is "0.0.0.0" or "::" or "[::]") uri.Host = "127.0.0.1";
        return uri.Uri.AbsoluteUri;
    }
}
