using System.Buffers.Binary;
using MercuryManager.Server;
using Xunit;
namespace MercuryManager.Assets.Tests;
public class CriUtfTests
{
    private static byte[] Fixture()
    {
        // One constant VLData field; all offsets are relative to byte eight.
        var b=new byte[57];"@UTF"u8.CopyTo(b);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4),49);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(10),37);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12),37);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16),45);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(24),1);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(28),1);b[32]=0x3b;BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(33),2);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(41),4);"T\0Blob\0\0"u8.CopyTo(b.AsSpan(45));new byte[]{1,2,3,4}.CopyTo(b,53);return b;
    }
    [Fact]public void PreservesConstantBlobAndAllowsGrowth(){var table=CriUtf.Read(Fixture());Assert.Equal(new byte[]{1,2,3,4},table.Blob(0,"Blob"));var roundtrip=CriUtf.Read(table.Write());Assert.Equal(table.Blob(0,"Blob"),roundtrip.Blob(0,"Blob"));table.SetBlob(0,"Blob",[9,8,7,6,5]);Assert.Equal(new byte[]{9,8,7,6,5},CriUtf.Read(table.Write()).Blob(0,"Blob"));}
    [Fact]public void RejectsInvalidOffsets(){var b=Fixture();BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16),uint.MaxValue);Assert.Throws<OverflowException>(()=>CriUtf.Read(b));}
}
