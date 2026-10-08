using System.Buffers.Binary;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;
public static class CueSheetWriter
{
    public static byte[] Extract(UAsset asset)
    {
        var e=asset.Exports.Single(x=>x.GetExportClassType().ToString()=="SoundAtomCueSheet");var b=e.Extras;
        if(b.Length<20||BinaryPrimitives.ReadUInt32LittleEndian(b)!=0)throw new InvalidDataException("Only inline uncompressed CueSheet bulk is supported.");
        int length=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4));if(length!=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8))||length!=b.Length-20)throw new InvalidDataException("Invalid CueSheet bulk length.");
        return b[20..];
    }
    public static void Embed(string source,byte[] acb,string output)
    {
        var asset=new UAsset(source,EngineVersion.VER_UE4_19);Extract(asset);
        var e=asset.Exports.Single(x=>x.GetExportClassType().ToString()=="SoundAtomCueSheet");
        e.Extras=new byte[20+acb.Length];BinaryPrimitives.WriteInt32LittleEndian(e.Extras.AsSpan(4),acb.Length);BinaryPrimitives.WriteInt32LittleEndian(e.Extras.AsSpan(8),acb.Length);acb.CopyTo(e.Extras,20);
        asset.Write(output);var measured=new UAsset(output,EngineVersion.VER_UE4_19);var m=measured.Exports.Single(x=>x.GetExportClassType().ToString()=="SoundAtomCueSheet");
        BinaryPrimitives.WriteInt64LittleEndian(e.Extras.AsSpan(12),m.SerialOffset+m.SerialSize-e.Extras.Length+20);asset.Write(output);
        var reread=new UAsset(output,EngineVersion.VER_UE4_19);if(!Extract(reread).SequenceEqual(acb)||!reread.VerifyBinaryEquality())throw new InvalidDataException("CueSheet embedding verification failed.");
    }
}
