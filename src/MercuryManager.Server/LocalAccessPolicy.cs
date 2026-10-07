using System.Net;
namespace MercuryManager.Server;

/// <summary>IP allowlist for this unauthenticated local file editor. No proxy headers are trusted.</summary>
public sealed class LocalAccessPolicy
{
    private readonly HashSet<IPAddress> allowed = [];
    public LocalAccessPolicy(IConfiguration configuration)
    {
        foreach (var value in (configuration["allowed-clients"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(value, out var address)) throw new ArgumentException("allowed-clients requires comma-separated IP addresses.");
            allowed.Add(Normalize(address));
        }
    }
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    public bool Allows(IPAddress? address) => address is not null && (IPAddress.IsLoopback(Normalize(address)) || allowed.Contains(Normalize(address)));
}
