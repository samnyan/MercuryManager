using MercuryManager.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MercuryManager.Assets.Tests;

public class WorkspaceTests
{
    [Fact]
    public void Invalid_workspace_identifier_is_rejected()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Environment.CurrentDirectory, "mercury-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workspace-root"] = root }).Build();
            var store = new MusicWorkspaceStore(configuration);
            Assert.Throws<ArgumentException>(() => store.Get("../escape"));
            Assert.Throws<ArgumentException>(() => store.ReadRows("../escape"));
            Assert.Throws<ArgumentException>(() => store.Open(Path.Combine(root, "not-a-table.uasset")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
