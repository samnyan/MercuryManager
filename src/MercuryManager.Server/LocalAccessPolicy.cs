using System.Net;
namespace MercuryManager.Server;

/// <summary>IP allowlist for this unauthenticated local file editor. No proxy headers are trusted.</summary>
public sealed class LocalAccessPolicy
{
    private readonly HashSet<IPAddress> allowed = [];
    private readonly List<IPNetwork> allowedNetworks = [];
    public LocalAccessPolicy(IConfiguration configuration)
    {
        foreach (var value in (configuration["allowed-clients"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(value, out var network))
            {
                allowedNetworks.Add(network);
                continue;
            }
            if (IPAddress.TryParse(value, out var address))
            {
                allowed.Add(Normalize(address));
                continue;
            }
            throw new ArgumentException("allowed-clients requires comma-separated IP addresses or CIDR subnets.");
        }
    }
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    public bool Allows(IPAddress? address)
    {
        if (address is null) return false;
        var norm = Normalize(address);
        if (IPAddress.IsLoopback(norm) || allowed.Contains(norm)) return true;
        foreach (var net in allowedNetworks)
        {
            if (net.Contains(norm)) return true;
        }
        return false;
    }
}
