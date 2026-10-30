using MercuryManager.Server;
using Xunit;

public class AssetRegistryAuditTests
{
    [Fact]
    public void DetectsMissingUnregisteredMountsAndDraftOverlay()
    {
        var root=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")!,"registry-audit-"+Guid.NewGuid().ToString("N"));
        void Touch(string relative){var p=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,[]);}
        try
        {
            Touch("Mercury/Content/Existing.uasset");Touch("Mercury/Content/Extra.umap");Touch("Mercury/Content/Ignore.uexp");
            Touch("Engine/Content/EngineAsset.uasset");Touch("Engine/Plugins/Category/Plugin/Content/P.uasset");
            Touch("Mercury/Plugins/GamePlugin/Content/P.uasset");
            Touch("working/Imported/Existing.uasset");Touch("working/Resources/Existing.uasset");
            Touch("working/Resources/New.uasset");Touch("Mercury/Content/Case.uasset");
            var packages=new[]{"/Game/Existing","/Game/Existing","/Game/Missing","/Engine/EngineAsset","/Plugin/P","/GamePlugin/P","/Game/case"};
            var entries=packages.Select((p,i)=>new AssetRegistryEditor.Entry(i,p+".Object"+i,"/Game","Object",p,"Object"+i,[],[],0)).ToArray();
            var report=AssetRegistryAudit.Check(new("revision",entries),Path.Combine(root,"Mercury/Content"),Path.Combine(root,"working"));
            Assert.Equal(7,report.AssetCount);Assert.Equal(6,report.RegisteredPackages);Assert.Equal(7,report.AvailablePackages);Assert.Equal(4,report.MatchedPackages);
            Assert.Equal("/Game/Missing",Assert.Single(report.Missing).PackageName);
            Assert.Equal("/Game/case",Assert.Single(report.CaseMismatches).PackageName);
            Assert.Equal(new[]{"/Game/Case","/Game/Extra","/Game/New"},report.Unregistered.Select(x=>x.PackageName));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void RealGame_AllRegisteredPackagesExist()
    {
        var game="/media/Vostro3/Arcade/SDFE_3.07/WindowsNoEditor/Mercury";
        if(!File.Exists(Path.Combine(game,"AssetRegistry.bin")))return;
        var registry=AssetRegistryEditor.Read(File.ReadAllBytes(Path.Combine(game,"AssetRegistry.bin")));
        var report=AssetRegistryAudit.Check(registry,Path.Combine(game,"Content"),Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")!,"nonexistent-registry-audit-"+Guid.NewGuid().ToString("N")));
        Assert.Equal(8365,report.AssetCount);Assert.Equal(8362,report.RegisteredPackages);Assert.Equal(8362,report.AvailablePackages);Assert.Equal(8362,report.MatchedPackages);
        Assert.Empty(report.Missing);Assert.Empty(report.Unregistered);Assert.Empty(report.CaseMismatches);
    }
}
