using System.Net;
using MercuryManager.Server;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class LocalAccessPolicyTests
{
    private static LocalAccessPolicy Policy(string? clients=null)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["allowed-clients"]=clients}).Build());
    [Theory]
    [InlineData("127.0.0.1",true)]
    [InlineData("::1",true)]
    [InlineData("::ffff:127.0.0.1",true)]
    [InlineData("203.0.113.10",false)]
    public void Default_accepts_only_loopback(string ip,bool expected)=>Assert.Equal(expected,Policy().Allows(IPAddress.Parse(ip)));
    [Fact] public void Explicit_allowlist_accepts_only_selected_client()
    {
        var policy=Policy("203.0.113.10");
        Assert.True(policy.Allows(IPAddress.Parse("::ffff:203.0.113.10")));
        Assert.False(policy.Allows(IPAddress.Parse("203.0.113.11")));
        Assert.False(policy.Allows(null));
    }
    [Fact] public void Cidr_subnet_accepts_all_addresses_in_range()
    {
        var policy=Policy("100.64.0.0/10, 192.168.2.0/24");
        Assert.True(policy.Allows(IPAddress.Parse("100.106.185.5")));
        Assert.True(policy.Allows(IPAddress.Parse("100.64.0.1")));
        Assert.True(policy.Allows(IPAddress.Parse("192.168.2.32")));
        Assert.True(policy.Allows(IPAddress.Parse("::ffff:100.106.185.5")));
        Assert.False(policy.Allows(IPAddress.Parse("10.0.0.1")));
    }
    [Fact] public void Invalid_configuration_is_rejected()=>Assert.Throws<ArgumentException>(()=>Policy("not-an-ip"));
    [Fact] public void Catalog_is_unique_and_contains_only_safe_asset_names()
    {
        Assert.NotEmpty(TableCatalog.Names);
        Assert.Equal(TableCatalog.Names.Length,TableCatalog.Names.Distinct().Count());
        Assert.All(TableCatalog.Names,n=>Assert.Matches("^[A-Za-z][A-Za-z0-9]*$",n));
    }
}
