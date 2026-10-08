using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
namespace MercuryManager.Server;

public static class AudioBanks
{
    public static void ValidateName(string name)
    {
        if(!name.EndsWith(".awb",StringComparison.OrdinalIgnoreCase)||name.Length>128||name[..^4].Length==0||name[..^4].Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'&&c!='-'))
            throw new ArgumentException("Bank must be an ASCII name ending in .awb.");
    }
    public static string[] List(string sheet)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAssetAPI.UAsset(sheet,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19)));
        using var acb=new AcbReader(new MemoryStream(top.Write()));return acb.AtomCueSheetData["StreamAwb"].Select(r=>Convert.ToString(r["Name"])+".awb").ToArray();
    }
    public static int Port(CriUtf top,string bank)
    {
        using var acb=new AcbReader(new MemoryStream(top.Write()));
        return acb.AtomCueSheetData["StreamAwb"].FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(bank));
    }
    // Clone schema metadata, but never retain the donor bank's hash or index.
    public static int Register(CriUtf top,string name,byte[] hash,byte[] header)
    {
        ValidateName(name);if(Port(top,name)>=0)throw new InvalidDataException("Bank already registered.");
        var hashes=CriUtf.Read(top.Blob(0,"StreamAwbHash"));var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));
        if(hashes.Rows.Count==0||hashes.Rows.Count!=headers.Rows.Count||hashes.Rows.Count>=65535)throw new InvalidDataException("Unsupported bank table schema.");
        int port=hashes.CloneRow(0),hi=headers.CloneRow(0);
        hashes.SetText(port,"Name",Path.GetFileNameWithoutExtension(name));hashes.Promote("Hash");hashes.SetBlob(port,"Hash",hash);
        headers.Promote("Header");headers.SetBlob(hi,"Header",header);
        top.SetBlob(0,"StreamAwbHash",hashes.Write());top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());
        if(Port(top,name)!=port)throw new InvalidDataException("New bank port verification failed.");return port;
    }
    public static byte[] Header(string path,int count)
    {
        var bytes=new byte[checked(16+count*2+(count+1)*4+2)];using var f=File.OpenRead(path);f.ReadExactly(bytes.AsSpan(0,bytes.Length-2));return bytes;
    }
    public static void Create(string donor,string destination)
    {
        var header=new byte[16];using(var f=File.OpenRead(donor))f.ReadExactly(header);
        using var reader=new AwbReader(File.OpenRead(donor));using var output=new FileStream(destination,FileMode.CreateNew);
        AwbWriter.Write(output,[],BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)),reader.Subkey);
    }
}
