using MercuryManager.Server;
using CUE4Parse.UE4.Criware.Readers;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class AudioBanksTests
{
    [Theory][InlineData("../x.awb")][InlineData("x\\y.awb")][InlineData(".awb")][InlineData("x.hca")][InlineData("x y.awb")]
    public void RejectsUnsafeNames(string name)=>Assert.Throws<ArgumentException>(()=>AudioBanks.ValidateName(name));
    [Fact]public void CreatesEmptyBankWithDonorAlignmentAndSubkey()
    {
        var dir=Path.Combine(Path.GetTempPath(),"bank-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{string donor=Path.Combine(dir,"old.awb"),target=Path.Combine(dir,"new.awb");using(var f=File.Create(donor))AwbWriter.Write(f,[new(42,3,()=>new MemoryStream([1,2,3]))],64,123);
        AudioBanks.Create(donor,target);using var reader=new AwbReader(File.OpenRead(target));Assert.Empty(reader.Waves);Assert.Equal((ushort)123,reader.Subkey);Assert.Equal(22,AudioBanks.Header(target,0).Length);Assert.Throws<IOException>(()=>AudioBanks.Create(donor,target));}
        finally{Directory.Delete(dir,true);}
    }
}
