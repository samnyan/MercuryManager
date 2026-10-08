using MercuryManager.Server;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class FileTransactionTests
{
    [Fact]public void WritesPairedFilesAndDiskBackups()
    {
        var root=Path.Combine(Path.GetTempPath(),"mercury-transaction-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{string a=Path.Combine(root,"source-a"),b=Path.Combine(root,"source-b"),x=Path.Combine(root,"target-a"),y=Path.Combine(root,"target-b");File.WriteAllText(a,"new-a");File.WriteAllText(b,"new-b");File.WriteAllText(x,"old-a");File.WriteAllText(y,"old-b");FileTransaction.Copy([(a,x),(b,y)],true);Assert.Equal("new-a",File.ReadAllText(x));Assert.Equal("new-b",File.ReadAllText(y));Assert.Equal("old-a",File.ReadAllText(x+"_bak"));Assert.Equal("old-b",File.ReadAllText(y+"_bak"));}finally{Directory.Delete(root,true);}
    }
    [Fact]public void MissingSecondSourceDoesNotPublishFirstFile()
    {
        var root=Path.Combine(Path.GetTempPath(),"mercury-transaction-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{string a=Path.Combine(root,"source"),x=Path.Combine(root,"target");File.WriteAllText(a,"new");File.WriteAllText(x,"old");Assert.Throws<FileNotFoundException>(()=>FileTransaction.Copy([(a,x),(Path.Combine(root,"missing"),Path.Combine(root,"other"))],false));Assert.Equal("old",File.ReadAllText(x));Assert.Equal(2,Directory.GetFiles(root).Length);}finally{Directory.Delete(root,true);}
    }
}
