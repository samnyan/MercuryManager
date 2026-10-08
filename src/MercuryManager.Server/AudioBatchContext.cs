using System.Buffers.Binary;
using System.Security.Cryptography;
using CUE4Parse.UE4.Criware.Readers;
namespace MercuryManager.Server;
internal sealed class AudioBatchContext:IDisposable
{
    private readonly Dictionary<string,(AwbReader Reader,List<AwbWriter.Entry> Entries)> banks=[];
    public IReadOnlyList<AwbWriter.Entry> Entries(string path,AwbReader ignored)
    {
        if(!banks.TryGetValue(path,out var bank)){var reader=new AwbReader(File.OpenRead(path));bank=(reader,reader.Waves.Select(w=>new AwbWriter.Entry((ushort)w.WaveId,w.Length,()=>reader.GetWaveSubfileStream(w))).ToList());banks.Add(path,bank);}return bank.Entries;
    }
    public void Update(string bank,IReadOnlyList<AwbWriter.Entry> entries,CriUtf top,string source,string output)
    {
        banks[bank]=(banks[bank].Reader,entries.ToList());Directory.CreateDirectory(output);CueSheetWriter.Embed(source,top.Write(),Path.Combine(output,Path.GetFileName(source)));
    }
    public void Finish(string sheet,string output,Action<int,string> progress)
    {
        var top=CriUtf.Read(CueSheetWriter.Extract(new UAssetAPI.UAsset(sheet,UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19)));
        using var acb=new AcbReader(new MemoryStream(top.Write()));var hashes=CriUtf.Read(top.Blob(0,"StreamAwbHash"));var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));int n=0;
        foreach(var pair in banks){var bank=pair.Value;int port=acb.AtomCueSheetData["StreamAwb"].FindIndex(r=>Convert.ToString(r["Name"])==Path.GetFileNameWithoutExtension(pair.Key));var h=new byte[16];using(var f=File.OpenRead(pair.Key))f.ReadExactly(h);var target=Path.Combine(output,Path.GetFileName(pair.Key));int start=45+n*35/banks.Count,range=35/banks.Count;
        using(var f=File.Create(target))AwbWriter.Write(f,bank.Entries,BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(12)),bank.Reader.Subkey,(done,total)=>progress(start+(int)(done*range/Math.Max(1,total)),"awb:"+Path.GetFileName(pair.Key)));
        progress(start+range,"hash:"+Path.GetFileName(pair.Key));using(var f=File.OpenRead(target))hashes.SetBlob(port,"Hash",MD5.HashData(f));var bytes=new byte[16+bank.Entries.Count*2+(bank.Entries.Count+1)*4+2];using(var f=File.OpenRead(target))f.ReadExactly(bytes.AsSpan(0,bytes.Length-2));headers.SetBlob(port,"Header",bytes);
        using var check=new AwbReader(File.OpenRead(target));if(!check.Waves.Select(w=>w.WaveId).SequenceEqual(bank.Entries.Select(w=>(int)w.Id)))throw new InvalidDataException("Final bank IDs mismatch.");n++;}
        top.SetBlob(0,"StreamAwbHash",hashes.Write());top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());progress(85,"cuesheet");CueSheetWriter.Embed(sheet,top.Write(),Path.Combine(output,Path.GetFileName(sheet)));progress(90,"verify");if(CriAudio.List(Path.Combine(output,Path.GetFileName(sheet))).Any(c=>c.Error!=null))throw new InvalidDataException("Final Cue references invalid.");
    }
    public void Dispose(){foreach(var b in banks.Values)b.Reader.Dispose();}
}
